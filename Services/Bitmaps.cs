using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Proofly.Services
{
    /// <summary>Screen grabbing and the bitmap conversions the rest of the app needs.</summary>
    public static class Bitmaps
    {
        /// <summary>
        /// Copies a rectangle of the desktop at native resolution. Coordinates
        /// are physical pixels on the virtual screen.
        /// </summary>
        public static BitmapSource CaptureScreen(Int32Rect area)
        {
            return CaptureScreen(area, false);
        }

        /// <summary>
        /// The same, optionally with the mouse pointer drawn in. Windows keeps
        /// the pointer out of screen copies, so it is painted on afterwards.
        /// </summary>
        public static BitmapSource CaptureScreen(Int32Rect area, bool includeCursor)
        {
            if (area.Width <= 0 || area.Height <= 0) throw new InvalidOperationException("No screen found to capture.");

            IntPtr screenDc = Native.GetDC(IntPtr.Zero);
            if (screenDc == IntPtr.Zero) throw new InvalidOperationException("The screen could not be read.");
            IntPtr memoryDc = IntPtr.Zero;
            IntPtr bitmap = IntPtr.Zero;
            IntPtr previous = IntPtr.Zero;
            try
            {
                memoryDc = Native.CreateCompatibleDC(screenDc);
                var header = new Native.BITMAPINFOHEADER();
                header.biSize = (uint)Marshal.SizeOf(typeof(Native.BITMAPINFOHEADER));
                header.biWidth = area.Width;
                // A negative height asks for rows from top to bottom.
                header.biHeight = -area.Height;
                header.biPlanes = 1;
                header.biBitCount = 32;
                header.biCompression = 0;

                IntPtr bits;
                bitmap = Native.CreateDIBSection(memoryDc, ref header, 0, out bits, IntPtr.Zero, 0);
                if (bitmap == IntPtr.Zero || bits == IntPtr.Zero)
                    throw new InvalidOperationException("Not enough memory for the screenshot.");
                previous = Native.SelectObject(memoryDc, bitmap);

                // CaptureBlt includes layered windows such as tooltips and menus.
                if (!Native.BitBlt(memoryDc, 0, 0, area.Width, area.Height, screenDc, area.X, area.Y,
                        Native.SrcCopy | Native.CaptureBlt))
                    throw new InvalidOperationException("Windows refused to copy the screen.");

                if (includeCursor) DrawCursor(memoryDc, area);

                int stride = area.Width * 4;
                BitmapSource result = BitmapSource.Create(
                    area.Width, area.Height, 96, 96, PixelFormats.Bgr32, null, bits, stride * area.Height, stride);
                result.Freeze();
                return result;
            }
            finally
            {
                if (previous != IntPtr.Zero) Native.SelectObject(memoryDc, previous);
                if (bitmap != IntPtr.Zero) Native.DeleteObject(bitmap);
                if (memoryDc != IntPtr.Zero) Native.DeleteDC(memoryDc);
                Native.ReleaseDC(IntPtr.Zero, screenDc);
            }
        }

        private static void DrawCursor(IntPtr targetDc, Int32Rect area)
        {
            var info = new Native.CURSORINFO();
            info.cbSize = Marshal.SizeOf(typeof(Native.CURSORINFO));
            if (!Native.GetCursorInfo(ref info)) return;
            if ((info.flags & Native.CursorShowing) == 0 || info.hCursor == IntPtr.Zero) return;

            // The position Windows reports is the hot spot, not the corner of
            // the pointer image, so the offset between the two is taken off.
            int hotX = 0;
            int hotY = 0;
            Native.ICONINFO icon;
            if (Native.GetIconInfo(info.hCursor, out icon))
            {
                hotX = icon.xHotspot;
                hotY = icon.yHotspot;
                if (icon.hbmMask != IntPtr.Zero) Native.DeleteObject(icon.hbmMask);
                if (icon.hbmColor != IntPtr.Zero) Native.DeleteObject(icon.hbmColor);
            }

            Native.DrawIconEx(
                targetDc,
                info.ptScreenPos.X - area.X - hotX,
                info.ptScreenPos.Y - area.Y - hotY,
                info.hCursor, 0, 0, 0, IntPtr.Zero, Native.DiNormal);
        }

        public static byte[] EncodePng(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var buffer = new MemoryStream())
            {
                encoder.Save(buffer);
                return buffer.ToArray();
            }
        }

        public static byte[] EncodeJpeg(BitmapSource source, int quality)
        {
            var encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = quality;
            encoder.Frames.Add(BitmapFrame.Create(Opaque(source)));
            using (var buffer = new MemoryStream())
            {
                encoder.Save(buffer);
                return buffer.ToArray();
            }
        }

        /// <summary>Decodes an image fully into memory, so the stream can be released.</summary>
        public static BitmapSource Decode(byte[] data)
        {
            using (var buffer = new MemoryStream(data))
            {
                var image = new BitmapImage();
                image.BeginInit();
                image.CacheOption = BitmapCacheOption.OnLoad;
                image.CreateOptions = BitmapCreateOptions.PreservePixelFormat;
                image.StreamSource = buffer;
                image.EndInit();
                image.Freeze();
                return image;
            }
        }

        /// <summary>Raw 24 bit RGB rows from top to bottom, the layout the PDF writer takes.</summary>
        public static byte[] ToRgb24(BitmapSource source)
        {
            BitmapSource converted = new FormatConvertedBitmap(Opaque(source), PixelFormats.Rgb24, null, 0);
            int stride = converted.PixelWidth * 3;
            var pixels = new byte[stride * converted.PixelHeight];
            converted.CopyPixels(pixels, stride, 0);
            return pixels;
        }

        /// <summary>Drops the alpha channel, which JPEG and the PDF image format cannot carry.</summary>
        public static BitmapSource Opaque(BitmapSource source)
        {
            if (source.Format == PixelFormats.Bgr32 || source.Format == PixelFormats.Bgr24 ||
                source.Format == PixelFormats.Rgb24)
                return source;
            return new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
        }

        public static BitmapSource Crop(BitmapSource source, Int32Rect area)
        {
            return Materialize(new CroppedBitmap(source, area));
        }

        /// <summary>Small preview for the gallery.</summary>
        public static BitmapSource MakeThumb(BitmapSource source, int maxWidth = 320)
        {
            double scale = Math.Min(1.0, maxWidth / (double)Math.Max(1, source.PixelWidth));
            if (scale >= 1.0) return Materialize(source);
            return Materialize(new TransformedBitmap(source, new ScaleTransform(scale, scale)));
        }

        /// <summary>
        /// Cropped and scaled bitmaps are views that keep their full size source
        /// alive. Copying the pixels out lets the large image be collected.
        /// </summary>
        private static BitmapSource Materialize(BitmapSource view)
        {
            var copy = new WriteableBitmap(view);
            copy.Freeze();
            return copy;
        }

        /// <summary>Writes the small preview kept next to a screenshot or recording.</summary>
        public static void SaveThumb(BitmapSource thumb, string path)
        {
            File.WriteAllBytes(path, EncodeJpeg(thumb, 80));
        }

        /// <summary>Reads a stored preview, or returns null when it is missing or unreadable.</summary>
        public static BitmapSource LoadThumb(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
                return Decode(File.ReadAllBytes(path));
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// The image on the clipboard as an opaque bitmap, or null when there is
        /// none. Some programs put images there with an empty alpha channel,
        /// which would paste as a blank picture if it were kept.
        /// </summary>
        public static BitmapSource FromClipboard()
        {
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    if (!Clipboard.ContainsImage()) return null;
                    BitmapSource image = Clipboard.GetImage();
                    if (image == null) return null;
                    return Materialize(new FormatConvertedBitmap(image, PixelFormats.Bgr32, null, 0));
                }
                catch (COMException)
                {
                    Thread.Sleep(40);
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return null;
        }

        /// <summary>Image files copied in Explorer, as full paths.</summary>
        public static List<string> ClipboardImageFiles()
        {
            var files = new List<string>();
            try
            {
                if (!Clipboard.ContainsFileDropList()) return files;
                foreach (string path in Clipboard.GetFileDropList())
                {
                    string extension = Path.GetExtension(path).ToLowerInvariant();
                    if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp")
                        files.Add(path);
                }
            }
            catch (Exception)
            {
                // The clipboard is held by another program. Nothing to paste.
            }
            return files;
        }

        /// <summary>Clamps a rectangle to the inside of an image.</summary>
        public static Int32Rect ClampTo(Int32Rect rect, int width, int height)
        {
            int x = Math.Max(0, Math.Min(width - 1, rect.X));
            int y = Math.Max(0, Math.Min(height - 1, rect.Y));
            int w = Math.Max(1, Math.Min(width - x, rect.Width));
            int h = Math.Max(1, Math.Min(height - y, rect.Height));
            return new Int32Rect(x, y, w, h);
        }

        /// <summary>
        /// Puts an image on the clipboard. Another program can hold the
        /// clipboard open for a moment, so a few attempts are made.
        /// </summary>
        public static bool CopyToClipboard(BitmapSource image)
        {
            BitmapSource opaque = Opaque(image);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try
                {
                    Clipboard.SetImage(opaque);
                    return true;
                }
                catch (COMException)
                {
                    Thread.Sleep(40);
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return false;
        }
    }
}
