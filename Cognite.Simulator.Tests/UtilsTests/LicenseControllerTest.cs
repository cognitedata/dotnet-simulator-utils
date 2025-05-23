using System;
using System.Threading;
using System.Threading.Tasks;

using Cognite.Extractor.Common;
using Cognite.Simulator.Utils;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

using Moq;

using Xunit;

using static Cognite.Simulator.Tests.UtilsTests.TestUtilities;

namespace Cognite.Simulator.Tests.UtilsTests
{

    public enum TestLicenseState
    {
        Released,
        Held
    }

    public class LicenseControllerTests
    {
        private readonly Mock<ILogger<LicenseControllerTests>> _loggerMock = new Mock<ILogger<LicenseControllerTests>>();

        private FakeTimeProvider fakeTimeProvider = new FakeTimeProvider();

        private (LicenseController controller, StateHolder<TestLicenseState> stateHolder) CreateTracker(
            Mock<Func<object>>? releaseMock = null,
            Mock<Func<object>>? acquireMock = null,
            TimeSpan? licenseLockTime = null,
            TimeProvider? timeProvider = null)
        {
            var stateHolder = new StateHolder<TestLicenseState> { State = TestLicenseState.Released };

            releaseMock ??= new Mock<Func<object>>();
            acquireMock ??= new Mock<Func<object>>();

            // Set up the mocks to track the state changes
            releaseMock.Setup(m => m()).Callback(() => stateHolder.State = TestLicenseState.Released);
            acquireMock.Setup(m => m()).Callback(() => stateHolder.State = TestLicenseState.Held);

            var tracker = new LicenseController(
                licenseLockTime ?? TimeSpan.FromMilliseconds(100),
                (CancellationToken _token) => { releaseMock.Object(); },
                (CancellationToken _token) => { acquireMock.Object(); },
                _loggerMock.Object,
                fakeTimeProvider
            );

            return (tracker, stateHolder);
        }

        // Simple class to hold state that can be captured in lambdas
        private class StateHolder<T>
        {
            public required T State { get; set; }
        }


        [Fact]
        public void LicenseTracker_ShouldReleaseLicense_WhenNotInUse()
        {
            // Arrange
            Mock<Func<object>> _releaseLicenseFuncMock = new Mock<Func<object>>();
            Mock<Func<object>> _acquireLicenseFuncMock = new Mock<Func<object>>();
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker(_releaseLicenseFuncMock, _acquireLicenseFuncMock, licenseLockTime);

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
        }

        [Fact]
        public void LicenseTracker_ShouldNotRelease_WhileInUse()
        {
            // Arrange
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker(licenseLockTime: licenseLockTime);

            // Act
            tracker.AcquireLicense(CancellationToken.None);
            Assert.Equal(TestLicenseState.Held, license.State);
            Assert.True(tracker.LicenseHeld);
            using (var usage = tracker.BeginUsage())
            {
                fakeTimeProvider.Advance(TimeSpan.FromMilliseconds(200));
                Assert.True(tracker.LicenseHeld); // Should not release while in use
            }

        }

        [Fact]
        public void LicenseTracker_ShouldResetTimer_OnNewUsage()
        {
            // Arrange
            var licenseLockTime = TimeSpan.FromMilliseconds(100);
            var (tracker, license) = CreateTracker(licenseLockTime: licenseLockTime);

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

        }

        [Fact]
        public void LicenseTracker_ShouldPreventRelease_DuringContinuousUsage()
        {
            // Arrange
            var (tracker, license) = CreateTracker();

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
        }

        [Fact]
        public void ClearLicenseState_ShouldLogProperlyAfterReset()
        {
            // Arrange
            Mock<Func<object>> _releaseLicenseFuncMock = new Mock<Func<object>>();
            Mock<Func<object>> _acquireLicenseFuncMock = new Mock<Func<object>>();
            var licenseLockTime = TimeSpan.FromMinutes(5);
            var (tracker, license) = CreateTracker(_releaseLicenseFuncMock, _acquireLicenseFuncMock, licenseLockTime);

            fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
            VerifyLog(_loggerMock, LogLevel.Information, "License is currently not held", Times.AtLeastOnce(), true);

            // Current time at this point is 2000-01-01 00:00:01
            tracker.AcquireLicense(CancellationToken.None);
            using (tracker.BeginUsage()) { }
            fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));
            // forcing logging for the Github action
            tracker.LogLicenseStatus();
            VerifyLog(_loggerMock, LogLevel.Information, "License is currently held", Times.AtLeastOnce(), true);
            VerifyLog(_loggerMock, LogLevel.Information, "License release scheduled for ", Times.AtLeastOnce(), true);
            VerifyLog(_loggerMock, LogLevel.Information, "License will be released in 3.0 minutes (at 2000-01-01 00:06:00)", Times.AtLeastOnce(), true);

            _loggerMock.Invocations.Clear();

            // Act
            tracker.ClearLicenseState();

            fakeTimeProvider.Advance(TimeSpan.FromMinutes(2));
            tracker.LogLicenseStatus();
            VerifyLog(_loggerMock, LogLevel.Information, "License released forcefully", Times.AtLeastOnce(), true);
            VerifyLog(_loggerMock, LogLevel.Information, "License is currently not held", Times.AtLeastOnce(), true);

            tracker.AcquireLicense(CancellationToken.None);
            using (tracker.BeginUsage()) { }
            fakeTimeProvider.Advance(TimeSpan.FromMinutes(1));
            tracker.LogLicenseStatus();
            VerifyLog(_loggerMock, LogLevel.Information, "License is currently held", Times.AtLeastOnce(), true);
            VerifyLog(_loggerMock, LogLevel.Information, "License has been held for 1.0 minutes", Times.AtLeastOnce(), true);
            VerifyLog(_loggerMock, LogLevel.Information, "License will be released in 4.0 minutes (at 2000-01-01 00:10:00)", Times.AtLeastOnce(), true);
        }
    }
}