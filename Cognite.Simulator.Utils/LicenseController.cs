using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Cognite.Simulator.Utils
{

    /// <summary>
    /// Class to handle license acquisition and release.
    /// With this a license can be acquired and released after a certain period of time.
    /// </summary>
    public class LicenseController : IDisposable
    {
        private readonly TimeSpan _licenseLockTime;
        private readonly object _lock = new object();
        private ITimer _releaseTimer;
        private readonly Action _releaseLicenseFunc;
        private readonly Action<CancellationToken> _acquireLicenseFunc;
        private bool _licenseHeld;
        private DateTimeOffset _lastUsageTime;
        private DateTimeOffset _scheduledReleaseTime;
        private readonly ILogger _logger;
        private readonly TimeProvider _timeProvider;
        private const int LoggingIntervalMs = 60000; // 1 minute
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Class to handle license acquisition and release.
        /// When this is used in a using block, the license is released based on the time span provided after the block ends.
        /// When the block ends a timer is started to release the license after the time span has elapsed.
        /// </summary>
        /// <param name="licenseLockTime">The time span for which the license is locked.</param>
        /// <param name="releaseLicenseFunc">The function to release the license.</param>
        /// <param name="acquireLicenseFunc">The function to acquire the license.</param>
        /// <param name="logger">The logger instance for logging information.</param>
        /// <param name="timeProvider">The time provider to use for all time-related operations.</param>
        /// <param name="cancellationToken">The cancellation token to observe while waiting for the task to complete. Optional.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="releaseLicenseFunc"/> is null.</exception>
        public LicenseController(
            TimeSpan licenseLockTime,
            Action releaseLicenseFunc,
            Action<CancellationToken> acquireLicenseFunc,
            ILogger logger,
            TimeProvider timeProvider = null,
            CancellationToken cancellationToken = new CancellationToken()
        )
        {
            _licenseLockTime = licenseLockTime;
            _licenseHeld = false;
            _releaseLicenseFunc = releaseLicenseFunc ?? throw new ArgumentNullException(nameof(releaseLicenseFunc));
            _acquireLicenseFunc = acquireLicenseFunc ?? throw new ArgumentNullException(nameof(acquireLicenseFunc));
            _logger = logger;
            _timeProvider = timeProvider == null? TimeProvider.System : timeProvider;
            _releaseTimer = _timeProvider.CreateTimer(ReleaseLicenseAfterTimeout, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            
            // Dispose the timer on cancellation
            _cts.Token.Register(() => {
                _releaseTimer?.Dispose();
                _releaseTimer = null;
            }); 
            _ = StartLoggingTask(_cts.Token);
        }

        /// <summary>
        /// Gets a boolean indicating whether the license is currently held or not.
        /// </summary>
        public bool LicenseHeld => _licenseHeld;

        private async Task StartLoggingTask(CancellationToken cancellationToken)
        {
            try
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    LogLicenseStatus();
                    
                    var tcs = new TaskCompletionSource<bool>();
                    
                    using var delayTimer = _timeProvider.CreateTimer(_ => tcs.TrySetResult(true), null,
                        TimeSpan.FromMilliseconds(LoggingIntervalMs), Timeout.InfiniteTimeSpan);
                    
                    using var registration = cancellationToken.Register(() => tcs.TrySetCanceled());
                    
                    await tcs.Task.ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled, log final status
                LogLicenseStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in license status logging task");
            }
        }

        private void LogLicenseStatus() {
          _logger.LogInformation("License is currently {Status}", _licenseHeld ? "held" : "not held");
          if (_licenseHeld) {
            var duration = _timeProvider.GetUtcNow() - _lastUsageTime;
            _logger.LogInformation("License has been held for {Duration}", CommonUtils.FormatDuration(duration));
            var timeUntilRelease = _scheduledReleaseTime - _timeProvider.GetUtcNow();
            var timeUntilReleaseFormatted = CommonUtils.FormatDuration(timeUntilRelease);
            var scheduledReleaseTimeFormatted = _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss");
            _logger.LogInformation("License will be released in {TimeUntilRelease} (at {ReleaseTime})",
              timeUntilReleaseFormatted,
              scheduledReleaseTimeFormatted
              );
          }
        }
        
        /// <summary>
        /// Acquires a license by invoking the license acquisition function and sets the license status to held.
        /// Also updates the release timer to ensure the license is released after a certain period.
        /// </summary>
        /// <exception cref="Exception">Thrown when an error occurs while acquiring the license.</exception>
        public void AcquireLicense(CancellationToken cancellationToken) {
            lock (_lock)
            {
                try
                {
                    if (_licenseHeld)
                    {
                        _logger.LogInformation("License already held, skipping acquisition");
                        return;
                    }
                    _logger.LogInformation("Attempting to acquire license");
                    _acquireLicenseFunc(cancellationToken);
                    _licenseHeld = true;
                    PauseTimer();
                    _logger.LogInformation("License acquired successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error acquiring license");
                    throw;
                }
            }
        }

        /// <summary>
        /// Releases the license after the timeout has elapsed. This is called by the timer.
        /// </summary>
        private void ReleaseLicenseAfterTimeout(object state)
        {
            lock (_lock)
            {
                _logger.LogInformation("Attempting to release license at scheduled time {ScheduledTime}", _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
                if (_timeProvider.GetUtcNow() >= _scheduledReleaseTime)
                {
                    _releaseLicenseFunc();
                    _licenseHeld = false;  
                    _logger.LogInformation("License released successfully");
                }
                else
                {
                    _logger.LogInformation("License not released - current time {CurrentTime} is before scheduled release time", 
                        _timeProvider.GetUtcNow().ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
        }

        /// <summary>
        /// Releases the license immediately. This is used externally to reset the internal state when an external process finds out
        /// that the license is no longer held.
        /// </summary>
        public void ClearLicenseState() {
            lock (_lock)
            {
                _licenseHeld = false;
                _lastUsageTime = _timeProvider.GetUtcNow();
                _scheduledReleaseTime = DateTimeOffset.MaxValue;
                _releaseTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _logger.LogInformation("License released forcefully");
            }
        }

        private void PauseTimer() {
            _logger.LogInformation("License usage started: pausing release timer");
            _releaseTimer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        }

        /// <summary>
        /// Begins usage of the license.
        /// </summary>
        /// <returns>An <see cref="IDisposable"/> object that can be used to end the license usage.</returns>
        public IDisposable BeginUsage()
        {
            lock (_lock)
            {
                _logger.LogInformation("Starting license usage");
                PauseTimer();
                return new LicenseUsageScope(this);
            }
        }

        private void EndUsage()
        {
            lock (_lock)
            {
                _logger.LogInformation("Ending license usage, scheduling release");
                _lastUsageTime = _timeProvider.GetUtcNow();
                _scheduledReleaseTime = _lastUsageTime.Add(_licenseLockTime);
                _logger.LogInformation("License release scheduled for {ReleaseTime}", 
                _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _releaseTimer.Change(_licenseLockTime, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Disposes the license controller and releases the timer.
        /// </summary>
        public void Dispose()
        {
            _releaseTimer?.Dispose();
            _releaseTimer = null;
        }

        private class LicenseUsageScope : IDisposable
        {
            private readonly LicenseController _tracker;
            private bool _disposed;

            public LicenseUsageScope(LicenseController tracker)
            {
                _tracker = tracker;
            }

            public void Dispose()
            {
                if (!_disposed)
                {
                    _tracker.EndUsage();
                    _disposed = true;
                }
            }
        }
    }
}