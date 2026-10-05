using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Proofly.Core
{
    /// <summary>One screenshot or recording as it is written to the session file.</summary>
    public sealed class SessionItem
    {
        public string Id { get; set; } = "";

        /// <summary>"image" or "video".</summary>
        public string Kind { get; set; } = "image";

        /// <summary>File name inside the session folder.</summary>
        public string File { get; set; } = "";

        /// <summary>Small preview kept next to the file, so opening a session is quick.</summary>
        public string Thumb { get; set; } = "";

        public string Name { get; set; } = "";
        public int Width { get; set; }
        public int Height { get; set; }
        public long Size { get; set; }
        public long DurationMs { get; set; }
        public bool HasAudio { get; set; }
    }

    /// <summary>
    /// A named set of evidence. Everything in it lives in one folder on disk
    /// from the moment it is captured, which is what makes a session survive a
    /// crash or a restart and lets the user come back to it later.
    /// </summary>
    public sealed class SessionData
    {
        public string Name { get; set; } = "";
        public DateTime Created { get; set; }

        /// <summary>When the session was last saved to a document, if ever.</summary>
        public DateTime? Exported { get; set; }

        public List<SessionItem> Items { get; set; } = new List<SessionItem>();

        /// <summary>Full path of the folder holding this session.</summary>
        [JsonIgnore]
        public string Folder { get; set; }

        public string PathOf(string fileName)
        {
            return Path.Combine(Folder, fileName);
        }

        public override string ToString()
        {
            return Name;
        }
    }

    public static class SessionStore
    {
        private const string IndexFile = "session.json";

        /// <summary>
        /// Sessions sit in the local profile, not the roaming one, so large
        /// files are never synchronised to a server along with the profile.
        /// </summary>
        public static string DefaultRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Proofly", "Sessions");
            }
        }

        /// <summary>Every session found on disk, oldest first.</summary>
        public static List<SessionData> LoadAll(string root)
        {
            var sessions = new List<SessionData>();
            if (!Directory.Exists(root)) return sessions;
            foreach (string folder in Directory.GetDirectories(root))
            {
                string index = Path.Combine(folder, IndexFile);
                if (!System.IO.File.Exists(index)) continue;
                try
                {
                    SessionData session = JsonSerializer.Deserialize<SessionData>(System.IO.File.ReadAllText(index));
                    if (session == null) continue;
                    session.Folder = folder;
                    if (session.Items == null) session.Items = new List<SessionItem>();
                    if (string.IsNullOrWhiteSpace(session.Name)) session.Name = Path.GetFileName(folder);
                    // An entry whose file has gone missing is dropped rather than shown broken.
                    session.Items.RemoveAll(item =>
                        item == null || string.IsNullOrEmpty(item.File) || !System.IO.File.Exists(Path.Combine(folder, item.File)));
                    sessions.Add(session);
                }
                catch (Exception)
                {
                    // A damaged index is skipped. Its files stay on disk untouched.
                }
            }
            sessions.Sort((a, b) => a.Created.CompareTo(b.Created));
            return sessions;
        }

        public static SessionData Create(string root, string name)
        {
            Directory.CreateDirectory(root);
            string stem = DateTime.Now.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
            string folder = Path.Combine(root, stem);
            for (int counter = 2; Directory.Exists(folder); counter++)
                folder = Path.Combine(root, stem + "_" + counter.ToString(CultureInfo.InvariantCulture));
            Directory.CreateDirectory(folder);

            var session = new SessionData { Name = name, Created = DateTime.Now, Folder = folder };
            Save(session);
            return session;
        }

        /// <summary>
        /// Writes the index through a temporary file, so a crash in the middle
        /// of saving leaves the previous index in place instead of half of a new one.
        /// </summary>
        public static void Save(SessionData session)
        {
            Directory.CreateDirectory(session.Folder);
            string target = Path.Combine(session.Folder, IndexFile);
            string temporary = target + ".tmp";
            var options = new JsonSerializerOptions { WriteIndented = true };
            System.IO.File.WriteAllText(temporary, JsonSerializer.Serialize(session, options));
            System.IO.File.Move(temporary, target, true);
        }

        public static void Delete(SessionData session)
        {
            if (!string.IsNullOrEmpty(session.Folder) && Directory.Exists(session.Folder))
                Directory.Delete(session.Folder, true);
        }

        /// <summary>
        /// Removes files the index does not mention: a recording cut short by a
        /// crash, or items deleted just before the app closed.
        /// </summary>
        public static void RemoveStrayFiles(SessionData session)
        {
            if (!Directory.Exists(session.Folder)) return;
            var known = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { IndexFile };
            foreach (SessionItem item in session.Items)
            {
                if (!string.IsNullOrEmpty(item.File)) known.Add(item.File);
                if (!string.IsNullOrEmpty(item.Thumb)) known.Add(item.Thumb);
            }
            foreach (string path in Directory.GetFiles(session.Folder))
            {
                if (known.Contains(Path.GetFileName(path))) continue;
                try
                {
                    System.IO.File.Delete(path);
                }
                catch (Exception)
                {
                    // Still open somewhere. It goes the next time the session loads.
                }
            }
        }

        /// <summary>
        /// The number in a name the app gave itself, such as 3 for "Session 3".
        /// Zero for any name the user chose.
        /// </summary>
        public static int DefaultNumber(string name)
        {
            const string prefix = "Session ";
            if (name == null || !name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;
            return int.TryParse(name.Substring(prefix.Length).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out int number) && number > 0
                ? number
                : 0;
        }

        /// <summary>"Session 4" when the highest numbered session so far is "Session 3". Numbering starts at one.</summary>
        public static string NextName(IEnumerable<SessionData> existing)
        {
            int highest = 0;
            foreach (SessionData session in existing) highest = Math.Max(highest, DefaultNumber(session.Name));
            return "Session " + (highest + 1).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>A session name turned into something safe to use as a file name.</summary>
        public static string SafeFileName(string name)
        {
            var result = new StringBuilder();
            char[] invalid = Path.GetInvalidFileNameChars();
            foreach (char c in name ?? "")
                result.Append(invalid.Contains(c) || c < 32 || c == ':' || c == '*' || c == '?' || c == '"' ||
                              c == '<' || c == '>' || c == '|' || c == '\\' || c == '/'
                    ? '_'
                    : c);
            string cleaned = result.ToString().Trim().TrimEnd('.');
            if (cleaned.Length > 80) cleaned = cleaned.Substring(0, 80).Trim();
            return cleaned.Length == 0 ? "documentation" : cleaned;
        }
    }
}
