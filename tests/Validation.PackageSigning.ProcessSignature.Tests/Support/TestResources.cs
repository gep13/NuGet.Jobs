﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.IO;
using NuGet.Packaging.Signing;

namespace Validation.PackageSigning.ProcessSignature.Tests
{
    public static class TestResources
    {
        private const string ResourceNamespace = "Validation.PackageSigning.ProcessSignature.Tests.TestData";
        public const string SignedPackageLeaf1 = ResourceNamespace + ".TestSigned.leaf-1.1.0.0.nupkg";
        public const string SignedPackageLeaf2 = ResourceNamespace + ".TestSigned.leaf-2.2.0.0.nupkg";
        public const string UnsignedPackage = ResourceNamespace + ".TestUnsigned.1.0.0.nupkg";
        public const string Zip64Package = ResourceNamespace + ".Zip64Package.1.0.0.nupkg";

        /// <summary>
        /// This is the SHA-256 thumbprint of the root CA certificate for the signing certificate of <see cref="SignedPackageLeaf1"/>.
        /// </summary>
        public const string RootThumbprint = "557276839c961df211cf267b318d880568676efa41e8b62d9bb38752c1d6214d";

        /// <summary>
        /// This is the SHA-256 thumbprint of the signing certificate in <see cref="SignedPackageLeaf1"/>.
        /// </summary>
        public const string Leaf1Thumbprint = "4456d5d38709876dcd20ef3d7ba98bfd79fcaee91141d153a55f10841ef909c6";

        /// <summary>
        /// This is the SHA-256 thumbprint of the signing certificate in <see cref="SignedPackageLeaf2"/>.
        /// </summary>
        public const string Leaf2Thumbprint = "a8cc70dbbd8bc61410231805b690cca7c5a8d07553c1c49b299a6aabaeb7ff9a";

        /// <summary>
        /// This is the SHA-256 thumbprint of the timestamp certificate in <see cref="SignedPackageLeaf1"/>.
        /// </summary>
        public const string Leaf1TimestampThumbprint = "cf7ac17ad047ecd5fdc36822031b12d4ef078b6f2b4c5e6ba41f8ff2cf4bad67";

        public static SignedPackageArchive SignedPackageLeaf1Reader => LoadPackage(SignedPackageLeaf1);
        public static SignedPackageArchive SignedPackageLeaf2Reader => LoadPackage(SignedPackageLeaf2);

        /// <summary>
        /// Buffer the resource stream into memory so the caller doesn't have to dispose.
        /// </summary>
        public static MemoryStream GetResourceStream(string resourceName)
        {
            var resourceStream = typeof(TestResources)
                .Assembly
                .GetManifestResourceStream(resourceName);

            if (resourceStream == null)
            {
                return null;
            }

            var bufferedStream = new MemoryStream();
            using (resourceStream)
            {
                resourceStream.CopyTo(bufferedStream);
            }

            bufferedStream.Position = 0;
            return bufferedStream;
        }

        public static SignedPackageArchive LoadPackage(string resourceName)
        {
            var resourceStream = GetResourceStream(resourceName);
            if (resourceStream == null)
            {
                return null;
            }

            return new SignedPackageArchive(resourceStream, resourceStream);
        }
    }
}