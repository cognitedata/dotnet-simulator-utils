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

        private Mock<AutomationClient> CreateMockClient(bool preShutdownThrows = false, string programId = "Test.Program")
        {
            AutomationConfig config = new AutomationConfig { ProgramId = programId };
            var mock = new Mock<AutomationClient>(_mockLogger.Object, config) { CallBase = true };

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

        [WindowsOnlyFact]
        public void Initialize_WhenServerTypeNotFound_ThrowsAndLogsError()
        {
            var mockClient = CreateMockClient(programId: "NonExistent.Application.12345");

            var exception = Assert.Throws<SimulatorConnectionException>(() => mockClient.Object.Initialize());

            Assert.Equal("Cannot connect to automation server", exception.Message);
            VerifyLog(_mockLogger, LogLevel.Error, "Could not find automation server using the id", Times.Once(), true);
            Assert.NotNull(exception.InnerException);
        }

        [Fact]
        public void SimulatorConnectionException_AllConstructors_WorkCorrectly()
        {
            var defaultException = new SimulatorConnectionException();
            Assert.NotNull(defaultException);
            Assert.Null(defaultException.InnerException);

            var messageException = new SimulatorConnectionException("Test error");
            Assert.Equal("Test error", messageException.Message);
            Assert.Null(messageException.InnerException);

            var innerEx = new InvalidOperationException("Inner error");
            var fullException = new SimulatorConnectionException("Outer error", innerEx);
            Assert.Equal("Outer error", fullException.Message);
            Assert.Same(innerEx, fullException.InnerException);
        }
    }
}
