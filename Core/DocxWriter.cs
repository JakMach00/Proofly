using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Proofly.Core
{
    /// <summary>One screenshot for a Word document: a complete PNG or JPEG file.</summary>
    public sealed class DocxImage
    {
        public int Width;
        public int Height;
        public byte[] Data;
        public bool IsJpeg;
    }

    /// <summary>
    /// Writes a Word document with one A4 page per image and nothing else on
    /// it, each page turned to suit its image. A .docx file is a zip archive of
    /// a few XML parts, which is simple enough to write without a library.
    /// </summary>
    public static class DocxWriter
    {
        // Page size in points, margins as in the PDF export.
        private const double A4Short = 595.3;
        private const double A4Long = 841.9;
        private const double Margin = 36;

        // Word needs room for the paragraph the picture sits in. Without this
        // allowance a full height picture is pushed onto a page of its own.
        private const double ParagraphAllowance = 24;

        private const double EmuPerPoint = 12700;
        private const double TwipsPerPoint = 20;

        public static void Write(Stream output, int imageCount, Func<int, DocxImage> getImage, string title)
        {
            if (imageCount <= 0) throw new InvalidOperationException("There are no screenshots to put in the document.");

            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, true))
            {
                // The content type list goes first, which is where readers expect it.
                Add(zip, "[Content_Types].xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
                    "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
                    "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
                    "<Default Extension=\"png\" ContentType=\"image/png\"/>" +
                    "<Default Extension=\"jpeg\" ContentType=\"image/jpeg\"/>" +
                    "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
                    "<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>" +
                    "</Types>");

                var body = new StringBuilder();
                var relations = new StringBuilder();

                for (int i = 0; i < imageCount; i++)
                {
                    DocxImage image = getImage(i);
                    if (image == null || image.Data == null || image.Width <= 0 || image.Height <= 0)
                        throw new InvalidOperationException("A screenshot could not be read.");

                    int number = i + 1;
                    string extension = image.IsJpeg ? "jpeg" : "png";
                    string fileName = "image" + number.ToString(CultureInfo.InvariantCulture) + "." + extension;

                    // Images are already compressed, so they are stored as they are.
                    ZipArchiveEntry media = zip.CreateEntry("word/media/" + fileName, CompressionLevel.NoCompression);
                    using (Stream stream = media.Open()) stream.Write(image.Data, 0, image.Data.Length);

                    string relationId = "rId" + number.ToString(CultureInfo.InvariantCulture);
                    relations.Append("<Relationship Id=\"").Append(relationId)
                        .Append("\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"media/")
                        .Append(fileName).Append("\"/>");

                    bool landscape = image.Width >= image.Height;
                    double pageW = landscape ? A4Long : A4Short;
                    double pageH = landscape ? A4Short : A4Long;
                    double availW = pageW - Margin * 2;
                    double availH = pageH - Margin * 2 - ParagraphAllowance;
                    double ratio = Math.Min(availW / image.Width, availH / image.Height);
                    long cx = (long)Math.Round(image.Width * ratio * EmuPerPoint);
                    long cy = (long)Math.Round(image.Height * ratio * EmuPerPoint);

                    string section = SectionProperties(pageW, pageH, landscape);
                    bool last = i == imageCount - 1;

                    // Every section but the last is closed by the paragraph that
                    // ends it. The last one is closed at the end of the body.
                    body.Append("<w:p><w:pPr><w:spacing w:before=\"0\" w:after=\"0\"/><w:jc w:val=\"center\"/>");
                    if (!last) body.Append(section);
                    body.Append("</w:pPr><w:r>");
                    body.Append(Drawing(number, relationId, fileName, cx, cy));
                    body.Append("</w:r></w:p>");
                    if (last) body.Append(section);
                }

                Add(zip, "_rels/.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>" +
                    "</Relationships>");

                Add(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\">" +
                    "<dc:title>" + Escape(title) + "</dc:title><dc:creator>Proofly</dc:creator>" +
                    "</cp:coreProperties>");

                Add(zip, "word/_rels/document.xml.rels",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                    relations + "</Relationships>");

                Add(zip, "word/document.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
                    "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
                    "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                    "xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                    "<w:body>" + body + "</w:body></w:document>");
            }
            output.Flush();
        }

        private static string SectionProperties(double pageW, double pageH, bool landscape)
        {
            string margin = Twips(Margin);
            return "<w:sectPr><w:pgSz w:w=\"" + Twips(pageW) + "\" w:h=\"" + Twips(pageH) + "\"" +
                   (landscape ? " w:orient=\"landscape\"" : "") + "/>" +
                   "<w:pgMar w:top=\"" + margin + "\" w:right=\"" + margin + "\" w:bottom=\"" + margin +
                   "\" w:left=\"" + margin + "\" w:header=\"0\" w:footer=\"0\" w:gutter=\"0\"/></w:sectPr>";
        }

        private static string Drawing(int number, string relationId, string fileName, long cx, long cy)
        {
            string id = number.ToString(CultureInfo.InvariantCulture);
            string size = "cx=\"" + cx.ToString(CultureInfo.InvariantCulture) + "\" cy=\"" +
                          cy.ToString(CultureInfo.InvariantCulture) + "\"";
            return "<w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">" +
                   "<wp:extent " + size + "/>" +
                   "<wp:docPr id=\"" + id + "\" name=\"Picture " + id + "\"/>" +
                   "<wp:cNvGraphicFramePr><a:graphicFrameLocks noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
                   "<a:graphic><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
                   "<pic:pic><pic:nvPicPr><pic:cNvPr id=\"" + id + "\" name=\"" + fileName + "\"/><pic:cNvPicPr/></pic:nvPicPr>" +
                   "<pic:blipFill><a:blip r:embed=\"" + relationId + "\"/><a:stretch><a:fillRect/></a:stretch></pic:blipFill>" +
                   "<pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext " + size + "/></a:xfrm>" +
                   "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic>" +
                   "</a:graphicData></a:graphic></wp:inline></w:drawing>";
        }

        private static string Twips(double points)
        {
            return ((long)Math.Round(points * TwipsPerPoint)).ToString(CultureInfo.InvariantCulture);
        }

        private static string Escape(string text)
        {
            return (text ?? string.Empty).Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");
        }

        private static void Add(ZipArchive zip, string name, string content)
        {
            ZipArchiveEntry entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (Stream stream = entry.Open())
            {
                byte[] bytes = new UTF8Encoding(false).GetBytes(content);
                stream.Write(bytes, 0, bytes.Length);
            }
        }
    }
}
