using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Proofly.Core
{
    /// <summary>One screenshot ready to be placed on a page.</summary>
    public sealed class PdfImage
    {
        public int Width;
        public int Height;

        /// <summary>
        /// A complete JPEG file when <see cref="IsJpeg"/> is set, otherwise raw
        /// 24 bit RGB rows from top to bottom with no padding.
        /// </summary>
        public byte[] Data;

        public bool IsJpeg;
    }

    /// <summary>
    /// Writes a document with one A4 page per image and nothing else on it.
    /// The format needed here is small enough to write by hand, which keeps a
    /// PDF library out of the dependency list.
    /// </summary>
    public static class PdfWriter
    {
        private const double A4Short = 595.28;
        private const double A4Long = 841.89;
        private const double Margin = 36;

        public static void Write(Stream output, int pageCount, Func<int, PdfImage> getImage, string title)
        {
            if (pageCount <= 0) throw new InvalidOperationException("There are no screenshots to put in the PDF.");

            var writer = new Counter(output);
            // Object numbers are fixed up front so the page tree can be written
            // before the pages: 1 catalog, 2 page tree, then three per page.
            int objectCount = 2 + pageCount * 3 + 1;
            var offsets = new long[objectCount + 1];

            writer.Ascii("%PDF-1.4\n");
            writer.Bytes(new byte[] { (byte)'%', 0xE2, 0xE3, 0xCF, 0xD3, (byte)'\n' });

            offsets[1] = writer.Position;
            writer.Ascii("1 0 obj\n<< /Type /Catalog /Pages 2 0 R >>\nendobj\n");

            var kids = new StringBuilder();
            for (int i = 0; i < pageCount; i++) kids.Append(3 + i * 3).Append(" 0 R ");
            offsets[2] = writer.Position;
            writer.Ascii("2 0 obj\n<< /Type /Pages /Count " + pageCount + " /Kids [ " + kids + "] >>\nendobj\n");

            for (int i = 0; i < pageCount; i++)
            {
                PdfImage image = getImage(i);
                if (image == null || image.Data == null || image.Width <= 0 || image.Height <= 0)
                    throw new InvalidOperationException("A screenshot could not be read.");

                int pageId = 3 + i * 3;
                int contentId = pageId + 1;
                int imageId = pageId + 2;

                bool landscape = image.Width >= image.Height;
                double pageW = landscape ? A4Long : A4Short;
                double pageH = landscape ? A4Short : A4Long;
                double availW = pageW - Margin * 2;
                double availH = pageH - Margin * 2;
                double ratio = Math.Min(availW / image.Width, availH / image.Height);
                double drawW = image.Width * ratio;
                double drawH = image.Height * ratio;
                double x = Margin + (availW - drawW) / 2;
                double y = Margin + (availH - drawH) / 2;

                offsets[pageId] = writer.Position;
                writer.Ascii(
                    pageId + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Num(pageW) + " " + Num(pageH) + "]" +
                    " /Resources << /XObject << /Im0 " + imageId + " 0 R >> >> /Contents " + contentId + " 0 R >>\nendobj\n");

                string content = "q " + Num(drawW) + " 0 0 " + Num(drawH) + " " + Num(x) + " " + Num(y) + " cm /Im0 Do Q\n";
                offsets[contentId] = writer.Position;
                writer.Ascii(contentId + " 0 obj\n<< /Length " + content.Length + " >>\nstream\n" + content + "endstream\nendobj\n");

                byte[] payload = image.IsJpeg ? image.Data : Deflate(image.Data);
                offsets[imageId] = writer.Position;
                writer.Ascii(
                    imageId + " 0 obj\n<< /Type /XObject /Subtype /Image /Width " + image.Width + " /Height " + image.Height +
                    " /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter " + (image.IsJpeg ? "/DCTDecode" : "/FlateDecode") +
                    " /Length " + payload.Length + " >>\nstream\n");
                writer.Bytes(payload);
                writer.Ascii("\nendstream\nendobj\n");
            }

            int infoId = objectCount;
            offsets[infoId] = writer.Position;
            writer.Ascii(
                infoId + " 0 obj\n<< /Title " + Literal(title) + " /Creator (Proofly) /Producer (Proofly)" +
                " /CreationDate (D:" + DateTime.Now.ToString("yyyyMMddHHmmss", CultureInfo.InvariantCulture) + ") >>\nendobj\n");

            long xref = writer.Position;
            var table = new StringBuilder();
            table.Append("xref\n0 ").Append(objectCount + 1).Append('\n');
            table.Append("0000000000 65535 f \n");
            for (int i = 1; i <= objectCount; i++)
                table.Append(offsets[i].ToString("D10", CultureInfo.InvariantCulture)).Append(" 00000 n \n");
            table.Append("trailer\n<< /Size ").Append(objectCount + 1).Append(" /Root 1 0 R /Info ").Append(infoId).Append(" 0 R >>\n");
            table.Append("startxref\n").Append(xref).Append("\n%%EOF\n");
            writer.Ascii(table.ToString());
            output.Flush();
        }

        private static string Num(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Literal(string text)
        {
            var sb = new StringBuilder("(");
            foreach (char c in text ?? string.Empty)
            {
                if (c == '(' || c == ')' || c == '\\') sb.Append('\\').Append(c);
                else if (c >= 32 && c < 127) sb.Append(c);
                else sb.Append('?');
            }
            return sb.Append(')').ToString();
        }

        private static byte[] Deflate(byte[] raw)
        {
            using (var buffer = new MemoryStream())
            {
                using (var zlib = new ZLibStream(buffer, CompressionLevel.Optimal, true))
                    zlib.Write(raw, 0, raw.Length);
                return buffer.ToArray();
            }
        }

        /// <summary>Tracks the byte offset without needing a seekable stream.</summary>
        private sealed class Counter
        {
            private readonly Stream _stream;
            public long Position;

            public Counter(Stream stream)
            {
                _stream = stream;
            }

            public void Ascii(string text)
            {
                Bytes(Encoding.ASCII.GetBytes(text));
            }

            public void Bytes(byte[] data)
            {
                _stream.Write(data, 0, data.Length);
                Position += data.Length;
            }
        }
    }
}
