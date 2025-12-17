using System;

using Cognite.Simulator.Utils.Automation;

using Microsoft.Extensions.Logging;

using Moq;
using Moq.Protected;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    [Collection(nameof(SequentialTestCollection))]
    public class AutomationUtilsUnitTest
    {
        private readonly Mock<ILogger<AutomationClient>> _mockLogger;
        private readonly AutomationConfig _config;

        public AutomationUtilsUnitTest()
        {
            _mockLogger = new Mock<ILogger<AutomationClient>>();
            _config = new AutomationConfig { ProgramId = "Test.Program" };
        }

        private Mock<AutomationClient> CreateMockClient(bool preShutdownThrows = false)
        {
            var mock = new Mock<AutomationClient>(_mockLogger.Object, _config) { CallBase = true };

            if (preShutdownThrows)
            {
                mock.Protected()
                    .Setup("PreShutdown")
                    .Throws(new InvalidOperationException("PreShutdown failed"));
            }

            return mock;
        }

        [Fact]
        public void Shutdown_WhenServerIsNull_CallsPreShutdownButSkipsComRelease()
        {
            var mockClient = CreateMockClient();

            mockClient.Object.Shutdown();

            mockClient.Protected().Verify("PreShutdown", Times.Once());
            VerifyLog(_mockLogger, LogLevel.Debug, "Released COM Object", Times.Never(), true);
            VerifyLog(_mockLogger, LogLevel.Debug, "Automation server instance removed", Times.Once(), true);
        }

        [WindowsOnlyFact]
        public void Initialize_WhenAlreadyConnected_SkipsReconnection()
        {
            var mockClient = CreateMockClient();

            mockClient.Object.Initialize();
            _mockLogger.Invocations.Clear();

            mockClient.Object.Initialize();

            VerifyLog(_mockLogger, LogLevel.Debug, "Connecting to automation server", Times.Never(), true);
        }

        [Fact]
        public void Shutdown_WhenPreShutdownThrows_ExecutesFinallyBlock()
        {
            var mockClient = CreateMockClient(preShutdownThrows: true);

            var exception = Record.Exception(() => mockClient.Object.Shutdown());

            Assert.NotNull(exception);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Equal("PreShutdown failed", exception.Message);
            mockClient.Protected().Verify("PreShutdown", Times.Once());
            VerifyLog(_mockLogger, LogLevel.Debug, "Released COM Object", Times.Never(), true);
        }
    }
}
