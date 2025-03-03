using Xunit;
using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Moq;
using Cognite.Simulator.Utils;
using Cognite.Extractor.Common;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;

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
            TimeSpan? licenseLockTime = null,
            TimeProvider timeProvider = null)
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
                (CancellationToken _token) => { acquireMock.Object(); },
                _loggerMock.Object,
                timeProvider
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
            var fakeTimeProvider = new FakeTimeProvider();
            Mock<Func<object>> _releaseLicenseFuncMock = new Mock<Func<object>>();
            Mock<Func<object>> _acquireLicenseFuncMock = new Mock<Func<object>>();
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker( _releaseLicenseFuncMock, _acquireLicenseFuncMock, licenseLockTime, timeProvider: fakeTimeProvider);

            Assert.Equal(TestLicenseState.Released, license.State);

            // Check if acquiring the license calls the acquire license function and changes the state
            tracker.AcquireLicense(CancellationToken.None);
            _acquireLicenseFuncMock.Verify(f => f(), Times.Once);
            Assert.Equal(TestLicenseState.Held, license.State);
            Assert.True(tracker.LicenseHeld);

            using (tracker.BeginUsage()) { /* Use and immediately release */ } 
            
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
            
            _releaseLicenseFuncMock.Verify(f => f(), Times.Once);

            Assert.Equal(TestLicenseState.Released, license.State);
            Assert.False(tracker.LicenseHeld);
            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldNotRelease_WhileInUse()
        {
            // Arrange
            var fakeTimeProvider = new FakeTimeProvider();
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker(licenseLockTime: licenseLockTime, timeProvider: fakeTimeProvider);
            
            // Act
            tracker.AcquireLicense(CancellationToken.None);
            Assert.Equal(TestLicenseState.Held, license.State);
            Assert.True(tracker.LicenseHeld);
            using (var usage = tracker.BeginUsage())
            {
                fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(200));
                Assert.True(tracker.LicenseHeld); // Should not release while in use
            }

            tracker.Dispose();
        }

        [Fact]
        public async Task LicenseTracker_ShouldResetTimer_OnNewUsage()
        {
            // Arrange
            var fakeTimeProvider = new FakeTimeProvider();
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker(licenseLockTime: licenseLockTime, timeProvider: fakeTimeProvider);

            // Act  
            tracker.AcquireLicense(CancellationToken.None);
            Assert.Equal(TestLicenseState.Held, license.State);
            
            using (tracker.BeginUsage()) { } 
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(50));
            
            using (tracker.BeginUsage()) { } // This usage should reset the timeout timer
            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(50));
            
            // Assert
            Assert.True(tracker.LicenseHeld); // Should not be released yet due to timer reset

            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(100));

            Assert.False(tracker.LicenseHeld);
            Assert.Equal(TestLicenseState.Released, license.State);

            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldPreventRelease_DuringContinuousUsage()
        {
            // Arrange
            var fakeTimeProvider = new FakeTimeProvider();
            var (tracker, license) = CreateTracker(timeProvider: fakeTimeProvider);

            // Act
            tracker.AcquireLicense(CancellationToken.None);
            using (var usage1 = tracker.BeginUsage())
            {
                fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(50));
                using (var usage2 = tracker.BeginUsage())
                {
                    fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(100));
                    Assert.True(tracker.LicenseHeld);
                }
            }

            fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(100)); // Wait for the lock time to pass
            
            // Assert
            Assert.True(license.State == TestLicenseState.Released);
            tracker.Dispose();
        }

        [Fact]
        public void LicenseTracker_ShouldUpdateState_WhenLicenseReleasedManually()
        {
            // Arrange
            var (tracker, license) = CreateTracker();
            tracker.AcquireLicense(CancellationToken.None);
            using (tracker.BeginUsage()) { }
            Assert.True(tracker.LicenseHeld);

            // Act
            tracker.ClearLicenseState();
            
            // Assert
            Assert.False(tracker.LicenseHeld);
            // Should not change external license state (since this is a manual release)
            Assert.True(license.State == TestLicenseState.Held);
            tracker.Dispose();
        }
    }
}