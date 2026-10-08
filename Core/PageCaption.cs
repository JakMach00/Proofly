using System;
using System.Collections.Generic;
using System.Text;

namespace Proofly.Core
{
    /// <summary>
    /// The text printed under a screenshot: a bold heading such as "Step 3",
    /// an optional detail on the same line (the capture time), and the note.
    /// </summary>
    public sealed class PageCaption
    {
        public string Heading = "";
        public string Detail = "";
        public string Note = "";
    }

    /// <summary>
    /// Measures and wraps caption text. Both documents use Helvetica metrics:
    /// the PDF prints in Helvetica itself, and Arial, used in the Word file, has
    /// the same character widths. So the layout worked out here holds for both.
    /// </summary>
    public static class CaptionText
    {
        public const double FontSize = 10.5;
        public const double LineHeight = 14;

        /// <summary>Space between the screenshot and its caption.</summary>
        public const double Gap = 10;

        /// <summary>The caption is at least this wide, so a narrow screenshot does not squeeze it.</summary>
        public const double MinWidth = 420;

        /// <summary>A note longer than this is cut, so the screenshot always keeps most of the page.</summary>
        public const int MaxNoteLines = 12;

        /// <summary>Longest note the editor accepts.</summary>
        public const int MaxNoteLength = 500;

        private struct Glyph
        {
            public byte Code;
            public int Regular;
            public int Bold;
        }

        /// <summary>
        /// Glyph names for codes 1 to 16 of the PDF font encoding. Windows-1252
        /// has no room for the Polish letters, so they take codes no text uses.
        /// </summary>
        public const string PdfDifferences = "1 /Aogonek /aogonek /Cacute /cacute /Eogonek /eogonek /Lslash /lslash /Nacute /nacute /Sacute /sacute /Zacute /zacute /Zdotaccent /zdotaccent";

        private static readonly Dictionary<char, Glyph> Glyphs = Build();

