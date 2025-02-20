using Xunit;
using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using Cognite.Simulator.Utils;

namespace Cognite.Simulator.Tests.UtilsTests{

    public enum TestLicenseState
    {
        Released,
        Held
    }

    public class LicenseTrackerTests
    {
        private readonly Mock<ILogger> _loggerMock = new Mock<ILogger>();

        

        [Fact]
        public void LicenseTracker_ShouldReleaseLicense_WhenNotInUse()
        {
            // Arrange
            Mock<Func<object>> _releaseLicenseFuncMock = new Mock<Func<object>>();
            Mock<Func<object>> _acquireLicenseFuncMock = new Mock<Func<object>>();
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var licenseState = TestLicenseState.Released;
            var tracker = new LicenseController(
                licenseLockTime: licenseLockTime,
                releaseLicenseFunc: () => 
                { 
                    _releaseLicenseFuncMock.Object();
                    licenseState = TestLicenseState.Released;
                    return null;
                },
                acquireLicenseFunc: () => {
                    _acquireLicenseFuncMock.Object();
                    licenseState = TestLicenseState.Held;
                    return null;
                },
                _loggerMock.Object
            );

            // default state of the license should be released
            Assert.Equal(TestLicenseState.Released, licenseState);
            tracker.AcquireLicense();
            _acquireLicenseFuncMock.Verify(f => f(), Times.Once);
            Assert.Equal(TestLicenseState.Held, licenseState);
            Assert.True(tracker.LicenseHeld);
            using (tracker.BeginUsage()) { 
                // Use and immediately release
            } 
            
            // Add buffer time to account for timer inconsistencies
            Thread.Sleep(250);
            
            _releaseLicenseFuncMock.Verify(f => f(), Times.Once);
            Assert.Equal(TestLicenseState.Released, licenseState);
            Assert.False(tracker.LicenseHeld);
            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldNotRelease_WhileInUse()
        {
            // Arrange
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var licenseState = TestLicenseState.Released;
            var tracker = new LicenseController(
                licenseLockTime: licenseLockTime,
                releaseLicenseFunc: () => 
                { 
                    licenseState = TestLicenseState.Released;
                    return null;
                },
                acquireLicenseFunc: () => {
                    licenseState = TestLicenseState.Held;
                    return null;
                },
                _loggerMock.Object
            );
            
            // Act
            tracker.AcquireLicense();
            Assert.Equal(TestLicenseState.Held, licenseState);
            Assert.True(tracker.LicenseHeld);
            using (var usage = tracker.BeginUsage())
            {
                Thread.Sleep(200); // Wait longer than lock time
                Assert.True(tracker.LicenseHeld); // Should not release while in use
            }

            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldResetTimer_OnNewUsage()
        {
            // Arrange
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var licenseState = TestLicenseState.Released;
            var tracker = new LicenseController(
                licenseLockTime: licenseLockTime,
                releaseLicenseFunc: () => 
                { 
                    licenseState = TestLicenseState.Released;
                    return null;
                },
                acquireLicenseFunc: () => {
                    licenseState = TestLicenseState.Held;
                    return null;
                },
                _loggerMock.Object
            );

            // Act  
            tracker.AcquireLicense();
            Assert.Equal(TestLicenseState.Held, licenseState);
            using (tracker.BeginUsage()) { } // First usage
            Thread.Sleep(50);
            
            using (tracker.BeginUsage()) { } // Second usage should reset timer
            Thread.Sleep(50);
            
            // Assert
            Assert.True(tracker.LicenseHeld); // Should not be released yet due to timer reset

            Thread.Sleep(100); // Wait for full lock time
            Assert.True(licenseState == TestLicenseState.Released);
            Assert.False(tracker.LicenseHeld);

            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldPreventRelease_DuringContinuousUsage()
        {
            // Arrange
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var licenseState = TestLicenseState.Released;
            var tracker = new LicenseController(
                licenseLockTime: licenseLockTime,
                releaseLicenseFunc: () => 
                { 
                    licenseState = TestLicenseState.Released;
                    return null;
                },
                acquireLicenseFunc: () => {
                    licenseState = TestLicenseState.Held;
                    return null;
                },
                _loggerMock.Object
            );

            // Act
            tracker.AcquireLicense();
            using (var usage1 = tracker.BeginUsage())
            {
                Thread.Sleep(50);
                using (var usage2 = tracker.BeginUsage())
                {
                    Thread.Sleep(100);
                    // Should not release during overlapping usage
                    Assert.True(tracker.LicenseHeld);
                }
            }

            Thread.Sleep(200); // Wait longer than lock time after all usage ends
            
            // Assert
            Assert.True(licenseState == TestLicenseState.Released);
            tracker.Dispose();
        }
    }
}