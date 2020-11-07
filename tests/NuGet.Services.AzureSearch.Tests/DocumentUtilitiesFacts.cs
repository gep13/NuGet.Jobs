// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Linq;
using Xunit;

namespace NuGet.Services.AzureSearch
{
    public class DocumentUtilitiesFacts
    {
        public class GetHijackDocumentKey
        {
            [Theory]
            [InlineData("NuGet.Versioning", "1.0.0", "nuget_versioning_1_0_0-bnVnZXQudmVyc2lvbmluZy8xLjAuMA")]
            [InlineData("nuget.versioning", "1.0.0", "nuget_versioning_1_0_0-bnVnZXQudmVyc2lvbmluZy8xLjAuMA")]
            [InlineData("NUGET.VERSIONING", "1.0.0", "nuget_versioning_1_0_0-bnVnZXQudmVyc2lvbmluZy8xLjAuMA")]
            [InlineData("_", "1.0.0", "1_0_0-Xy8xLjAuMA")]
            [InlineData("foo-bar", "1.0.0", "foo-bar_1_0_0-Zm9vLWJhci8xLjAuMA")]
            [InlineData("İzmir", "1.0.0", "zmir_1_0_0-xLB6bWlyLzEuMC4w")]
            [InlineData("İİzmir", "1.0.0", "zmir_1_0_0-xLDEsHptaXIvMS4wLjA")]
            [InlineData("zİİmir", "1.0.0", "z__mir_1_0_0-esSwxLBtaXIvMS4wLjA")]
            [InlineData("zmirİ", "1.0.0", "zmir__1_0_0-em1pcsSwLzEuMC4w")]
            [InlineData("zmirİİ", "1.0.0", "zmir___1_0_0-em1pcsSwxLAvMS4wLjA")]
            [InlineData("惡", "1.0.0", "1_0_0-5oOhLzEuMC4w")]
            [InlineData("jQuery", "1.0.0-alpha", "jquery_1_0_0-alpha-anF1ZXJ5LzEuMC4wLWFscGhh")]
            [InlineData("jQuery", "1.0.0-Alpha", "jquery_1_0_0-alpha-anF1ZXJ5LzEuMC4wLWFscGhh")]
            [InlineData("jQuery", "1.0.0-ALPHA", "jquery_1_0_0-alpha-anF1ZXJ5LzEuMC4wLWFscGhh")]
            [InlineData("jQuery", "1.0.0-ALPHA.1", "jquery_1_0_0-alpha_1-anF1ZXJ5LzEuMC4wLWFscGhhLjE")]
            public void EncodesHijackDocumentKey(string id, string version, string expected)
            {
                var actual = DocumentUtilities.GetHijackDocumentKey(id, version);

                Assert.Equal(expected, actual);
            }
        }

        public class GetSearchDocumentKey
        {
            [Theory]
            [InlineData("NuGet.Versioning", "nuget_versioning-bnVnZXQudmVyc2lvbmluZw")]
            [InlineData("nuget.versioning", "nuget_versioning-bnVnZXQudmVyc2lvbmluZw")]
            [InlineData("NUGET.VERSIONING", "nuget_versioning-bnVnZXQudmVyc2lvbmluZw")]
            [InlineData("_", "Xw")]
            [InlineData("__", "X18")]
            [InlineData("___", "X19f")]
            [InlineData("____", "X19fXw")]
            [InlineData("_____", "X19fX18")]
            [InlineData("______", "X19fX19f")]
            [InlineData("_______", "X19fX19fXw")]
            [InlineData("________", "X19fX19fX18")]
            [InlineData("foo-bar", "foo-bar-Zm9vLWJhcg")]
            [InlineData("İzmir", "zmir-xLB6bWly")]
            [InlineData("İİzmir", "zmir-xLDEsHptaXI")]
            [InlineData("zİİmir", "z__mir-esSwxLBtaXI")]
            [InlineData("zmirİ", "zmir_-em1pcsSw")]
            [InlineData("zmirİİ", "zmir__-em1pcsSwxLA")]
            [InlineData("惡", "5oOh")]
            public void EncodesSearchDocumentKey(string id, string expected)
            {
                foreach (var searchFilters in Enum.GetValues(typeof(SearchFilters)).Cast<SearchFilters>())
                {
                    var actual = DocumentUtilities.GetSearchDocumentKey(id, searchFilters);

                    Assert.Equal(expected + "-" + searchFilters, actual);
                }
            }
        }
    }
}
