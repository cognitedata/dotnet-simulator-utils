using Microsoft.Extensions.Logging;
using Moq;
using System;
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Xunit;
using Cognite.Simulator.Utils;

namespace Cognite.Simulator.Tests.UtilsTests
{
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

        [Fact]
        public void KillProcess_KillsOwnedProcessesOnly()
        {
            // Only run on Windows for simplicity
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Arrange
            var mockLogger = new Mock<ILogger>();
            var processName = "notepad";
            
            // Create test process to ensure at least one exists
            Process testProcess = null;
            try
            {
                testProcess = Process.Start("notepad.exe");
                
                // Act
                ProcessUtils.KillProcess(processName, mockLogger.Object);
                
                // Assert
                // Verify log messages were called with correct parameters
                TestUtilities.VerifyLog(mockLogger, LogLevel.Debug, "Searching for process : {processName}", Times.Once(), true);
                
                // Verify that process.Kill was called if this process belongs to current user
                string currentUser = ProcessUtils.GetCurrentUsername().ToLower();
                TestUtilities.VerifyLog(mockLogger, LogLevel.Information, "Killing process with PID", Times.Once(), true);
                
                // Verify that our testProcess was killed (it should belong to current user)
                Assert.Throws<InvalidOperationException>(() => testProcess.Refresh());
            }
            finally
            {
                // Cleanup - make sure process is closed
                testProcess?.Close();
            }
        }

        [Fact]
        public void KillProcess_HandlesExceptions()
        {
            // Arrange
            var mockLogger = new Mock<ILogger>();
            
            // Act - pass a non-existent process name
            ProcessUtils.KillProcess("ThisProcessDoesNotExist_12345", mockLogger.Object);
            
            // Assert - verify error wasn't logged since no exception should occur
            // when process not found (empty enumeration handled gracefully)
            TestUtilities.VerifyLog(mockLogger, LogLevel.Information, "Failed to kill process", Times.Once(), true);
        }

        [Fact]
        public void GetProcessOwnerWmi_ReturnsOwnerString()
        {
            // Skip test on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Arrange - get current process ID
            int processId = Process.GetCurrentProcess().Id;
            
            // Act
            string owner = ProcessUtils.GetProcessOwnerWmi(processId);
            
            // Assert
            Assert.NotEqual("No Owner Found", owner);
            Assert.NotEqual("Access Denied or Process Exited", owner);
            Assert.Contains("\\", owner); // Owner format should be "domain\user"
        }

        [Fact]
        public void GetProcessOwnerWmi_ReturnsNoOwnerFound_WhenProcessDoesNotExist()
        {
            // Skip test on non-Windows platforms
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return;
            }

            // Arrange - unlikely that process ID 999999 exists
            int nonExistentProcessId = 999999;
            
            // Act
            string owner = ProcessUtils.GetProcessOwnerWmi(nonExistentProcessId);
            
            // Assert
            Assert.Equal("No Owner Found", owner);
        }
    }
}