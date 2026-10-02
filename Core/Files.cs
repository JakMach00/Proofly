using System;
using System.Globalization;
using System.IO;

namespace Proofly.Core
{
    /// <summary>Names, sizes and durations shown in the gallery and on disk.</summary>
    public static class Files
    {
        public static string Stamp()
        {
            return Stamp(DateTime.Now);
        }

        public static string Stamp(DateTime moment)
        {
            return moment.ToString("yyyyMMdd_HHmmss", CultureInfo.InvariantCulture);
        }

        /// <summary>Adds a counter to the name rather than overwriting an existing file.</summary>
        public static string UniquePath(string candidate)
        {
            if (!File.Exists(candidate)) return candidate;
            string dir = Path.GetDirectoryName(candidate) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(candidate);
            string ext = Path.GetExtension(candidate);
            for (int counter = 2; ; counter++)
            {
                string attempt = Path.Combine(dir, stem + " (" + counter.ToString(CultureInfo.InvariantCulture) + ")" + ext);
                if (!File.Exists(attempt)) return attempt;
            }
        }

        public static string FormatDuration(long milliseconds)
        {
            long total = (long)Math.Round(milliseconds / 1000.0);
            return (total / 60).ToString("00", CultureInfo.InvariantCulture) + ":" +
                   (total % 60).ToString("00", CultureInfo.InvariantCulture);
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024) return bytes.ToString(CultureInfo.InvariantCulture) + " B";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("0", CultureInfo.InvariantCulture) + " KB";
            return (bytes / (1024.0 * 1024.0)).ToString("0.0", CultureInfo.InvariantCulture) + " MB";
        }
    }
}
