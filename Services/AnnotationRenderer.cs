using System;
using System.Collections.Generic;
using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Proofly.Models;

namespace Proofly.Services
{
    /// <summary>
    /// Draws annotations. The editor uses it for the live view and for the
    /// final image, so what is saved is exactly what was on screen.
    /// </summary>
    public static class AnnotationRenderer
    {
        private const double LineSpacing = 1.25;

        private static readonly Dictionary<string, Brush> BrushCache = new Dictionary<string, Brush>();
        private static readonly Dictionary<string, Typeface> TypefaceCache = new Dictionary<string, Typeface>();
        private static readonly Brush DarkOutline = Frozen(Color.FromArgb(179, 0, 0, 0));
        private static readonly Brush LightOutline = Frozen(Color.FromArgb(217, 255, 255, 255));

        public static Brush BrushFor(string hex)
        {
            string key = hex ?? "#ff3b30";
            Brush brush;
            if (BrushCache.TryGetValue(key, out brush)) return brush;
            Color color;
            try
            {
                color = (Color)ColorConverter.ConvertFromString(key);
            }
            catch (Exception)
            {
                color = Color.FromRgb(0xFF, 0x3B, 0x30);
            }
            brush = Frozen(color);
            BrushCache[key] = brush;
            return brush;
        }

        /// <summary>
        /// The outline has to contrast with the text: dark around every colour
        /// except the dark one, which gets a white outline instead.
        /// </summary>
        private static Brush OutlineFor(string hex)
        {
            var brush = BrushFor(hex) as SolidColorBrush;
            if (brush == null) return DarkOutline;
            Color c = brush.Color;
            double luminance = (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
            return luminance < 0.3 ? LightOutline : DarkOutline;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static Typeface TypefaceFor(string fontName)
        {
            string key = fontName ?? "Arial";
            Typeface typeface;
            if (TypefaceCache.TryGetValue(key, out typeface)) return typeface;
            typeface = new Typeface(
                new FontFamily(EditorFonts.FamilyFor(key)), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
            TypefaceCache[key] = typeface;
            return typeface;
        }

        private static FormattedText Layout(string text, Typeface typeface, double size, Brush brush)
        {
            // Annotations are measured in image pixels, so the text is laid out
            // at one pixel per unit whatever the monitor scaling is.
            return new FormattedText(
                text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, typeface, Math.Max(1, size), brush, 1.0);
        }

        public static double TextWidth(TextAnnotation text)
        {
            Typeface typeface = TypefaceFor(text.Font);
            double widest = 0;
            foreach (string line in text.Lines)
            {
                FormattedText layout = Layout(line.Length == 0 ? " " : line, typeface, text.Size, Brushes.Black);
                widest = Math.Max(widest, layout.WidthIncludingTrailingWhitespace);
            }
            return widest;
        }

        public static Rect Bounds(Annotation annotation)
        {
            var marker = annotation as MarkerAnnotation;
            if (marker != null)
            {
                if (marker.Points.Count == 0) return Rect.Empty;
                double pad = MarkerAnnotation.StrokeFor(marker.Width) / 2 + 4;
                double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
                foreach (Point p in marker.Points)
                {
                    minX = Math.Min(minX, p.X);
                    minY = Math.Min(minY, p.Y);
                    maxX = Math.Max(maxX, p.X);
                    maxY = Math.Max(maxY, p.Y);
                }
                return new Rect(minX - pad, minY - pad, maxX - minX + pad * 2, maxY - minY + pad * 2);
            }

            var step = annotation as StepAnnotation;
            if (step != null)
                return new Rect(step.X - step.Radius, step.Y - step.Radius, step.Radius * 2, step.Radius * 2);

            var text = annotation as TextAnnotation;
            if (text != null)
                return new Rect(text.X, text.Y, TextWidth(text), text.Lines.Length * text.Size * LineSpacing);

            var shape = (ShapeAnnotation)annotation;
            double margin = shape.Width + 6;
            return new Rect(
                Math.Min(shape.X1, shape.X2) - margin,
                Math.Min(shape.Y1, shape.Y2) - margin,
                Math.Abs(shape.X2 - shape.X1) + margin * 2,
                Math.Abs(shape.Y2 - shape.Y1) + margin * 2);
        }

        public static void Draw(DrawingContext dc, Annotation annotation)
        {
            var step = annotation as StepAnnotation;
            if (step != null)
            {
                DrawStep(dc, step);
                return;
            }
            var marker = annotation as MarkerAnnotation;
            if (marker != null)
            {
                DrawMarker(dc, marker);
                return;
            }
            var text = annotation as TextAnnotation;
            if (text != null)
            {
                DrawText(dc, text);
                return;
            }
            DrawShape(dc, (ShapeAnnotation)annotation);
        }

        private static void DrawStep(DrawingContext dc, StepAnnotation step)
        {
            double r = step.Radius;
            var ring = new Pen(Brushes.White, Math.Max(2, r * 0.12));
            dc.DrawEllipse(BrushFor(step.Color), ring, new Point(step.X, step.Y), r, r);

            FormattedText number = Layout(
                step.Number.ToString(CultureInfo.InvariantCulture), TypefaceFor("Arial"), Math.Round(r * 1.25), Brushes.White);
            dc.DrawText(number, new Point(step.X - number.Width / 2, step.Y + r * 0.05 - number.Height / 2));
        }

        /// <summary>
        /// One smooth stroke through the recorded points. Drawing it as a single
        /// path keeps the transparency even where the stroke crosses itself.
        /// </summary>
        private static void DrawMarker(DrawingContext dc, MarkerAnnotation marker)
        {
            List<Point> points = marker.Points;
            if (points.Count == 0) return;
            Brush brush = BrushFor(marker.Color);
            double stroke = MarkerAnnotation.StrokeFor(marker.Width);

            dc.PushOpacity(0.4);
            if (points.Count == 1)
            {
                dc.DrawEllipse(brush, null, points[0], stroke / 2, stroke / 2);
            }
            else
            {
                var geometry = new StreamGeometry();
                using (StreamGeometryContext context = geometry.Open())
                {
                    context.BeginFigure(points[0], false, false);
                    for (int i = 1; i < points.Count - 1; i++)
                    {
                        var middle = new Point((points[i].X + points[i + 1].X) / 2, (points[i].Y + points[i + 1].Y) / 2);
                        context.QuadraticBezierTo(points[i], middle, true, true);
                    }
                    context.LineTo(points[points.Count - 1], true, true);
                }
                geometry.Freeze();
                var pen = new Pen(brush, stroke)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round,
                    LineJoin = PenLineJoin.Round,
                };
                dc.DrawGeometry(null, pen, geometry);
            }
            dc.Pop();
        }

        private static void DrawText(DrawingContext dc, TextAnnotation text)
        {
            Typeface typeface = TypefaceFor(text.Font);
            Brush brush = BrushFor(text.Color);
            double lineHeight = text.Size * LineSpacing;
            string[] lines = text.Lines;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Length == 0) continue;
                var origin = new Point(text.X, text.Y + i * lineHeight);
                FormattedText layout = Layout(lines[i], typeface, text.Size, brush);
                if (text.Outline)
                {
                    var pen = new Pen(OutlineFor(text.Color), Math.Max(3, text.Size * 0.16)) { LineJoin = PenLineJoin.Round };
                    dc.DrawGeometry(null, pen, layout.BuildGeometry(origin));
                }
                dc.DrawText(layout, origin);
            }
        }

