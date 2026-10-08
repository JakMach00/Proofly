using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace Proofly.Core
{
    /// <summary>
    /// Everything the app remembers between runs, kept as one JSON file in the
    /// user profile. A missing or damaged file simply falls back to defaults.
    /// </summary>
    public sealed class AppSettings
    {
        public const string ActionCapture = "capture";
        public const string ActionRegion = "region";
        public const string ActionRecord = "record";
        public const string ActionPause = "pause";
        public const string ActionExport = "export";

        public static readonly string[] Actions =
        {
            ActionCapture, ActionRegion, ActionRecord, ActionPause, ActionExport,
        };

        /// <summary>"dark" or "light". Empty follows the Windows setting on first run.</summary>
        public string Theme { get; set; } = "";

        public string Quality { get; set; } = "medium";
        public string AudioSource { get; set; } = "none";
        public bool HideOnCapture { get; set; } = true;

        /// <summary>
        /// When on, saving a document keeps the session and makes a fresh one
        /// current. Off by default: the session stays open after saving.
        /// </summary>
        public bool NewSessionAfterExport { get; set; }

        /// <summary>"pdf" or "docx".</summary>
        public string ExportFormat { get; set; } = "pdf";

        /// <summary>Draw the mouse pointer into screenshots.</summary>
        public bool IncludeCursor { get; set; } = true;

        /// <summary>Mark mouse clicks in recordings.</summary>
        public bool HighlightClicks { get; set; } = true;

        /// <summary>Folder name of the session that was open when the app closed.</summary>
        public string LastSession { get; set; } = "";

        public bool CompressPdf { get; set; } = true;
        public string SaveDir { get; set; } = "";
        public bool UseSaveDir { get; set; }

        /// <summary>Device name of the display picked in the sidebar.</summary>
        public string Display { get; set; } = "";

        public Dictionary<string, string> Shortcuts { get; set; } = DefaultShortcuts();


        public string EditorTool { get; set; } = "arrow";
        public string EditorColor { get; set; } = "#ff3b30";
        public double EditorWidth { get; set; } = 5;
        public int EditorFontSize { get; set; } = 34;
        public string EditorFont { get; set; } = "Arial";
        public bool EditorOutline { get; set; } = true;

        /// <summary>
        /// Defaults avoid plain Ctrl combinations, which would take the key away
        /// from every other application, and Ctrl+Alt combinations, which AltGr
        /// sends while typing accented characters.
        /// </summary>
        public static Dictionary<string, string> DefaultShortcuts()
        {
            return new Dictionary<string, string>
            {
                { ActionCapture, "Ctrl+Shift+F9" },
                { ActionRegion, "PrintScreen" },
                { ActionRecord, "Ctrl+Shift+F11" },
                { ActionPause, "Ctrl+Shift+F10" },
                { ActionExport, "Ctrl+Shift+F12" },
            };
        }

        public string Shortcut(string action)
        {
            return Shortcuts != null && Shortcuts.TryGetValue(action, out string value) ? value ?? "" : "";
        }

        public static string DefaultPath
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Proofly", "settings.json");
            }
        }

        public static AppSettings Load()
        {
            return Load(DefaultPath);
        }

        public static AppSettings Load(string path)
        {
            AppSettings settings = null;
            try
            {
                if (File.Exists(path))
                    settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path));
            }
            catch (Exception)
            {
                settings = null;
            }
            if (settings == null) settings = new AppSettings();
            settings.Normalize();
            return settings;
        }

        public void Save()
        {
            Save(DefaultPath);
        }

        public void Save(string path)
        {
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(path, JsonSerializer.Serialize(this, options));
            }
            catch (Exception)
            {
                // Settings are a convenience. Failing to store them must never
                // interrupt a capture.
            }
        }

        /// <summary>Repairs values a hand edited or older file may carry.</summary>
        private void Normalize()
        {
            Dictionary<string, string> defaults = DefaultShortcuts();
            if (Shortcuts == null) Shortcuts = defaults;
            foreach (string action in Actions)
                if (!Shortcuts.ContainsKey(action)) Shortcuts[action] = defaults[action];


            if (Quality != "low" && Quality != "medium" && Quality != "high") Quality = "medium";
            if (AudioSource != "none" && AudioSource != "mic" && AudioSource != "system" && AudioSource != "both")
                AudioSource = "none";
            if (Theme != "dark" && Theme != "light") Theme = "";
            if (ExportFormat != "pdf" && ExportFormat != "docx") ExportFormat = "pdf";
            if (LastSession == null) LastSession = "";
            if (SaveDir == null) SaveDir = "";
            if (Display == null) Display = "";
            if (string.IsNullOrEmpty(EditorTool)) EditorTool = "arrow";
            if (string.IsNullOrEmpty(EditorColor)) EditorColor = "#ff3b30";
            if (string.IsNullOrEmpty(EditorFont)) EditorFont = "Arial";
            if (EditorWidth < 2 || EditorWidth > 14) EditorWidth = 5;
            if (EditorFontSize < 10 || EditorFontSize > 200) EditorFontSize = 34;
        }
    }
}
