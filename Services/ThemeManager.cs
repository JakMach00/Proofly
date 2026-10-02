using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using Microsoft.Win32;

namespace Proofly.Services
{
    /// <summary>
    /// Swaps the colour brushes the whole interface refers to. Every style uses
    /// these keys through DynamicResource, so replacing them repaints the app.
    /// </summary>
    public static class ThemeManager
    {
        public const string Dark = "dark";
        public const string Light = "light";

        private static readonly Dictionary<string, string> DarkPalette = new Dictionary<string, string>
        {
            { "Bg", "#FF0D0F11" },
            { "Panel", "#FF141719" },
            { "Panel2", "#FF191D20" },
            { "Raised", "#FF22272B" },
            { "Line", "#FF262C31" },
            { "LineStrong", "#FF333B41" },
            { "Text", "#FFE8EDEF" },
            { "Muted", "#FF8B969D" },
            { "Accent", "#FFB6F24A" },
            { "AccentSoft", "#24B6F24A" },
            { "AccentText", "#FF10160A" },
            { "Danger", "#FFEF5350" },
            { "Warn", "#FFF0B429" },
            { "CanvasBg", "#FF101315" },
            { "Backdrop", "#8C000000" },
        };

        private static readonly Dictionary<string, string> LightPalette = new Dictionary<string, string>
        {
            { "Bg", "#FFF5F7F5" },
            { "Panel", "#FFFFFFFF" },
            { "Panel2", "#FFF1F4F1" },
            { "Raised", "#FFE6EBE6" },
            { "Line", "#FFDDE3DD" },
            { "LineStrong", "#FFC6CEC6" },
            { "Text", "#FF141A15" },
            { "Muted", "#FF5D6A60" },
            { "Accent", "#FF8FD420" },
            { "AccentSoft", "#2E8FD420" },
            { "AccentText", "#FF131A08" },
            { "Danger", "#FFCF3B38" },
            { "Warn", "#FFA06B00" },
            { "CanvasBg", "#FFE9EDE9" },
            { "Backdrop", "#8C000000" },
        };

        public static string Current { get; private set; }

        public static void Apply(string theme)
        {
            Current = theme == Light ? Light : Dark;
            Dictionary<string, string> palette = Current == Light ? LightPalette : DarkPalette;
            ResourceDictionary resources = Application.Current.Resources;
            foreach (KeyValuePair<string, string> entry in palette)
            {
                var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(entry.Value));
                brush.Freeze();
                resources[entry.Key] = brush;
            }
        }

        /// <summary>The Windows app theme, used on the very first run.</summary>
        public static string SystemPreference()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", false))
                {
                    object value = key == null ? null : key.GetValue("AppsUseLightTheme");
                    if (value is int && (int)value == 1) return Light;
                }
            }
            catch (Exception)
            {
                // Falls through to the dark default.
            }
            return Dark;
        }

        /// <summary>Makes the native title bar follow the theme.</summary>
        public static void ApplyTitleBar(Window window)
        {
            try
            {
                IntPtr handle = new WindowInteropHelper(window).Handle;
                if (handle == IntPtr.Zero) return;
                int dark = Current == Dark ? 1 : 0;
                Native.DwmSetWindowAttribute(handle, Native.DwmUseImmersiveDarkMode, ref dark, sizeof(int));
            }
            catch (Exception)
            {
                // Older Windows builds do not know the attribute. The title bar
                // just keeps its default colour there.
            }
        }
    }
}
