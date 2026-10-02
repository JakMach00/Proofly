using System;
using Microsoft.Win32;

namespace Proofly.Services
{
    /// <summary>
    /// Start with Windows, through the per user Run key. The entry points at
    /// this copy of the executable, so moving the folder breaks it until the
    /// option is switched off and on again.
    /// </summary>
    public static class Autostart
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string ValueName = "Proofly";
        public const string HiddenArgument = "--hidden";

        private static string Command
        {
            get { return "\"" + Environment.ProcessPath + "\" " + HiddenArgument; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    string value = key == null ? null : key.GetValue(ValueName) as string;
                    return string.Equals(value, Command, StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (key == null) throw new InvalidOperationException("The startup list could not be opened.");
                if (enabled) key.SetValue(ValueName, Command, RegistryValueKind.String);
                else key.DeleteValue(ValueName, false);
            }
        }
    }
}
