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

        /// <summary>Text printed under the image. Null leaves the page to the image alone.</summary>
        public PageCaption Caption;
    }

    /// <summary>
    /// Writes a document with one A4 page per image, with an optional caption
    /// under it.
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
            // before the pages: 1 catalog, 2 page tree, three per page, then the
            // two caption fonts and the document information.
            int regularFontId = 2 + pageCount * 3 + 1;
            int boldFontId = regularFontId + 1;
            int objectCount = boldFontId + 1;
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

                // The caption takes its room from the bottom of the page first,
                // the image is fitted into what is left.
                PageCaption caption = image.Caption;
                double reserved = CaptionText.ReservedHeight(caption, availW);
                double ratio = Math.Min(availW / image.Width, (availH - reserved) / image.Height);
                double drawW = image.Width * ratio;
                double drawH = image.Height * ratio;

                double textWidth = CaptionText.TextWidth(drawW, availW);
                List<string> noteLines = caption == null ? new List<string>() : CaptionText.Wrap(caption.Note, textWidth);
                double captionH = caption == null ? 0 : CaptionText.Gap + (1 + noteLines.Count) * CaptionText.LineHeight;

                // Image and caption are centred on the page as one block.
                double x = Margin + (availW - drawW) / 2;
                double y = Margin + (availH - drawH - captionH) / 2 + captionH;

                offsets[pageId] = writer.Position;
                string fonts = caption == null
                    ? ""
                    : " /Font << /F1 " + regularFontId + " 0 R /F2 " + boldFontId + " 0 R >>";
                writer.Ascii(
                    pageId + " 0 obj\n<< /Type /Page /Parent 2 0 R /MediaBox [0 0 " + Num(pageW) + " " + Num(pageH) + "]" +
                    " /Resources << /XObject << /Im0 " + imageId + " 0 R >>" + fonts + " >> /Contents " + contentId + " 0 R >>\nendobj\n");

                string content = "q " + Num(drawW) + " 0 0 " + Num(drawH) + " " + Num(x) + " " + Num(y) + " cm /Im0 Do Q\n";
                if (caption != null)
                    content += CaptionContent(caption, noteLines, Margin + (availW - textWidth) / 2, y - CaptionText.Gap);
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

            // Helvetica is one of the fonts every PDF reader has built in, so
            // nothing is embedded. The encoding adds the Polish letters.
            string encoding = "<< /Type /Encoding /BaseEncoding /WinAnsiEncoding /Differences [ " + CaptionText.PdfDifferences + " ] >>";
            offsets[regularFontId] = writer.Position;
            writer.Ascii(regularFontId + " 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding " + encoding + " >>\nendobj\n");
            offsets[boldFontId] = writer.Position;
            writer.Ascii(boldFontId + " 0 obj\n<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold /Encoding " + encoding + " >>\nendobj\n");

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

        /// <summary>
        /// Drawing commands for a caption whose first line starts at the given
        /// top edge: the heading in bold, the detail after it in grey, then the
        /// note lines.
        /// </summary>
        private static string CaptionContent(PageCaption caption, List<string> noteLines, double left, double top)
        {
            double size = CaptionText.FontSize;
            // The first baseline sits one ascent below the top edge.
            double baseline = top - size * 0.8;
            var sb = new StringBuilder();

            sb.Append("BT /F2 ").Append(Num(size)).Append(" Tf 0 g ").Append(Num(left)).Append(' ').Append(Num(baseline))
              .Append(" Td ").Append(Text(caption.Heading)).Append(" Tj ET\n");
            if (!string.IsNullOrEmpty(caption.Detail))
            {
                double gap = CaptionText.Measure(caption.Heading + "   ", true, size);
                sb.Append("BT /F1 ").Append(Num(size)).Append(" Tf 0.42 g ").Append(Num(left + gap)).Append(' ')
                  .Append(Num(baseline)).Append(" Td ").Append(Text(caption.Detail)).Append(" Tj ET\n");
            }
            for (int i = 0; i < noteLines.Count; i++)
            {
                double lineY = baseline - (i + 1) * CaptionText.LineHeight;
                sb.Append("BT /F1 ").Append(Num(size)).Append(" Tf 0.12 g ").Append(Num(left)).Append(' ').Append(Num(lineY))
                  .Append(" Td ").Append(Text(noteLines[i])).Append(" Tj ET\n");
            }
            return sb.ToString();
        }

        /// <summary>A string in the caption font encoding, with every byte outside plain ASCII written as an octal escape.</summary>
        private static string Text(string value)
        {
            var sb = new StringBuilder("(");
            foreach (byte b in CaptionText.PdfBytes(value))
            {
                if (b == (byte)'(' || b == (byte)')' || b == (byte)'\\') sb.Append('\\').Append((char)b);
                else if (b >= 32 && b < 127) sb.Append((char)b);
                else sb.Append('\\').Append(Convert.ToString(b, 8).PadLeft(3, '0'));
            }
            return sb.Append(')').ToString();
        }

        private static string Num(double value)
        {
            return value.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>A text string for the document information. Anything beyond ASCII is written as UTF-16.</summary>
        private static string Literal(string text)
        {
            string value = text ?? string.Empty;
            bool ascii = true;
            foreach (char c in value) if (c < 32 || c >= 127) ascii = false;
            if (!ascii)
            {
                var hex = new StringBuilder("<FEFF");
                foreach (char c in value) hex.Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
                return hex.Append('>').ToString();
            }
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
