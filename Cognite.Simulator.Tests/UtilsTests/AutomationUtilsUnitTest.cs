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

        public AutomationUtilsUnitTest()
        {
            _mockLogger = new Mock<ILogger<AutomationClient>>();
        }

        private Mock<AutomationClient> CreateMockClient(string programId = "Test.Program")
        {
            AutomationConfig config = new AutomationConfig { ProgramId = programId };
            return new Mock<AutomationClient>(_mockLogger.Object, config) { CallBase = true };
        }

        [WindowsOnlyFact]
        public void Shutdown_WhenServerIsNull_CallsPreShutdownButSkipsComRelease()
        {
            var mockClient = CreateMockClient();

            mockClient.Object.Shutdown();

            mockClient.Protected().Verify("PreShutdown", Times.Once());
            VerifyLog(_mockLogger, LogLevel.Debug, "Released COM Object", Times.Never(), true);
            VerifyLog(_mockLogger, LogLevel.Debug, "Automation server instance removed", Times.Once(), true);
        }

        [WindowsOnlyFact]
        public void Shutdown_WhenPreShutdownThrows_AndServerIsNull_SkipsComRelease()
        {
            var mockClient = CreateMockClient();
            mockClient.Protected()
                .Setup("PreShutdown")
                .Throws(new InvalidOperationException("PreShutdown failed"));

            var exception = Record.Exception(() => mockClient.Object.Shutdown());

            Assert.NotNull(exception);
            Assert.Equal("PreShutdown failed", exception.Message);
            mockClient.Protected().Verify("PreShutdown", Times.Once());
            VerifyLog(_mockLogger, LogLevel.Debug, "Released COM Object", Times.Never(), true);
            VerifyLog(_mockLogger, LogLevel.Debug, "Automation server instance removed", Times.Never(), true);
        }

        [WindowsOnlyFact]
        public void Shutdown_WhenPreShutdownThrows_AndServerIsNotNull_ReleasesComObject()
        {
            var mockServer = new object();
            var mockClient = CreateMockClient();

            mockClient
                .Protected()
                .Setup<Type>("GetServerType", ItExpr.IsAny<string>())
                .Returns(typeof(object));

            mockClient
                .Protected()
                .Setup<dynamic>("CreateServerInstance", ItExpr.IsAny<Type>())
                .Returns(mockServer);

            mockClient
                .Protected()
                .Setup("ReleaseComObject", ItExpr.IsAny<object>());

            mockClient.Protected()
                .Setup("PreShutdown")
                .Throws(new InvalidOperationException("PreShutdown failed"));

            mockClient.Object.Initialize();

            var exception = Record.Exception(() => mockClient.Object.Shutdown());

            Assert.NotNull(exception);
            Assert.Equal("PreShutdown failed", exception.Message);
            mockClient.Protected().Verify("PreShutdown", Times.Once());
            mockClient.Protected().Verify("ReleaseComObject", Times.Once(), ItExpr.IsAny<object>());
            VerifyLog(_mockLogger, LogLevel.Debug, "Released COM Object", Times.Once(), true);
        }

        [WindowsOnlyFact]
        public void Initialize_WhenServerTypeNotFound_ThrowsAndLogsError()
        {
            var mockClient = CreateMockClient(programId: "NonExistent.Application.12345");

            var exception = Assert.Throws<SimulatorConnectionException>(() => mockClient.Object.Initialize());

            Assert.Equal("Cannot connect to automation server", exception.Message);
            VerifyLog(_mockLogger, LogLevel.Error, "Could not find automation server using the id", Times.Once(), true);
            Assert.NotNull(exception.InnerException);
            Assert.Equal("Cannot connect to get automation server type", exception.InnerException.Message);
        }

        [WindowsOnlyFact]
        public void Initialize_WhenAlreadyConnected_SkipsReconnection()
        {
            var mockServer = new object();
            var mockClient = CreateMockClient();

            mockClient
                .Protected()
                .Setup<Type>("GetServerType", ItExpr.IsAny<string>())
                .Returns(typeof(object));

            mockClient
                .Protected()
                .Setup<dynamic>("CreateServerInstance", ItExpr.IsAny<Type>())
                .Returns(mockServer);

            var client = mockClient.Object;

            client.Initialize();
            client.Initialize();

            mockClient
               .Protected()
               .Verify<dynamic>("CreateServerInstance", Times.Once(), ItExpr.IsAny<Type>());

            VerifyLog(_mockLogger, LogLevel.Debug, "Connected to simulator instance", Times.Once(), true);
        }
    }
}
