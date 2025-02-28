using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Xunit;
using Cognite.Simulator.Utils;
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

        [WindowsOnlyFact]
        public void KillProcess_KillsOwnedProcessesOnly()
        {

            // Arrange
            var mockLogger = new Mock<ILogger<ProcessUtilsTests>>();
            var processName = "notepad";
            
            // Create test process to ensure at least one exists
            Process testProcess = null;
            try
            {
                testProcess = Process.Start(processName + ".exe");
                
                // Act
                ProcessUtils.KillProcess(processName, mockLogger.Object);
                
                // Assert
                // Verify log messages were called with correct parameters
                VerifyLog(mockLogger, LogLevel.Debug, "Searching for process : " + processName, Times.Once(), true);
                
                // Verify that process.Kill was called if this process belongs to current user
                string currentUser = ProcessUtils.GetCurrentUsername().ToLower();
                VerifyLog(mockLogger, LogLevel.Information, "Killing process with PID", Times.Once(), true);
                
                // Verify that our testProcess was killed (it should belong to current user)
                bool processStillRunning = Process.GetProcessesByName(processName).Any(p => p.Id == testProcess.Id);
                Assert.False(processStillRunning, "Process should have been terminated");
                // Assert.Throws<InvalidOperationException>(() => testProcess.Refresh());
            }
            finally
            {
                // Cleanup - make sure process is closed
                testProcess?.Close();
            }
        }

        [Fact]
        public void KillProcess_DoesNotThrow_WhenProcessNotFound()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<ProcessUtilsTests>>();
            
            // Act - pass a non-existent process name
            ProcessUtils.KillProcess("ThisProcessDoesNotExist_12345", mockLogger.Object);
            
            // Assert - verify error wasn't logged since no exception should occur
            VerifyLog(mockLogger, LogLevel.Error, "Failed to kill process", Times.Never(), true);
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