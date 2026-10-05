using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Proofly.Core;
using Proofly.Services;

namespace Proofly
{
    public partial class App : Application
    {
        public static AppSettings Settings { get; private set; }

        /// <summary>Version shown in the status bar, taken from the project file.</summary>
        public static string Version
        {
            get
            {
                object[] attributes = typeof(App).Assembly.GetCustomAttributes(
                    typeof(System.Reflection.AssemblyInformationalVersionAttribute), false);
                if (attributes.Length > 0)
                {
                    string value = ((System.Reflection.AssemblyInformationalVersionAttribute)attributes[0]).InformationalVersion;
                    int plus = value.IndexOf('+');
                    return plus > 0 ? value.Substring(0, plus) : value;
                }
                System.Version version = typeof(App).Assembly.GetName().Version;
                return version == null ? "" : version.ToString(3);
            }
        }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            bool first = SingleInstance.Acquire(delegate
            {
                Dispatcher.BeginInvoke(new Action(delegate
                {
                    var window = MainWindow as MainWindow;
                    if (window != null) window.ShowFromTray();
                }));
            });
            if (!first)
            {
                Shutdown();
                return;
            }

            DispatcherUnhandledException += OnUnhandledException;

            Settings = AppSettings.Load();
            ThemeManager.Apply(string.IsNullOrEmpty(Settings.Theme) ? ThemeManager.SystemPreference() : Settings.Theme);

            // Launched by Windows at sign in, which should stay out of sight.
            bool hidden = e.Args.Contains(Autostart.HiddenArgument, StringComparer.OrdinalIgnoreCase);
            var main = new MainWindow();
            MainWindow = main;
            main.Start(hidden);
        }

        /// <summary>
        /// An unexpected error is written next to the settings and shown in the
        /// status bar. The session is kept, so nothing captured is lost.
        /// </summary>
        /// <summary>Full path of the file errors are written to.</summary>
        public static string ErrorLogPath
        {
            get { return Path.Combine(Path.GetDirectoryName(AppSettings.DefaultPath) ?? "", "error.log"); }
        }

        /// <summary>
        /// Appends an error with all its details to the log next to the
        /// settings. The status bar only has room for one line, the log keeps
        /// the rest for whoever has to find the cause.
        /// </summary>
        public static void LogError(string context, Exception error)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(ErrorLogPath));
                File.AppendAllText(
                    ErrorLogPath,
                    DateTime.Now.ToString("s") + " " + context + Environment.NewLine + error + Environment.NewLine +
                    Environment.NewLine);
            }
            catch (Exception)
            {
                // The log is a courtesy. Failing to write it changes nothing.
            }
        }

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            LogError("Unexpected error", e.Exception);

            var window = MainWindow as MainWindow;
            if (window != null) window.ReportError("Unexpected error: " + e.Exception.Message);
            e.Handled = true;
        }
    }
}
