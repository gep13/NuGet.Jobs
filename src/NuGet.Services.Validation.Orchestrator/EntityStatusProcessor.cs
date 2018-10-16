﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using NuGet.Services.Validation.Orchestrator.Telemetry;
using NuGetGallery;
using NuGetGallery.Packaging;

namespace NuGet.Services.Validation.Orchestrator
{
    public class EntityStatusProcessor<T> : IStatusProcessor<T> where T : class, IEntity
    {
        private readonly IEntityService<T> _galleryPackageService;
        private readonly IValidationFileService _packageFileService;
        private readonly IValidatorProvider _validatorProvider;
        private readonly ITelemetryService _telemetryService;
        private readonly ILogger<EntityStatusProcessor<T>> _logger;

        public EntityStatusProcessor(
            IEntityService<T> galleryPackageService,
            IValidationFileService packageFileService,
            IValidatorProvider validatorProvider,
            ITelemetryService telemetryService,
            ILogger<EntityStatusProcessor<T>> logger)
        {
            _galleryPackageService = galleryPackageService ?? throw new ArgumentNullException(nameof(galleryPackageService));
            _packageFileService = packageFileService ?? throw new ArgumentNullException(nameof(packageFileService));
            _validatorProvider = validatorProvider ?? throw new ArgumentNullException(nameof(validatorProvider));
            _telemetryService = telemetryService ?? throw new ArgumentNullException(nameof(telemetryService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public Task<SetStatusResult> SetStatusAsync(
            IValidatingEntity<T> validatingEntity,
            PackageValidationSet validationSet,
            PackageStatus status)
        {
            if (validatingEntity == null)
            {
                throw new ArgumentNullException(nameof(validatingEntity));
            }

            if (validationSet == null)
            {
                throw new ArgumentNullException(nameof(validationSet));
            }

            if (validatingEntity.Status == PackageStatus.Deleted)
            {
                throw new ArgumentException(
                    $"A package in the {nameof(PackageStatus.Deleted)} state cannot be processed.",
                    nameof(validatingEntity));
            }

            if (validatingEntity.Status == PackageStatus.Available &&
                status == PackageStatus.FailedValidation)
            {
                throw new ArgumentException(
                    $"A package cannot transition from {nameof(PackageStatus.Available)} to {nameof(PackageStatus.FailedValidation)}.",
                    nameof(status));
            }

            switch (status)
            {
                case PackageStatus.Available:
                    return TryMakePackageAvailableAsync(validatingEntity, validationSet);
                case PackageStatus.FailedValidation:
                    return MakePackageFailedValidationAsync(validatingEntity, validationSet);
                default:
                    throw new ArgumentException(
                        $"A package can only transition to the {nameof(PackageStatus.Available)} or " +
                        $"{nameof(PackageStatus.FailedValidation)} states.", nameof(status));
            }
        }

        private async Task<SetStatusResult> MakePackageFailedValidationAsync(IValidatingEntity<T> validatingEntity, PackageValidationSet validationSet)
        {
            var fromStatus = validatingEntity.Status;

            await _galleryPackageService.UpdateStatusAsync(validatingEntity.EntityRecord, PackageStatus.FailedValidation, commitChanges: true);

            if (fromStatus != PackageStatus.FailedValidation)
            {
                _telemetryService.TrackPackageStatusChange(fromStatus, PackageStatus.FailedValidation);
            }

            return SetStatusResult.Completed;
        }

        /// <summary>
        /// Attempt to make the validation set's package available. If the package desination has been modified since the
        /// validation set has been created, this operation will cancel itself.
        /// </summary>
        /// <param name="validatingEntity">The entity that should be made available</param>
        /// <param name="validationSet">The set that validated the entity</param>
        /// <returns>True if the package was made available, false if the operation is cancelled.</returns>
        private async Task<SetStatusResult> TryMakePackageAvailableAsync(IValidatingEntity<T> validatingEntity, PackageValidationSet validationSet)
        {
            // 1) Operate on blob storage.
            var updateResult = await TryUpdatePublicPackageAsync(validationSet);
            if (updateResult == UpdatePublicPackageResult.AccessConditionFailed)
            {
                // The package copy will fail if the public destination has been modified since the validation set was created. This may happen
                // if a package has multiple validations in parallel. If so, the first validation set to copy will "win" and all other validations
                // should gracefully "cancel" themselves.
                if (validatingEntity.Status != PackageStatus.Available)
                {
                    // The entity is updated after the public package is updated, so this case happens if the other validation has updated the package
                    // but hasn't updated the entity yet. We will throw and retry later. Eventually, this will deadletter if the parallel validation
                    // doesn't update the entity.
                    throw new InvalidOperationException(
                        $"Package {validationSet.PackageId} {validationSet.PackageNormalizedVersion} is public, but isn't marked as available!");
                }

                _logger.LogWarning("Cancelling validation set {ValidationSetId} as package {PackageId} {PackageVersion} has been modified",
                    validationSet.ValidationTrackingId,
                    validationSet.PackageId,
                    validationSet.PackageNormalizedVersion);

                return SetStatusResult.Cancelled;
            }

            // 2) Update the package's blob metadata in the packages blob storage container.
            var metadata = await _packageFileService.UpdatePackageBlobMetadataAsync(validationSet);

            // 3) Operate on the database.
            var fromStatus = await MarkPackageAsAvailableAsync(validationSet, validatingEntity, metadata, updateResult);

            // 4) Emit telemetry and clean up.
            if (fromStatus != PackageStatus.Available)
            {
                _telemetryService.TrackPackageStatusChange(fromStatus, PackageStatus.Available);

                _logger.LogInformation("Deleting from the source for package {PackageId} {PackageVersion}, validation set {ValidationSetId}",
                    validationSet.PackageId,
                    validationSet.PackageNormalizedVersion,
                    validationSet.ValidationTrackingId);

                await _packageFileService.DeleteValidationPackageFileAsync(validationSet);
            }

            // 4) Verify the package still exists (we've had bugs here before).
            if (validatingEntity.Status == PackageStatus.Available
                && !await _packageFileService.DoesPackageFileExistAsync(validationSet))
            {
                var validationPackageAvailable = await _packageFileService.DoesValidationPackageFileExistAsync(validationSet);

                _logger.LogWarning("Package {PackageId} {PackageVersion} is marked as available, but does not exist " +
                    "in public container. Does package exist in validation container: {ExistsInValidation}",
                    validationSet.PackageId,
                    validationSet.PackageNormalizedVersion,
                    validationPackageAvailable);

                // Report missing package, don't try to fix up anything. This shouldn't happen and needs an investigation.
                _telemetryService.TrackMissingNupkgForAvailablePackage(
                    validationSet.PackageId,
                    validationSet.PackageNormalizedVersion,
                    validationSet.ValidationTrackingId.ToString());
            }

            return SetStatusResult.Completed;
        }

        private async Task<PackageStatus> MarkPackageAsAvailableAsync(
            PackageValidationSet validationSet,
            IValidatingEntity<T> validatingEntity,
            PackageStreamMetadata streamMetadata,
            UpdatePublicPackageResult updateResult)
        {
            // Use whatever package made it into the packages container. This is what customers will consume so the DB
            // record must match.

            // We don't immediately commit here. Later, we will commit these changes as well as the new package
            // status as part of the same transaction.
            await _galleryPackageService.UpdateMetadataAsync(
                    validatingEntity.EntityRecord,
                    streamMetadata,
                    commitChanges: false);

            _logger.LogInformation("Marking package {PackageId} {PackageVersion}, validation set {ValidationSetId} as {PackageStatus} in DB",
                validationSet.PackageId,
                validationSet.PackageNormalizedVersion,
                validationSet.ValidationTrackingId,
                PackageStatus.Available);

            var fromStatus = validatingEntity.Status;

            try
            {
                // Make the package available and commit any other pending changes (e.g. updated hash).
                await _galleryPackageService.UpdateStatusAsync(validatingEntity.EntityRecord, PackageStatus.Available, commitChanges: true);
            }
            catch (Exception e)
            {
                _logger.LogError(
                    Error.UpdatingPackageDbStatusFailed,
                    e,
                    "Failed to update package status in Gallery Db. Package {PackageId} {PackageVersion}, validation set {ValidationSetId}",
                    validationSet.PackageId,
                    validationSet.PackageNormalizedVersion,
                    validationSet.ValidationTrackingId);

                // If this execution was not the one to copy the package, then don't delete the package on failure.
                // This prevents a missing passing in the (unlikely) case where two actors attempt the DB update, one
                // succeeds and one fails. We don't want an available package record with nothing in the packages
                // container!
                if (updateResult == UpdatePublicPackageResult.Copied && fromStatus != PackageStatus.Available)
                {
                    await _packageFileService.DeletePackageFileAsync(validationSet);
                }

                throw;
            }

            return fromStatus;
        }

        private async Task<UpdatePublicPackageResult> TryUpdatePublicPackageAsync(PackageValidationSet validationSet)
        {
            _logger.LogInformation("Copying .nupkg to public storage for package {PackageId} {PackageVersion}, validation set {ValidationSetId}",
                validationSet.PackageId,
                validationSet.PackageNormalizedVersion,
                validationSet.ValidationTrackingId);

            // If the validation set contains any processors, we must use the copy of the package that is specific to
            // this validation set. We can't use the original validation package because it does not have any of the
            // changes that the processors made. If the validation set package does not exist for some reason and there
            // are processors in the validation set, this indicates a bug and an exception will be thrown by the copy
            // operation below. This will cause the validation queue message to eventually dead-letter at which point
            // the on-call person should investigate.
            if (validationSet.PackageValidations.Any(x => _validatorProvider.IsProcessor(x.Type)) ||
                await _packageFileService.DoesValidationSetPackageExistAsync(validationSet))
            {
                IAccessCondition destAccessCondition;

                // The package etag will be null if this validation set is expecting the package to not yet exist in
                // the packages container.
                if (validationSet.PackageETag == null)
                {
                    // This will fail with HTTP 409 if the package already exists. This means that another validation
                    // set has completed and moved the package into the Available state first, with different package
                    // content.
                    destAccessCondition = AccessConditionWrapper.GenerateIfNotExistsCondition();

                    _logger.LogInformation(
                        "Attempting to copy validation set {ValidationSetId} package {PackageId} {PackageVersion} to" +
                        " the packages container, assuming that the package does not already exist.",
                        validationSet.ValidationTrackingId,
                        validationSet.PackageId,
                        validationSet.PackageNormalizedVersion);
                }
                else
                {
                    // This will fail with HTTP 412 if the package has been modified by another validation set. This
                    // would only happen if this validation set and another validation set are operating on a package
                    // already in the Available state.
                    destAccessCondition = AccessConditionWrapper.GenerateIfMatchCondition(validationSet.PackageETag);

                    _logger.LogInformation(
                        "Attempting to copy validation set {ValidationSetId} package {PackageId} {PackageVersion} to" +
                        " the packages container, assuming that the package has etag {PackageETag}.",
                        validationSet.ValidationTrackingId,
                        validationSet.PackageId,
                        validationSet.PackageNormalizedVersion,
                        validationSet.PackageETag);
                }

                // Failures here should result in an exception. This means that this validation set has
                // modified the package but is unable to copy the modified package into the packages container because
                // another validation set completed first.
                try
                {
                    await _packageFileService.CopyValidationSetPackageToPackageFileAsync(
                        validationSet,
                        destAccessCondition);
                }
                catch (Exception ex) when (IsAccessConditionFailedException(ex))
                {
                    _logger.LogError(
                        Error.UpdatingPackageAccessConditionFailed,
                        ex,
                        "Copying validation set {ValidationSetId} package {PackageId} {PackageVersion} failed as etag" +
                        " {PackageETag} is no longer valid",
                        validationSet.ValidationTrackingId,
                        validationSet.PackageId,
                        validationSet.PackageNormalizedVersion,
                        validationSet.PackageETag);

                    return UpdatePublicPackageResult.AccessConditionFailed;
                }

                return UpdatePublicPackageResult.Copied;
            }
            else
            {
                _logger.LogInformation(
                    "The package specific to the validation set does not exist. Falling back to the validation " +
                    "container for package {PackageId} {PackageVersion}, validation set {ValidationSetId}",
                    validationSet.PackageId,
                    validationSet.PackageNormalizedVersion,
                    validationSet.ValidationTrackingId);

                try
                {
                    await _packageFileService.CopyValidationPackageToPackageFileAsync(validationSet);

                    return UpdatePublicPackageResult.Copied;
                }
                catch (InvalidOperationException)
                {
                    // The package already exists in the packages container. This can happen if the DB commit below fails
                    // and this flow is retried or another validation set for the package completed first. Either way, we
                    // will later attempt to use the hash from the package in the packages container (the destination).
                    // In other words, we don't care which copy wins when copying from the validation package because
                    // we know the package has not been modified.
                    _logger.LogInformation(
                        "Package already exists in packages container for {PackageId} {PackageVersion}, validation set {ValidationSetId}",
                        validationSet.PackageId,
                        validationSet.PackageNormalizedVersion,
                        validationSet.ValidationTrackingId);

                    return UpdatePublicPackageResult.Skipped;
                }
            }
        }

        private bool IsAccessConditionFailedException(Exception e)
        {
            // See https://github.com/NuGet/NuGetGallery/blob/master/src/NuGetGallery.Core/Services/CloudBlobCoreFileStorageService.cs#L247
            if (e is FileAlreadyExistsException)
            {
                return true;
            }

            if (e is StorageException storageException)
            {
                if (storageException.IsFileAlreadyExistsException() || storageException.IsPreconditionFailedException())
                {
                    return true;
                }
            }

            return false;
        }
    }
}
