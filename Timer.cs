using System;
using System.Threading;

namespace FFMEGE {
    public class Timer : IDisposable {
        private System.Threading.Timer timer;
        private DateTime _lastElapsedTime;
        private DateTime lastNextDelayTime;
        private long _interval;
        private long _nextDelay;
        private bool _enabled;
        private bool _autoReset;

        /// <summary>
        /// Event that gets raised whenever an unhandled exception occurs within the Elapsed event.
        /// </summary>
        public static event UnhandledExceptionEventHandler UnhandledException;

        /// <summary>
        /// Creates a new Timer.
        /// </summary>
        public Timer() {
            _autoReset = true;
            timer = new System.Threading.Timer(new TimerCallback(timerCallback));
        }

        /// <summary>
        /// Creates a new Timer.
        /// </summary>
        /// <param name="interval">Values for Interval and NextElapsedDelay, in Milliseconds.</param>
        public Timer(long interval) : this() {
            if (interval < 0L)
                throw new ArgumentException("Argument 'interval' must be greater than 0", "interval");
            _nextDelay = interval;
            _interval = interval;
        }

        /// <summary>
        /// Releases all resouces used by this instance of CreativeSpace.Util.Timer.
        /// </summary>
        public void Dispose() {
            Dispose(true);
        }

        /// <summary>
        /// Releases all resouces used by this instance of CreativeSpace.Util.Timer. For internal / CLR use only.
        /// </summary>
        protected void Dispose(bool disposing) {
            if (disposing) {
                timer.Dispose();
            }
        }

        /// <summary>
        /// If true, the elaspsed event will be raised each interval. If false the event will be raised only once. Defaults to true.
        /// </summary>
        public bool AutoReset {
            get {
                return _autoReset;
            }
            set {
                if (_autoReset != value) {
                    _autoReset = value;
                    if (_enabled)
                        ApplyChange(true);
                }
            }
        }

        /// <summary>
        /// Setting to true starts the timer. Setting to false stops it.
        /// </summary>
        public bool Enabled {
            get {
                return _enabled;
            }
            set {
                if (_enabled != value) {
                    _enabled = value;
                    ApplyChange(false);
                    if (_enabled) {
                        _lastElapsedTime = DateTime.UtcNow;
                    }
                }
            }
        }

        /// <summary>
        /// The time until the timer fires next, in Milliseconds, if Enabled is true.
        /// </summary>
        public long NextElapsedDelay {
            get {
                if (_enabled) {
                    return _nextDelay - (long)DateTime.UtcNow.Subtract(lastNextDelayTime).TotalMilliseconds;
                }
                return _nextDelay;
            }
            set {
                if (_nextDelay != value) {
                    if (value < 0)
                        throw new ArgumentOutOfRangeException("NextElapsedDelay", "NextElapsedDelay must not be less than 0.");
                    _nextDelay = value;
                    if (_enabled) {
                        ApplyChange(false);
                    }
                }
            }
        }

        /// <summary>
        /// The time between timer fires, in Milliseconds.
        /// </summary>
        public long Interval {
            get {
                return _interval;
            }
            set {
                if (_interval != value) {
                    if (value <= 0)
                        throw new ArgumentOutOfRangeException("Interval", "Interval must be greater than 0.");
                    _interval = value;
                    if (_enabled)
                        ApplyChange(true);
                }
            }
        }

        /// <summary>
        /// The last time the timer elapsed, in UTC time.
        /// </summary>
        public DateTime LastElapsedTimeUtc {
            get {
                return _lastElapsedTime;
            }
        }

        private void ApplyChange(bool recalcNextDelay) {
            long tempNextDelay = _nextDelay;
            if (recalcNextDelay) {
                tempNextDelay = tempNextDelay - (long)DateTime.UtcNow.Subtract(_lastElapsedTime).TotalMilliseconds;
            }
            else {
                lastNextDelayTime = DateTime.UtcNow;
            }

            if (tempNextDelay < 0)
                tempNextDelay = 0;

            if (_enabled) {
                if (_autoReset)
                    timer.Change(tempNextDelay, _interval);
                else
                    timer.Change(tempNextDelay, Timeout.Infinite);
            }
            else {
                timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <summary>
        /// Event that gets raised every time the timer elapses.
        /// </summary>
        public event ElapsedEventHandler Elapsed;

        private void timerCallback(object obj) {
            _nextDelay = _interval;

            if (!_autoReset)
                _enabled = false;

            _lastElapsedTime = DateTime.UtcNow;
            lastNextDelayTime = _lastElapsedTime;
            ElapsedEventHandler temp = Elapsed;
            if (temp != null) {
                if (UnhandledException == null) {
                    temp(this, new ElapsedEventArgs(_lastElapsedTime));
                }
                else {
                    try {
                        temp(this, new ElapsedEventArgs(_lastElapsedTime));
                    }
                    catch (Exception ex) {
                        UnhandledException(this, new UnhandledExceptionEventArgs(ex, false));
                    }
                }
            }
        }
    }

    /// <summary>
    /// Delegate for the Timer.Elapsed event.
    /// </summary>
    /// <param name="sender">The timer that raises the event.</param>
    /// <param name="e">The data for the event.</param>
    public delegate void ElapsedEventHandler(object sender, ElapsedEventArgs e);

    /// <summary>
    /// Represents data for the Timer.Elapsed event.
    /// </summary>
    public class ElapsedEventArgs : EventArgs {
        /// <summary>
        /// Create a new ElapsedEventArgs
        /// </summary>
        /// <param name="elapsedTime">A DateTime representing the time the Timer elapsed, in UTC time.</param>
        internal ElapsedEventArgs(DateTime elapsedTime) {
            _elapsedTime = elapsedTime;
        }

        private DateTime _elapsedTime;

        /// <summary>
        /// Returns the UTC time at which the Timer elapsed
        /// </summary>
        public DateTime ElapsedTime {
            get {
                return _elapsedTime;
            }
        }
    }
}
