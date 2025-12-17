using System;
using System.Runtime.InteropServices;

using Cognite.Simulator.Utils.Automation;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    /// <summary>
    /// Minimal concrete implementation of AutomationClient for testing purposes.
    /// Required because AutomationClient is abstract.
    /// </summary>
    public class TestableAutomationClient : AutomationClient
    {
        public TestableAutomationClient(ILogger<TestableAutomationClient> logger, AutomationConfig config)
            : base(logger, config)
        {
        }

        protected override void PreShutdown()
        {
            // Required implementation for abstract method - no-op for testing
        }

        public void SetServerInitialized()
        {
            var serverProperty = typeof(AutomationClient).GetProperty("Server",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            serverProperty?.SetValue(this, new object());
        }
    }

    [Collection(nameof(SequentialTestCollection))]
    public class AutomationUtilsUnitTest
    {
        private readonly ServiceCollection _services;
        private readonly Mock<ILogger<TestableAutomationClient>> _mockedLogger;

        public AutomationUtilsUnitTest()
        {
            _services = new ServiceCollection();
            _mockedLogger = new Mock<ILogger<TestableAutomationClient>>();
            _services.AddSingleton(_mockedLogger.Object);
        }

        private TestableAutomationClient CreateClient(string programId = "Test.Program")
        {
            _services.AddSingleton(new AutomationConfig { ProgramId = programId });
            _services.AddSingleton<TestableAutomationClient>();
            return _services.BuildServiceProvider().GetRequiredService<TestableAutomationClient>();
        }

        [Fact]
        public void ShouldLogConnectionMessagesWhenInitializing()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "development");

                var client = CreateClient();
                client.Initialize();

                VerifyLog(_mockedLogger, LogLevel.Debug, "Connecting to automation server", Times.Once(), true);
                VerifyLog(_mockedLogger, LogLevel.Debug, "Connected to simulator instance", Times.Once(), true);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnv);
            }
        }

        [Fact]
        public void ShouldLogServerRemovedOnShutdown()
        {
            var client = CreateClient();
            client.Shutdown();

            VerifyLog(_mockedLogger, LogLevel.Debug, "Automation server instance removed", Times.Once(), true);
        }

        [Fact]
        public void ShouldSkipInitializationWhenAlreadyConnected()
        {
            var client = CreateClient();
            client.SetServerInitialized();

            client.Initialize();

            VerifyLog(_mockedLogger, LogLevel.Debug, "Connecting to automation server", Times.Never(), true);
        }

        [Fact]
        public void ShouldThrowExceptionWhenEnvironmentVariableIsMissing()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", null);

                var client = CreateClient();

                Assert.Throws<NullReferenceException>(() => client.Initialize());
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnv);
            }
        }

        [Fact]
        public void ShouldAcceptDevelopmentEnvironmentInAnyCasing()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            var originalEnv = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT");
            try
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "DEVELOPMENT");

                var client = CreateClient();

                var exception = Record.Exception(() => client.Initialize());

                Assert.Null(exception);
            }
            finally
            {
                Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", originalEnv);
            }
        }

        [WindowsOnlyFact]
        public void ShouldWrapComErrorsInSimulatorConnectionException()
        {
            var client = CreateClient("InvalidProgramId.That.Does.Not.Exist");

            var exception = Assert.Throws<SimulatorConnectionException>(() => client.Initialize());
            Assert.Equal("Cannot connect to automation server", exception.Message);
        }

        [WindowsOnlyFact]
        public void ShouldLogErrorWhenServerTypeNotFound()
        {
            var client = CreateClient("NonExistent.Application.12345");

            try
            {
                client.Initialize();
            }
            catch (SimulatorConnectionException)
            {
                // Expected
            }

            VerifyLog(_mockedLogger, LogLevel.Error, "Could not find automation server using the id", Times.Once(), true);
        }

        [Fact]
        public void ConfigShouldStoreProgramId()
        {
            var config = new AutomationConfig { ProgramId = "TestProgram.Application" };

            Assert.Equal("TestProgram.Application", config.ProgramId);
        }

        [Fact]
        public void ConfigShouldStoreProcessId()
        {
            var config = new AutomationConfig { ProcessId = "TestProcess" };

            Assert.Equal("TestProcess", config.ProcessId);
        }

        [Fact]
        public void ConfigShouldDefaultToNullValues()
        {
            var config = new AutomationConfig();

            Assert.Null(config.ProgramId);
            Assert.Null(config.ProcessId);
        }

        [Fact]
        public void ConnectionExceptionShouldBeCreatedWithoutArguments()
        {
            var exception = new SimulatorConnectionException();

            Assert.NotNull(exception);
            Assert.Null(exception.InnerException);
        }

        [Fact]
        public void ConnectionExceptionShouldStoreMessage()
        {
            var exception = new SimulatorConnectionException("Test error message");

            Assert.Equal("Test error message", exception.Message);
            Assert.Null(exception.InnerException);
        }

        [Fact]
        public void ConnectionExceptionShouldBeCaughtAsBaseException()
        {
            var specificException = new SimulatorConnectionException("Test message");

            try
            {
                throw specificException;
            }
            catch (Exception ex)
            {
                Assert.IsType<SimulatorConnectionException>(ex);
                Assert.Equal("Test message", ex.Message);
            }
        }

        [Fact]
        public void ConnectionExceptionShouldPreserveInnerExceptionDetails()
        {
            Exception? caughtException = null;

            try
            {
                try
                {
                    throw new InvalidOperationException("Original error");
                }
                catch (InvalidOperationException ex)
                {
                    throw new SimulatorConnectionException("Wrapper message", ex);
                }
            }
            catch (SimulatorConnectionException ex)
            {
                caughtException = ex;
            }

            Assert.NotNull(caughtException);
            Assert.NotNull(caughtException!.InnerException);
            Assert.Contains("Original error", caughtException.InnerException.Message);
            Assert.NotNull(caughtException.InnerException.StackTrace);
        }
    }
}
