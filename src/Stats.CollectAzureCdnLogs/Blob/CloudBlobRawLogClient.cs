// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.RetryPolicies;

namespace Stats.CollectAzureCdnLogs.Blob
{
    internal sealed class CloudBlobRawLogClient
    {
        private readonly JobEventSource _jobEventSource;
        private readonly CloudStorageAccount _cloudStorageAccount;

        public CloudBlobRawLogClient(JobEventSource jobEventSource, CloudStorageAccount cloudStorageAccount)
        {
            _jobEventSource = jobEventSource;
            _cloudStorageAccount = cloudStorageAccount;
        }

        public async Task<CloudBlobContainer> CreateContainerIfNotExistsAsync(string containerName)
        {
            var cloudBlobClient = _cloudStorageAccount.CreateCloudBlobClient();
            cloudBlobClient.DefaultRequestOptions.RetryPolicy = new ExponentialRetry(TimeSpan.FromSeconds(10), 5);

            var cloudBlobContainer = cloudBlobClient.GetContainerReference(containerName);
            await cloudBlobContainer.CreateIfNotExistsAsync();

            return cloudBlobContainer;
        }

        public async Task<bool> UploadBlobAsync(CloudBlobContainer targetContainer, RawLogFileInfo logFile, Stream rawLogStream)
        {
            try
            {
                var blobName = logFile.Uri.ToString();
                _jobEventSource.BeginningBlobUpload(blobName);
                Trace.TraceInformation("Uploading file '{0}'.", logFile.FileName);

                var blob = targetContainer.GetBlockBlobReference(logFile.FileName);
                blob.Properties.ContentType = logFile.ContentType;

                // 3. Upload the file using the original file name.
                await blob.UploadFromStreamAsync(rawLogStream);

                Trace.TraceInformation("Finished uploading file '{0}' to '{1}'.", logFile.FileName, blob.Uri.AbsoluteUri);
                _jobEventSource.FinishingBlobUpload(blobName);
                return true;
            }
            catch (Exception exception)
            {
                Trace.TraceError(exception.ToString());
            }
            return false;
        }
    }
}