        private static Dictionary<char, Glyph> Build()
        {
            int[,] table =
            {
                // Unicode, code in the font encoding, width in Helvetica, width in Helvetica-Bold,
                // in thousandths of the font size.
            { 0x0020, 0x20, 278, 278 },
            { 0x0021, 0x21, 278, 333 },
            { 0x0022, 0x22, 355, 474 },
            { 0x0023, 0x23, 556, 556 },
            { 0x0024, 0x24, 556, 556 },
            { 0x0025, 0x25, 889, 889 },
            { 0x0026, 0x26, 667, 722 },
            { 0x0027, 0x27, 191, 238 },
            { 0x0028, 0x28, 333, 333 },
            { 0x0029, 0x29, 333, 333 },
            { 0x002A, 0x2A, 389, 389 },
            { 0x002B, 0x2B, 584, 584 },
            { 0x002C, 0x2C, 278, 278 },
            { 0x002D, 0x2D, 333, 333 },
            { 0x002E, 0x2E, 278, 278 },
            { 0x002F, 0x2F, 278, 278 },
            { 0x0030, 0x30, 556, 556 },
            { 0x0031, 0x31, 556, 556 },
            { 0x0032, 0x32, 556, 556 },
            { 0x0033, 0x33, 556, 556 },
            { 0x0034, 0x34, 556, 556 },
            { 0x0035, 0x35, 556, 556 },
            { 0x0036, 0x36, 556, 556 },
            { 0x0037, 0x37, 556, 556 },
            { 0x0038, 0x38, 556, 556 },
            { 0x0039, 0x39, 556, 556 },
            { 0x003A, 0x3A, 278, 333 },
            { 0x003B, 0x3B, 278, 333 },
            { 0x003C, 0x3C, 584, 584 },
            { 0x003D, 0x3D, 584, 584 },
            { 0x003E, 0x3E, 584, 584 },
            { 0x003F, 0x3F, 556, 611 },
            { 0x0040, 0x40, 1015, 975 },
            { 0x0041, 0x41, 667, 722 },
            { 0x0042, 0x42, 667, 722 },
            { 0x0043, 0x43, 722, 722 },
            { 0x0044, 0x44, 722, 722 },
            { 0x0045, 0x45, 667, 667 },
            { 0x0046, 0x46, 611, 611 },
            { 0x0047, 0x47, 778, 778 },
            { 0x0048, 0x48, 722, 722 },
            { 0x0049, 0x49, 278, 278 },
            { 0x004A, 0x4A, 500, 556 },
            { 0x004B, 0x4B, 667, 722 },
            { 0x004C, 0x4C, 556, 611 },
            { 0x004D, 0x4D, 833, 833 },
            { 0x004E, 0x4E, 722, 722 },
            { 0x004F, 0x4F, 778, 778 },
            { 0x0050, 0x50, 667, 667 },
            { 0x0051, 0x51, 778, 778 },
            { 0x0052, 0x52, 722, 722 },
            { 0x0053, 0x53, 667, 667 },
            { 0x0054, 0x54, 611, 611 },
            { 0x0055, 0x55, 722, 722 },
            { 0x0056, 0x56, 667, 667 },
            { 0x0057, 0x57, 944, 944 },
            { 0x0058, 0x58, 667, 667 },
            { 0x0059, 0x59, 667, 667 },
            { 0x005A, 0x5A, 611, 611 },
            { 0x005B, 0x5B, 278, 333 },
            { 0x005C, 0x5C, 278, 278 },
            { 0x005D, 0x5D, 278, 333 },
            { 0x005E, 0x5E, 469, 584 },
            { 0x005F, 0x5F, 556, 556 },
            { 0x0060, 0x60, 333, 333 },
            { 0x0061, 0x61, 556, 556 },
            { 0x0062, 0x62, 556, 611 },
            { 0x0063, 0x63, 500, 556 },
            { 0x0064, 0x64, 556, 611 },
            { 0x0065, 0x65, 556, 556 },
            { 0x0066, 0x66, 278, 333 },
            { 0x0067, 0x67, 556, 611 },
            { 0x0068, 0x68, 556, 611 },
            { 0x0069, 0x69, 222, 278 },
            { 0x006A, 0x6A, 222, 278 },
            { 0x006B, 0x6B, 500, 556 },
            { 0x006C, 0x6C, 222, 278 },
            { 0x006D, 0x6D, 833, 889 },
            { 0x006E, 0x6E, 556, 611 },
            { 0x006F, 0x6F, 556, 611 },
            { 0x0070, 0x70, 556, 611 },
            { 0x0071, 0x71, 556, 611 },
            { 0x0072, 0x72, 333, 389 },
            { 0x0073, 0x73, 500, 556 },
            { 0x0074, 0x74, 278, 333 },
            { 0x0075, 0x75, 556, 611 },
            { 0x0076, 0x76, 500, 556 },
            { 0x0077, 0x77, 722, 778 },
            { 0x0078, 0x78, 500, 556 },
            { 0x0079, 0x79, 500, 556 },
            { 0x007A, 0x7A, 500, 500 },
            { 0x007B, 0x7B, 334, 389 },
            { 0x007C, 0x7C, 260, 280 },
            { 0x007D, 0x7D, 334, 389 },
            { 0x007E, 0x7E, 584, 584 },
            { 0x20AC, 0x80, 556, 556 },
            { 0x201A, 0x82, 222, 278 },
            { 0x0192, 0x83, 556, 556 },
            { 0x201E, 0x84, 333, 500 },
            { 0x2026, 0x85, 1000, 1000 },
            { 0x2020, 0x86, 556, 556 },
            { 0x2021, 0x87, 556, 556 },
            { 0x02C6, 0x88, 333, 333 },
            { 0x2030, 0x89, 1000, 1000 },
            { 0x0160, 0x8A, 667, 667 },
            { 0x2039, 0x8B, 333, 333 },
            { 0x0152, 0x8C, 1000, 1000 },
            { 0x017D, 0x8E, 611, 611 },
            { 0x2018, 0x91, 222, 278 },
            { 0x2019, 0x92, 222, 278 },
            { 0x201C, 0x93, 333, 500 },
            { 0x201D, 0x94, 333, 500 },
            { 0x2022, 0x95, 350, 350 },
            { 0x2013, 0x96, 556, 556 },
            { 0x2014, 0x97, 1000, 1000 },
            { 0x02DC, 0x98, 333, 333 },
            { 0x2122, 0x99, 1000, 1000 },
            { 0x0161, 0x9A, 500, 556 },
            { 0x203A, 0x9B, 333, 333 },
            { 0x0153, 0x9C, 944, 944 },
            { 0x017E, 0x9E, 500, 500 },
            { 0x0178, 0x9F, 667, 667 },
            { 0x00A0, 0xA0, 278, 278 },
            { 0x00A1, 0xA1, 333, 333 },
            { 0x00A2, 0xA2, 556, 556 },
            { 0x00A3, 0xA3, 556, 556 },
            { 0x00A4, 0xA4, 556, 556 },
            { 0x00A5, 0xA5, 556, 556 },
            { 0x00A6, 0xA6, 260, 280 },
            { 0x00A7, 0xA7, 556, 556 },
            { 0x00A8, 0xA8, 333, 333 },
            { 0x00A9, 0xA9, 737, 737 },
            { 0x00AA, 0xAA, 370, 370 },
            { 0x00AB, 0xAB, 556, 556 },
            { 0x00AC, 0xAC, 584, 584 },
            { 0x00AD, 0xAD, 333, 333 },
            { 0x00AE, 0xAE, 737, 737 },
            { 0x00AF, 0xAF, 333, 333 },
            { 0x00B0, 0xB0, 400, 400 },
            { 0x00B1, 0xB1, 584, 584 },
            { 0x00B4, 0xB4, 333, 333 },
            { 0x00B5, 0xB5, 556, 611 },
            { 0x00B6, 0xB6, 537, 556 },
            { 0x00B7, 0xB7, 278, 278 },
            { 0x00B8, 0xB8, 333, 333 },
            { 0x00BA, 0xBA, 365, 365 },
            { 0x00BB, 0xBB, 556, 556 },
            { 0x00BC, 0xBC, 834, 834 },
            { 0x00BD, 0xBD, 834, 834 },
            { 0x00BE, 0xBE, 834, 834 },
            { 0x00BF, 0xBF, 611, 611 },
            { 0x00C0, 0xC0, 667, 722 },
            { 0x00C1, 0xC1, 667, 722 },
            { 0x00C2, 0xC2, 667, 722 },
            { 0x00C3, 0xC3, 667, 722 },
            { 0x00C4, 0xC4, 667, 722 },
            { 0x00C5, 0xC5, 667, 722 },
            { 0x00C6, 0xC6, 1000, 1000 },
            { 0x00C7, 0xC7, 722, 722 },
            { 0x00C8, 0xC8, 667, 667 },
            { 0x00C9, 0xC9, 667, 667 },
            { 0x00CA, 0xCA, 667, 667 },
            { 0x00CB, 0xCB, 667, 667 },
            { 0x00CC, 0xCC, 278, 278 },
            { 0x00CD, 0xCD, 278, 278 },
            { 0x00CE, 0xCE, 278, 278 },
            { 0x00CF, 0xCF, 278, 278 },
            { 0x00D0, 0xD0, 722, 722 },
            { 0x00D1, 0xD1, 722, 722 },
            { 0x00D2, 0xD2, 778, 778 },
            { 0x00D3, 0xD3, 778, 778 },
            { 0x00D4, 0xD4, 778, 778 },
            { 0x00D5, 0xD5, 778, 778 },
            { 0x00D6, 0xD6, 778, 778 },
            { 0x00D7, 0xD7, 584, 584 },
            { 0x00D8, 0xD8, 778, 778 },
            { 0x00D9, 0xD9, 722, 722 },
            { 0x00DA, 0xDA, 722, 722 },
            { 0x00DB, 0xDB, 722, 722 },
            { 0x00DC, 0xDC, 722, 722 },
            { 0x00DD, 0xDD, 667, 667 },
            { 0x00DE, 0xDE, 667, 667 },
            { 0x00DF, 0xDF, 611, 611 },
            { 0x00E0, 0xE0, 556, 556 },
            { 0x00E1, 0xE1, 556, 556 },
            { 0x00E2, 0xE2, 556, 556 },
            { 0x00E3, 0xE3, 556, 556 },
            { 0x00E4, 0xE4, 556, 556 },
            { 0x00E5, 0xE5, 556, 556 },
            { 0x00E6, 0xE6, 889, 889 },
            { 0x00E7, 0xE7, 500, 556 },
            { 0x00E8, 0xE8, 556, 556 },
            { 0x00E9, 0xE9, 556, 556 },
            { 0x00EA, 0xEA, 556, 556 },
            { 0x00EB, 0xEB, 556, 556 },
            { 0x00EC, 0xEC, 278, 278 },
            { 0x00ED, 0xED, 278, 278 },
            { 0x00EE, 0xEE, 278, 278 },
            { 0x00EF, 0xEF, 278, 278 },
            { 0x00F0, 0xF0, 556, 611 },
            { 0x00F1, 0xF1, 556, 611 },
            { 0x00F2, 0xF2, 556, 611 },
            { 0x00F3, 0xF3, 556, 611 },
            { 0x00F4, 0xF4, 556, 611 },
            { 0x00F5, 0xF5, 556, 611 },
            { 0x00F6, 0xF6, 556, 611 },
            { 0x00F7, 0xF7, 584, 584 },
            { 0x00F8, 0xF8, 611, 611 },
            { 0x00F9, 0xF9, 556, 611 },
            { 0x00FA, 0xFA, 556, 611 },
            { 0x00FB, 0xFB, 556, 611 },
            { 0x00FC, 0xFC, 556, 611 },
            { 0x00FD, 0xFD, 500, 556 },
            { 0x00FE, 0xFE, 556, 611 },
            { 0x00FF, 0xFF, 500, 556 },
            { 0x0104, 0x01, 667, 722 },
            { 0x0105, 0x02, 556, 556 },
            { 0x0106, 0x03, 722, 722 },
            { 0x0107, 0x04, 500, 556 },
            { 0x0118, 0x05, 667, 667 },
            { 0x0119, 0x06, 556, 556 },
            { 0x0141, 0x07, 556, 611 },
            { 0x0142, 0x08, 222, 278 },
            { 0x0143, 0x09, 722, 722 },
            { 0x0144, 0x0A, 556, 611 },
            { 0x015A, 0x0B, 667, 667 },
            { 0x015B, 0x0C, 500, 556 },
            { 0x0179, 0x0D, 611, 611 },
            { 0x017A, 0x0E, 500, 500 },
            { 0x017B, 0x0F, 611, 611 },
            { 0x017C, 0x10, 500, 500 },
            };
            var glyphs = new Dictionary<char, Glyph>();
            for (int i = 0; i < table.GetLength(0); i++)
            {
                glyphs[(char)table[i, 0]] = new Glyph
                {
                    Code = (byte)table[i, 1],
                    Regular = table[i, 2],
                    Bold = table[i, 3],
                };
            }
            return glyphs;
        }

