using System;
using System.Collections.Generic;
using Proofly.Core;

namespace Proofly.Services
{
    /// <summary>
    /// Global shortcuts that keep working while the window is minimized or
    /// hidden in the tray.
    /// </summary>
    public sealed class HotkeyManager
    {
        private readonly IntPtr _window;
        private readonly Dictionary<int, string> _registered = new Dictionary<int, string>();
        private Dictionary<string, string> _bindings = new Dictionary<string, string>();

        public HotkeyManager(IntPtr window)
        {
            _window = window;
        }

        /// <summary>Registers the bindings and returns the ones Windows refused.</summary>
        public List<string> Apply(Dictionary<string, string> bindings)
        {
            _bindings = new Dictionary<string, string>(bindings);
            return Register();
        }

        /// <summary>Used while a new combination is being recorded in the settings.</summary>
        public void Suspend()
        {
            Unregister();
        }

        public List<string> Resume()
        {
            return Register();
        }

        /// <summary>Action bound to the id carried by a WM_HOTKEY message, or null.</summary>
        public string ActionFor(int id)
        {
            return _registered.TryGetValue(id, out string action) ? action : null;
        }

        public void Dispose()
        {
            Unregister();
        }

        private void Unregister()
        {
            foreach (int id in _registered.Keys) Native.UnregisterHotKey(_window, id);
            _registered.Clear();
        }

        private List<string> Register()
        {
            Unregister();
            var failed = new List<string>();
            int id = 1;
            foreach (string action in AppSettings.Actions)
            {
                int current = id++;
                if (!_bindings.TryGetValue(action, out string text) || string.IsNullOrEmpty(text)) continue;
                if (!Accelerators.TryParse(text, out Accelerator accelerator))
                {
                    failed.Add(text);
                    continue;
                }
                bool ok = Native.RegisterHotKey(
                    _window, current, accelerator.Modifiers | Accelerators.ModNoRepeat, accelerator.VirtualKey);
                if (ok) _registered[current] = action;
                else failed.Add(text);
            }
            return failed;
        }
    }
}
