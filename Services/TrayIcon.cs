using System;
using System.IO;
using WinForms = System.Windows.Forms;

namespace Proofly.Services
{
    /// <summary>
    /// The notification area icon. WPF has none of its own, so this is the one
    /// place that uses Windows Forms.
    /// </summary>
    public sealed class TrayIcon : IDisposable
    {
        private readonly WinForms.NotifyIcon _icon;
        private bool _noticeShown;

        public event Action OpenRequested;
        public event Action RegionRequested;
        public event Action QuitRequested;

        public TrayIcon(Stream iconStream)
        {
            var menu = new WinForms.ContextMenuStrip();
            menu.Items.Add("Open Proofly", null, delegate { Fire(OpenRequested); });
            menu.Items.Add("Capture a region", null, delegate { Fire(RegionRequested); });
            menu.Items.Add(new WinForms.ToolStripSeparator());
            menu.Items.Add("Quit", null, delegate { Fire(QuitRequested); });

            _icon = new WinForms.NotifyIcon();
            _icon.Text = "Proofly";
            _icon.Icon = iconStream != null
                ? new System.Drawing.Icon(iconStream, WinForms.SystemInformation.SmallIconSize)
                : System.Drawing.SystemIcons.Application;
            _icon.ContextMenuStrip = menu;
            _icon.DoubleClick += delegate { Fire(OpenRequested); };
            _icon.Visible = true;
        }

        /// <summary>Shown once per session, the first time the window is closed to the tray.</summary>
        public void ShowStillRunningNotice()
        {
            if (_noticeShown) return;
            _noticeShown = true;
            _icon.ShowBalloonTip(
                4000,
                "Still running in the tray",
                "Print Screen keeps working. Quit from the tray icon menu.",
                WinForms.ToolTipIcon.Info);
        }

        public void Dispose()
        {
            _icon.Visible = false;
            _icon.Dispose();
        }

        private static void Fire(Action handler)
        {
            if (handler != null) handler();
        }
    }
}
