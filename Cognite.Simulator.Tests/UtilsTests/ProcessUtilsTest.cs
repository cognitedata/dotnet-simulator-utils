using System;
using System.Diagnostics;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;

using Cognite.Simulator.Utils;

using Microsoft.Extensions.Logging;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{
    public class WindowsOnlyFactAttribute : FactAttribute
    {
        public WindowsOnlyFactAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Skip = "Test only runs on Windows";
            }
        }
    }
    public class ProcessUtilsTests
    {
        [Fact]
        public void GetProcessOwnerWmi_ThrowsOnNonWindows()
        {
            // We can't easily mock this static method, so we'll only run this test on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Throws<PlatformNotSupportedException>(() => ProcessUtils.GetProcessOwnerWmi(1));
            }
        }

        [Fact]
        public void GetCurrentUsername_ThrowsOnNonWindows()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                Assert.Throws<PlatformNotSupportedException>(() => ProcessUtils.GetCurrentUsername());
            }
        }

        private bool IsProcessRunning(string processName, Process process)
        {
            try
            {
                return Process.GetProcessesByName(processName).Any(p => p.Id == process.Id);
            }
            catch
            {
                // Any other exception indicates the process is likely not running
                return false;
            }
        }

        [WindowsOnlyFact]
        public void KillProcess_KillsOwnedProcessesOnly()
        {

            // Arrange
            var mockLogger = new Mock<ILogger<ProcessUtilsTests>>();
            var processName = "notepad";

            // Create test process to ensure at least one exists
            Process testProcess = null;
            testProcess = Process.Start(processName + ".exe");

            // Act
            ProcessUtils.KillProcess(processName, mockLogger.Object);

            // Verify log messages were called with correct parameters
            VerifyLog(mockLogger, LogLevel.Debug, "Searching for process : " + processName, Times.Once(), true);
            VerifyLog(mockLogger, LogLevel.Information, "Killing process with PID", Times.Once(), true);


            bool processStillRunning = IsProcessRunning(processName, testProcess);
            Assert.False(processStillRunning, "Process should have been terminated");
        }

        [Fact]
        public void KillProcess_DoesThrow_WhenProcessNotFound()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<ProcessUtilsTests>>();

            // Act 
            Assert.Throws<Exception>(() => ProcessUtils.KillProcess("ThisProcessDoesNotExist_12345", mockLogger.Object));

            VerifyLog(mockLogger, LogLevel.Error, "No processes found to kill for the current user", Times.Once(), true);
        }

        [WindowsOnlyFact]
        public void GetProcessOwnerWmi_ReturnsOwnerString()
        {

            // Arrange - get current process ID
            int processId = Process.GetCurrentProcess().Id;

            // Act
            string owner = ProcessUtils.GetProcessOwnerWmi(processId);

            // Assert
            Assert.Contains("\\", owner); // Owner format should be "domain\user"
        }

        [WindowsOnlyFact]
        public void GetProcessOwnerWmi_ReturnsNoOwnerFound_WhenProcessDoesNotExist()
        {

            // Arrange - unlikely that process ID 999999 exists
            int nonExistentProcessId = 999999;

            var exception = Assert.Throws<Exception>(() => ProcessUtils.GetProcessOwnerWmi(nonExistentProcessId));
            Assert.Equal("Process not found", exception.Message);
        }
    }
}
