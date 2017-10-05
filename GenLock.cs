using System.Threading;

namespace FFMEGE {
    internal static class GenLock {
        internal static readonly AutoResetEvent GenLockEvent = new AutoResetEvent(false);
        private static Timer GenLockTimer;

        internal static void TimerStart() {
            GenLockTimer = new Timer(1000 / 25);
            GenLockTimer.AutoReset = true;
            GenLockTimer.Elapsed += GenLockTimer_Elapsed;

            GenLockTimer.Enabled = true;
        }

        internal static void TimerStop() {
            GenLockTimer.Enabled = false;
            GenLockTimer.Dispose();
            GenLockTimer = null;
        }

        private static void GenLockTimer_Elapsed(object sender, ElapsedEventArgs e) {
            //Sync playback threads
            GenLockEvent.Set();
        }
    }
}
