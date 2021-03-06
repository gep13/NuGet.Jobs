﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Jobs.Monitoring.PackageLag
{
    public interface IHttpClientWrapper
    {
        Task<IHttpResponseMessageWrapper> GetAsync(string requestUri, HttpCompletionOption completionOption, CancellationToken cancellationToken);
    }
}