        /// <summary>Characters the PDF fonts cannot show are printed as a question mark.</summary>
        private static Glyph Lookup(char c)
        {
            Glyph glyph;
            if (c == '\t') c = ' ';
            return Glyphs.TryGetValue(c, out glyph) ? glyph : Glyphs['?'];
        }

        public static double Measure(string text, bool bold, double size)
        {
            double total = 0;
            foreach (char c in text ?? string.Empty)
            {
                Glyph glyph = Lookup(c);
                total += bold ? glyph.Bold : glyph.Regular;
            }
            return total * size / 1000.0;
        }

        /// <summary>The bytes that print the text in the PDF fonts.</summary>
        public static byte[] PdfBytes(string text)
        {
            string value = text ?? string.Empty;
            var bytes = new byte[value.Length];
            for (int i = 0; i < value.Length; i++) bytes[i] = Lookup(value[i]).Code;
            return bytes;
        }

        /// <summary>Removes what has no place in a caption: carriage returns, other control characters, outer blank space.</summary>
        public static string Clean(string text)
        {
            if (string.IsNullOrEmpty(text)) return "";
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c == '\n') sb.Append(c);
                else if (c == '\t') sb.Append(' ');
                else if (!char.IsControl(c)) sb.Append(c);
            }
            return sb.ToString().Trim();
        }

        /// <summary>
        /// Breaks the note into lines that fit the width, in the regular font.
        /// Line breaks typed by the user are kept. A word too long for a line is
        /// split. Past <see cref="MaxNoteLines"/> the text is cut and ends in "...".
        /// </summary>
        public static List<string> Wrap(string text, double width)
        {
            var lines = new List<string>();
            string clean = Clean(text);
            if (clean.Length == 0) return lines;

            foreach (string paragraph in clean.Split('\n'))
            {
                string current = "";
                foreach (string word in paragraph.Split(' '))
                {
                    string candidate = current.Length == 0 ? word : current + " " + word;
                    if (Measure(candidate, false, FontSize) <= width)
                    {
                        current = candidate;
                        continue;
                    }
                    if (current.Length > 0) lines.Add(current);
                    current = word;
                    // A single word wider than the line is broken where it has to be.
                    while (Measure(current, false, FontSize) > width && current.Length > 1)
                    {
                        int fit = 1;
                        while (fit < current.Length && Measure(current.Substring(0, fit + 1), false, FontSize) <= width) fit++;
                        lines.Add(current.Substring(0, fit));
                        current = current.Substring(fit);
                    }
                }
                lines.Add(current);
            }

            if (lines.Count > MaxNoteLines)
            {
                lines.RemoveRange(MaxNoteLines, lines.Count - MaxNoteLines);
                string last = lines[MaxNoteLines - 1];
                while (last.Length > 0 && Measure(last + "...", false, FontSize) > width) last = last.Substring(0, last.Length - 1);
                lines[MaxNoteLines - 1] = last.TrimEnd() + "...";
            }
            return lines;
        }

        /// <summary>Width of the caption under a screenshot drawn <paramref name="imageWidth"/> wide.</summary>
        public static double TextWidth(double imageWidth, double available)
        {
            return Math.Min(available, Math.Max(imageWidth, MinWidth));
        }

        /// <summary>
        /// Height the caption takes, gap included. Wrapping at the narrowest
        /// width the caption can get gives an upper bound, which is what the
        /// page layout reserves before it knows the size of the screenshot.
        /// </summary>
        public static double ReservedHeight(PageCaption caption, double available)
        {
            if (caption == null) return 0;
            int lines = 1 + Wrap(caption.Note, Math.Min(available, MinWidth)).Count;
            return Gap + lines * LineHeight;
        }
    }
}