        private static void DrawShape(DrawingContext dc, ShapeAnnotation shape)
        {
            var box = new Rect(
                Math.Min(shape.X1, shape.X2),
                Math.Min(shape.Y1, shape.Y2),
                Math.Abs(shape.X2 - shape.X1),
                Math.Abs(shape.Y2 - shape.Y1));
            Brush brush = BrushFor(shape.Color);

            switch (shape.Kind)
            {
                case ShapeKind.Arrow:
                    DrawArrow(dc, shape, brush);
                    break;
                case ShapeKind.Rect:
                    dc.DrawRectangle(null, new Pen(brush, shape.Width), box);
                    break;
                case ShapeKind.Ellipse:
                    dc.DrawEllipse(
                        null,
                        new Pen(brush, shape.Width),
                        new Point(box.X + box.Width / 2, box.Y + box.Height / 2),
                        box.Width / 2,
                        box.Height / 2);
                    break;
                case ShapeKind.Highlight:
                    dc.PushOpacity(0.32);
                    dc.DrawRectangle(brush, null, box);
                    dc.Pop();
                    break;
                case ShapeKind.Redact:
                    // A solid block, no stroke: this is a cover, not an outline.
                    dc.DrawRectangle(Brushes.Black, null, box);
                    break;
            }
        }

        private static void DrawArrow(DrawingContext dc, ShapeAnnotation a, Brush brush)
        {
            double head = Math.Max(10, a.Width * 4);
            double angle = Math.Atan2(a.Y2 - a.Y1, a.X2 - a.X1);
            double length = Math.Sqrt((a.X2 - a.X1) * (a.X2 - a.X1) + (a.Y2 - a.Y1) * (a.Y2 - a.Y1));
            double bodyEnd = Math.Max(0, length - head * 0.9);
            var end = new Point(a.X1 + Math.Cos(angle) * bodyEnd, a.Y1 + Math.Sin(angle) * bodyEnd);

            var pen = new Pen(brush, a.Width) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
            dc.DrawLine(pen, new Point(a.X1, a.Y1), end);

            var tip = new StreamGeometry();
            using (StreamGeometryContext context = tip.Open())
            {
                context.BeginFigure(new Point(a.X2, a.Y2), true, true);
                context.LineTo(
                    new Point(a.X2 - Math.Cos(angle - Math.PI / 7) * head, a.Y2 - Math.Sin(angle - Math.PI / 7) * head),
                    true, false);
                context.LineTo(
                    new Point(a.X2 - Math.Cos(angle + Math.PI / 7) * head, a.Y2 - Math.Sin(angle + Math.PI / 7) * head),
                    true, false);
            }
            tip.Freeze();
            dc.DrawGeometry(brush, null, tip);
        }

        /// <summary>
        /// The finished image: the given area of the screenshot with every
        /// annotation painted on it, at the original pixel size.
        /// </summary>
        public static BitmapSource Render(BitmapSource image, Int32Rect area, IEnumerable<Annotation> annotations)
        {
            var visual = new DrawingVisual();
            RenderOptions.SetBitmapScalingMode(visual, BitmapScalingMode.NearestNeighbor);
            using (DrawingContext dc = visual.RenderOpen())
            {
                dc.PushTransform(new TranslateTransform(-area.X, -area.Y));
                dc.DrawImage(image, new Rect(0, 0, image.PixelWidth, image.PixelHeight));
                foreach (Annotation annotation in annotations) Draw(dc, annotation);
                dc.Pop();
            }

            var target = new RenderTargetBitmap(area.Width, area.Height, 96, 96, PixelFormats.Pbgra32);
            target.Render(visual);
            target.Freeze();

            var opaque = new FormatConvertedBitmap(target, PixelFormats.Bgr32, null, 0);
            opaque.Freeze();
            return opaque;
        }
    }
}
