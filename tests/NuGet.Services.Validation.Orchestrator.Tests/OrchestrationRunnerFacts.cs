﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using NuGet.Services.ServiceBus;
using Xunit;

namespace NuGet.Services.Validation.Orchestrator.Tests
{
    public class OrchestrationRunnerFacts
    {
        [Fact]
        public async Task StartsMessageProcessing()
        {
            var runner = CreateRunner();
            await runner.RunOrchestrationAsync();

            SubscriptionProcessorMock.Verify(o => o.Start(), Times.Once());
        }

        [Fact]
        public async Task ShutsDownMessageProcessing()
        {
            var startCalled = false;
            SubscriptionProcessorMock
                .Setup(o => o.Start())
                .Callback(() => startCalled = true);

            SubscriptionProcessorMock
                .Setup(o => o.StartShutdownAsync())
                .Callback(() => Assert.True(startCalled))
                .Returns(Task.FromResult(0));
            var runner = CreateRunner();
            await runner.RunOrchestrationAsync();

            SubscriptionProcessorMock.Verify(o => o.StartShutdownAsync(), Times.Once());
        }

        [Fact(Skip = "Flaky test. Won't run it as part of CI.")]
        public async Task WaitsOrchestratorToShutDown()
        {
            SetupOptionsAccessorMock(TimeSpan.Zero, TimeSpan.FromSeconds(3));

            int numberOfRequestsInProgress = 2;
            SubscriptionProcessorMock
                .SetupGet(o => o.NumberOfMessagesInProgress)
                .Returns(() => numberOfRequestsInProgress--);

            var runner = CreateRunner();
            await runner.RunOrchestrationAsync();

            SubscriptionProcessorMock.Verify(o => o.NumberOfMessagesInProgress, Times.AtLeast(3));
        }

        [Fact]
        public async Task SendsShutdownNotificationAfterRunningValidations()
        {
            bool startedProcessing = false;
            SubscriptionProcessorMock
                .Setup(sp => sp.Start())
                .Callback(() => startedProcessing = true);

            ShutdownNotificationProviderMock
                .Setup(snp => snp.NotifyShutdownInitiated())
                .Callback(() => Assert.True(startedProcessing));

            var runner = CreateRunner();
            await runner.RunOrchestrationAsync();

            ShutdownNotificationProviderMock
                .Verify(snp => snp.NotifyShutdownInitiated());
        }

        [Fact(Skip = "Time based test, will not run in CI")]
        public async Task DoesNotBlockOnWaitingForSubscriptionProcessorToInitiateShutdown()
        {
            SubscriptionProcessorMock
                .Setup(sp => sp.StartShutdownAsync())
                .Returns(Task.Delay(TimeSpan.FromDays(1))); // veeeery long shutdown

            var runner = CreateRunner();
            var orchestrationTask = runner.RunOrchestrationAsync();
            var delayTask = Task.Delay(TimeSpan.FromSeconds(5));

            var didNotWait = await Task.WhenAny(orchestrationTask, delayTask) == orchestrationTask;

            Assert.True(didNotWait);
            SubscriptionProcessorMock
                .Verify(sp => sp.StartShutdownAsync(), Times.AtLeastOnce());
        }

        private Mock<IOptionsSnapshot<OrchestrationRunnerConfiguration>> SetupOptionsAccessorMock(
            TimeSpan processRecycleInterval,
            TimeSpan shutdownWaitInterval)
        {
            OrchestrationRunnerConfigurationAccessorMock = new Mock<IOptionsSnapshot<OrchestrationRunnerConfiguration>>();
            OrchestrationRunnerConfigurationAccessorMock
                .SetupGet(o => o.Value)
                .Returns(new OrchestrationRunnerConfiguration
                {
                    ProcessRecycleInterval = processRecycleInterval,
                    ShutdownWaitInterval = shutdownWaitInterval
                });
            return OrchestrationRunnerConfigurationAccessorMock;
        }

        private OrchestrationRunner CreateRunner()
            => new OrchestrationRunner(
                SubscriptionProcessorMock.Object,
                OrchestrationRunnerConfigurationAccessorMock.Object,
                ShutdownNotificationProviderMock.Object,
                LoggerMock.Object);

        public OrchestrationRunnerFacts()
        {
            SubscriptionProcessorMock = new Mock<ISubscriptionProcessor<PackageValidationMessageData>>();
            LoggerMock = new Mock<ILogger<OrchestrationRunner>>();
            ShutdownNotificationProviderMock = new Mock<IShutdownNotificationProvider>();
            SetupOptionsAccessorMock(TimeSpan.Zero, TimeSpan.Zero);
        }

        private Mock<ISubscriptionProcessor<PackageValidationMessageData>> SubscriptionProcessorMock { get; }
        private Mock<ILogger<OrchestrationRunner>> LoggerMock { get; }
        private Mock<IShutdownNotificationProvider> ShutdownNotificationProviderMock { get; }
        private Mock<IOptionsSnapshot<OrchestrationRunnerConfiguration>> OrchestrationRunnerConfigurationAccessorMock { get; set;  }
    }
}
