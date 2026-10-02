using System;
using System.Collections.Generic;
using System.Globalization;

namespace Proofly.Core
{
    /// <summary>A parsed shortcut such as "Ctrl+Shift+F9".</summary>
    public struct Accelerator
    {
        public uint Modifiers;
        public uint VirtualKey;
    }

    /// <summary>
    /// Shortcut text handling. The names match the ones the earlier versions
    /// stored, so a saved binding keeps working.
    /// </summary>
    public static class Accelerators
    {
        public const uint ModAlt = 0x0001;
        public const uint ModControl = 0x0002;
        public const uint ModShift = 0x0004;
        public const uint ModWin = 0x0008;
        public const uint ModNoRepeat = 0x4000;

        public const uint VkPrintScreen = 0x2C;

        private static readonly Dictionary<string, uint> NamedKeys = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase)
        {
            { "PrintScreen", VkPrintScreen },
            { "Space", 0x20 },
            { "Return", 0x0D },
            { "PageUp", 0x21 },
            { "PageDown", 0x22 },
            { "End", 0x23 },
            { "Home", 0x24 },
            { "Left", 0x25 },
            { "Up", 0x26 },
            { "Right", 0x27 },
            { "Down", 0x28 },
            { "Insert", 0x2D },
            { "Pause", 0x13 },
            { "ScrollLock", 0x91 },
            { "Plus", 0xBB },
            { "Minus", 0xBD },
            { "Comma", 0xBC },
            { "Period", 0xBE },
        };

        /// <summary>Name of a key, or null when it cannot be part of a shortcut.</summary>
        public static string KeyName(uint virtualKey)
        {
            if (virtualKey >= 0x41 && virtualKey <= 0x5A) return ((char)virtualKey).ToString();
            if (virtualKey >= 0x30 && virtualKey <= 0x39) return ((char)virtualKey).ToString();
            if (virtualKey >= 0x60 && virtualKey <= 0x69) return "Num" + (virtualKey - 0x60).ToString(CultureInfo.InvariantCulture);
            if (virtualKey >= 0x70 && virtualKey <= 0x87) return "F" + (virtualKey - 0x70 + 1).ToString(CultureInfo.InvariantCulture);
            foreach (KeyValuePair<string, uint> pair in NamedKeys)
                if (pair.Value == virtualKey) return pair.Key;
            return null;
        }

        private static bool TryKey(string name, out uint virtualKey)
        {
            virtualKey = 0;
            if (string.IsNullOrEmpty(name)) return false;
            if (name.Length == 1)
            {
                char c = char.ToUpperInvariant(name[0]);
                if ((c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9'))
                {
                    virtualKey = c;
                    return true;
                }
                return false;
            }
            if ((name[0] == 'F' || name[0] == 'f') &&
                int.TryParse(name.Substring(1), NumberStyles.None, CultureInfo.InvariantCulture, out int fn) &&
                fn >= 1 && fn <= 24)
            {
                virtualKey = (uint)(0x70 + fn - 1);
                return true;
            }
            if (name.StartsWith("Num", StringComparison.OrdinalIgnoreCase) && name.Length == 4 && char.IsDigit(name[3]))
            {
                virtualKey = (uint)(0x60 + (name[3] - '0'));
                return true;
            }
            return NamedKeys.TryGetValue(name, out virtualKey);
        }

        /// <summary>True for keys that work as a global shortcut with no modifier.</summary>
        public static bool IsStandalone(uint virtualKey)
        {
            return (virtualKey >= 0x70 && virtualKey <= 0x87) || virtualKey == VkPrintScreen;
        }

        /// <summary>
        /// Builds the text for a combination, or null when it cannot be
        /// registered globally.
        /// </summary>
        public static string Format(bool ctrl, bool alt, bool shift, bool win, uint virtualKey)
        {
            string name = KeyName(virtualKey);
            if (name == null) return null;
            if (!ctrl && !alt && !shift && !win && !IsStandalone(virtualKey)) return null;
            var parts = new List<string>();
            if (ctrl) parts.Add("Ctrl");
            if (alt) parts.Add("Alt");
            if (shift) parts.Add("Shift");
            if (win) parts.Add("Super");
            parts.Add(name);
            return string.Join("+", parts);
        }

        public static bool TryParse(string text, out Accelerator result)
        {
            result = default(Accelerator);
            if (string.IsNullOrWhiteSpace(text)) return false;
            string[] parts = text.Split('+');
            uint modifiers = 0;
            for (int i = 0; i < parts.Length - 1; i++)
            {
                switch (parts[i].Trim().ToLowerInvariant())
                {
                    case "ctrl":
                    case "control":
                        modifiers |= ModControl;
                        break;
                    case "alt":
                        modifiers |= ModAlt;
                        break;
                    case "shift":
                        modifiers |= ModShift;
                        break;
                    case "super":
                    case "win":
                        modifiers |= ModWin;
                        break;
                    default:
                        return false;
                }
            }
            if (!TryKey(parts[parts.Length - 1].Trim(), out uint key)) return false;
            if (modifiers == 0 && !IsStandalone(key)) return false;
            result.Modifiers = modifiers;
            result.VirtualKey = key;
            return true;
        }

        /// <summary>
        /// True for combinations that AltGr also produces. On an international
        /// layout these fire while typing.
        /// </summary>
        public static bool ConflictsWithAltGr(string text)
        {
            return TryParse(text, out Accelerator a) &&
                   (a.Modifiers & ModControl) != 0 && (a.Modifiers & ModAlt) != 0;
        }
    }
}
