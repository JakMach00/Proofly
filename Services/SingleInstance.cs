using System;
using System.Threading;

namespace Proofly.Services
{
    /// <summary>
    /// A second launch, for example from the Start menu while the app already
    /// sits in the tray, brings the running window forward instead of competing
    /// with it for the global shortcuts.
    /// </summary>
    public static class SingleInstance
    {
        private const string MutexName = "Local\\Proofly.SingleInstance";
        private const string SignalName = "Local\\Proofly.Activate";

        private static Mutex _mutex;
        private static EventWaitHandle _signal;

        /// <summary>
        /// Returns false when another copy is already running, after asking that
        /// copy to show its window.
        /// </summary>
        public static bool Acquire(Action onActivate)
        {
            bool created;
            _mutex = new Mutex(true, MutexName, out created);
            if (!created)
            {
                try
                {
                    using (EventWaitHandle existing = EventWaitHandle.OpenExisting(SignalName))
                        existing.Set();
                }
                catch (Exception)
                {
                    // The first copy is still starting. Nothing more to do.
                }
                return false;
            }

            _signal = new EventWaitHandle(false, EventResetMode.AutoReset, SignalName);
            var listener = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        _signal.WaitOne();
                    }
                    catch (Exception)
                    {
                        return;
                    }
                    onActivate();
                }
            });
            listener.IsBackground = true;
            listener.Name = "Proofly activation listener";
            listener.Start();
            return true;
        }
    }
}
