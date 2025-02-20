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
        private Timer _releaseTimer;
        private readonly Func<object> _releaseLicenseFunc;
        private readonly Func<object> _acquireLicenseFunc;
        private bool _licenseHeld;
        private DateTime _lastUsageTime;
        private DateTime _scheduledReleaseTime;
        private readonly ILogger _logger;
        private const int LoggingIntervalMs = 60000; // 1 minute
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();

        /// <summary>
        /// Initializes a new instance of the <see cref="LicenseController"/> class.
        /// </summary>
        /// <param name="licenseLockTime">The time span for which the license is locked.</param>
        /// <param name="releaseLicenseFunc">The function to release the license.</param>
        /// <param name="acquireLicenseFunc">The function to acquire the license.</param>
        /// <param name="logger">The logger instance for logging information.</param>
        /// <param name="cancellationToken">The cancellation token to observe while waiting for the task to complete. Optional.</param>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="releaseLicenseFunc"/> is null.</exception>
        public LicenseController(
            TimeSpan licenseLockTime,
            Func<object> releaseLicenseFunc,
            Func<object> acquireLicenseFunc,
            ILogger logger,
            CancellationToken cancellationToken = new CancellationToken()
        )
        {
            _licenseLockTime = licenseLockTime;
            _licenseHeld = false;
            _releaseLicenseFunc = releaseLicenseFunc ?? throw new ArgumentNullException(nameof(releaseLicenseFunc));
            _acquireLicenseFunc = acquireLicenseFunc ?? throw new ArgumentNullException(nameof(acquireLicenseFunc));
            _releaseTimer = new Timer(ReleaseLicense, null, Timeout.Infinite, Timeout.Infinite);
            _logger = logger;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
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
                    await Task.Delay(LoggingIntervalMs, cancellationToken).ConfigureAwait(false);
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

        private string FormatDuration(TimeSpan duration)
        {
            if (duration.TotalDays >= 1)
                return $"{duration.TotalDays:N1} days";
            if (duration.TotalHours >= 1)
                return $"{duration.TotalHours:N1} hours";
            if (duration.TotalMinutes >= 1)
                return $"{duration.TotalMinutes:N1} minutes";
            return $"{duration.TotalSeconds:N1} seconds";
        }

        private void LogLicenseStatus()
        {
            _logger.LogInformation("License is currently {Status}", _licenseHeld ? "held" : "not held");
            if (_licenseHeld)
            {
                var duration = DateTime.Now - _lastUsageTime;
                _logger.LogInformation("License has been held for {Duration}", FormatDuration(duration));
                var timeUntilRelease = _scheduledReleaseTime - DateTime.Now;
                _logger.LogInformation("License will be released in {TimeUntilRelease} (at {ReleaseTime})", 
                    FormatDuration(timeUntilRelease),
                    _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }
        
        /// <summary>
        /// Acquires a license by invoking the license acquisition function and sets the license status to held.
        /// Also updates the release timer to ensure the license is released after a certain period.
        /// </summary>
        /// <exception cref="Exception">Thrown when an error occurs while acquiring the license.</exception>
        public void AcquireLicense() {
            lock (_lock)
            {
                try
                {
                    _logger.LogInformation("Attempting to acquire license");
                    _acquireLicenseFunc();
                    _licenseHeld = true;
                    UpdateReleaseTimer();
                    _logger.LogInformation("License acquired successfully");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error acquiring license");
                    throw;
                }
            }
        }

        private void ReleaseLicense(object state)
        {
            lock (_lock)
            {
                _logger.LogInformation("Attempting to release license at scheduled time {ScheduledTime}", _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
                if (DateTime.Now >= _scheduledReleaseTime)
                {
                    _releaseLicenseFunc();
                    _licenseHeld = false;  
                    _logger.LogInformation("License released successfully");
                }
                else
                {
                    _logger.LogInformation("License not released - current time {CurrentTime} is before scheduled release time", 
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                }
            }
        }

        private void UpdateReleaseTimer() {
            _lastUsageTime = DateTime.Now;
            // Extend release time
            _scheduledReleaseTime = DateTime.Now.Add(_licenseLockTime);
            _logger.LogInformation("License release scheduled for {ReleaseTime}", 
            _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
            _releaseTimer.Change(Timeout.Infinite, Timeout.Infinite);
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
                UpdateReleaseTimer();
                return new LicenseUsageScope(this);
            }
        }

        private void EndUsage()
        {
            lock (_lock)
            {
                _logger.LogInformation("Ending license usage, scheduling release");
                _lastUsageTime = DateTime.Now;
                _scheduledReleaseTime = _lastUsageTime.Add(_licenseLockTime);
                _logger.LogInformation("License release scheduled for {ReleaseTime}", 
                    _scheduledReleaseTime.ToString("yyyy-MM-dd HH:mm:ss"));
                _releaseTimer.Change(_licenseLockTime, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Releases the license immediately.
        /// </summary>
        public void Dispose()
        {
            _releaseTimer?.Dispose();
            _releaseTimer = null;
            GC.SuppressFinalize(this);
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