using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;

namespace Proofly.Services
{
    /// <summary>One physical screen, measured in physical pixels.</summary>
    public sealed class DisplayInfo
    {
        /// <summary>Windows device name such as \\.\DISPLAY1. Stable across resolution changes.</summary>
        public string DeviceName { get; set; }

        /// <summary>Generated in the app, because the name Windows reports is localized.</summary>
        public string Name { get; set; }

        public Int32Rect Bounds { get; set; }

        public bool Primary { get; set; }

        public IntPtr Handle { get; set; }

        /// <summary>Text shown in the display picker.</summary>
        public string Label
        {
            get { return Name + (Primary ? " (primary)" : "") + " - " + Bounds.Width + " x " + Bounds.Height; }
        }

        public override string ToString()
        {
            return Label;
        }
    }

    public static class Displays
    {
        public static List<DisplayInfo> List()
        {
            var found = new List<DisplayInfo>();
            Native.MonitorEnumProc callback = delegate(IntPtr monitor, IntPtr hdc, IntPtr rect, IntPtr data)
            {
                DisplayInfo info = Describe(monitor);
                if (info != null) found.Add(info);
                return true;
            };
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero);
            GC.KeepAlive(callback);

            // Device names end in the display number, which gives the same order
            // as the Windows display settings.
            found.Sort(delegate(DisplayInfo a, DisplayInfo b)
            {
                return string.Compare(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
            });
            for (int i = 0; i < found.Count; i++) found[i].Name = "Screen " + (i + 1);
            return found;
        }

        /// <summary>The display under the mouse pointer, or null when it cannot be determined.</summary>
        public static DisplayInfo UnderPointer(List<DisplayInfo> displays)
        {
            Native.POINT point;
            if (!Native.GetCursorPos(out point)) return null;
            IntPtr monitor = Native.MonitorFromPoint(point, Native.MonitorDefaultToNearest);
            return Find(displays, monitor);
        }

        /// <summary>The display a window mostly sits on.</summary>
        public static DisplayInfo OfWindow(List<DisplayInfo> displays, IntPtr window)
        {
            if (window == IntPtr.Zero) return null;
            return Find(displays, Native.MonitorFromWindow(window, Native.MonitorDefaultToNearest));
        }

        private static DisplayInfo Find(List<DisplayInfo> displays, IntPtr monitor)
        {
            foreach (DisplayInfo display in displays)
                if (display.Handle == monitor) return display;
            // Handles change when the display layout does, so fall back to the name.
            DisplayInfo described = Describe(monitor);
            if (described == null) return null;
            foreach (DisplayInfo display in displays)
                if (string.Equals(display.DeviceName, described.DeviceName, StringComparison.OrdinalIgnoreCase))
                    return display;
            return null;
        }

        private static DisplayInfo Describe(IntPtr monitor)
        {
            var info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
            if (!Native.GetMonitorInfo(monitor, ref info)) return null;
            return new DisplayInfo
            {
                Handle = monitor,
                DeviceName = info.szDevice,
                Primary = (info.dwFlags & Native.MonitorInfoPrimary) != 0,
                Bounds = new Int32Rect(
                    info.rcMonitor.Left,
                    info.rcMonitor.Top,
                    info.rcMonitor.Right - info.rcMonitor.Left,
                    info.rcMonitor.Bottom - info.rcMonitor.Top),
            };
        }
    }
}
