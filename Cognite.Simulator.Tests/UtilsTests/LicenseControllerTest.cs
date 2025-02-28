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

    public class LicenseControllerTests
    {
        private readonly Mock<ILogger> _loggerMock = new Mock<ILogger>();

        private (LicenseController controller, StateHolder<TestLicenseState> stateHolder) CreateTracker(
            Mock<Func<object>>? releaseMock = null,
            Mock<Func<object>>? acquireMock = null,
            TimeSpan? licenseLockTime = null)
        {
            var stateHolder = new StateHolder<TestLicenseState> { State = TestLicenseState.Released };
            
            releaseMock ??= new Mock<Func<object>>();
            acquireMock ??= new Mock<Func<object>>();
            
            // Set up the mocks to track the state changes
            releaseMock.Setup(m => m()).Callback(() => stateHolder.State = TestLicenseState.Released);
            acquireMock.Setup(m => m()).Callback(() => stateHolder.State = TestLicenseState.Held);
            
            var tracker = new LicenseController(
                licenseLockTime ?? TimeSpan.FromMilliseconds(100),
                () => { releaseMock.Object(); },
                () => { acquireMock.Object(); },
                _loggerMock.Object
            );
            
            return (tracker, stateHolder);
        }

        // Simple class to hold state that can be captured in lambdas
        private class StateHolder<T>
        {
            public T State { get; set; }
        }


        [Fact]
        public void LicenseTracker_ShouldReleaseLicense_WhenNotInUse()
        {

            // Arrange
            Mock<Func<object>> _releaseLicenseFuncMock = new Mock<Func<object>>();
            Mock<Func<object>> _acquireLicenseFuncMock = new Mock<Func<object>>();
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker( _releaseLicenseFuncMock, _acquireLicenseFuncMock, licenseLockTime);

            // Check if the default state of the license is released
            Assert.Equal(TestLicenseState.Released, license.State);

            // Check if acquiring the license calls the acquire license function and changes the state
            tracker.AcquireLicense();
            _acquireLicenseFuncMock.Verify(f => f(), Times.Once);
            Assert.Equal(TestLicenseState.Held, license.State);
            Assert.True(tracker.LicenseHeld);

            using (tracker.BeginUsage()) { /* Use and immediately release */ } 
            
            // Add buffer time to account for timer inconsistencies
            Thread.Sleep(400);
            
            // Check if the release license callback was called
            _releaseLicenseFuncMock.Verify(f => f(), Times.Once);

            // Check if the license is released
            Assert.Equal(TestLicenseState.Released, license.State);
            Assert.False(tracker.LicenseHeld);
            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldNotRelease_WhileInUse()
        {
            // Arrange
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker(licenseLockTime: licenseLockTime);
            
            // Act
            tracker.AcquireLicense();
            Assert.Equal(TestLicenseState.Held, license.State);
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
            var (tracker, license) = CreateTracker(licenseLockTime: licenseLockTime);

            // Act  
            tracker.AcquireLicense();
            Assert.Equal(TestLicenseState.Held, license.State);
            using (tracker.BeginUsage()) { } // First usage
            Thread.Sleep(50);
            
            using (tracker.BeginUsage()) { } // Second usage should reset timer
            Thread.Sleep(50);
            
            // Assert
            Assert.True(tracker.LicenseHeld); // Should not be released yet due to timer reset

            Thread.Sleep(400); // Wait for full lock time + buffer
            Assert.True(license.State == TestLicenseState.Released);
            Assert.False(tracker.LicenseHeld);

            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldPreventRelease_DuringContinuousUsage()
        {
            // Arrange
            var (tracker, license) = CreateTracker();

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

            Thread.Sleep(400); // Wait longer than lock time after all usage ends
            
            // Assert
            Assert.True(license.State == TestLicenseState.Released);
            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldUpdateState_WhenLicenseReleasedManually()
        {
            // Arrange
            var (tracker, license) = CreateTracker();
            tracker.AcquireLicense();
            using (tracker.BeginUsage()) { }
            Assert.True(tracker.LicenseHeld);

            // Act
            tracker.ClearLicenseState();
            
            // Assert
            Assert.False(tracker.LicenseHeld);
            // Should not change external license state (since this is a manual release)
            Assert.True(license.State == TestLicenseState.Held);
            // check timer is stopped
            tracker.Dispose();
        }
    }
}