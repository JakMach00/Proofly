using System;
using System.Collections.Generic;
using System.Windows;

namespace Proofly.Models
{
    public enum ShapeKind
    {
        Arrow,
        Rect,
        Ellipse,
        Highlight,
        Redact,
    }

    /// <summary>
    /// Base of everything drawn on a screenshot. Coordinates are always in the
    /// pixels of the original image, so a crop never has to move anything.
    /// </summary>
    public abstract class Annotation
    {
        protected Annotation()
        {
            Id = Guid.NewGuid().ToString("N");
        }

        public string Id { get; set; }

        /// <summary>Hex colour such as #ff3b30.</summary>
        public string Color { get; set; }

        public double Width { get; set; }

        /// <summary>
        /// A copy with the same id. Undo snapshots share annotation objects, so
        /// an object that a snapshot may hold is copied before it is changed.
        /// </summary>
        public abstract Annotation Clone();

        public abstract void Offset(double dx, double dy);

        protected T CopyBase<T>(T target) where T : Annotation
        {
            target.Id = Id;
            target.Color = Color;
            target.Width = Width;
            return target;
        }
    }

    public sealed class ShapeAnnotation : Annotation
    {
        public ShapeKind Kind { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        /// <summary>Filled blocks have no outline, so thickness means nothing for them.</summary>
        public bool IsSolid
        {
            get { return Kind == ShapeKind.Highlight || Kind == ShapeKind.Redact; }
        }

        public override Annotation Clone()
        {
            return CopyBase(new ShapeAnnotation { Kind = Kind, X1 = X1, Y1 = Y1, X2 = X2, Y2 = Y2 });
        }

        public override void Offset(double dx, double dy)
        {
            X1 += dx;
            Y1 += dy;
            X2 += dx;
            Y2 += dy;
        }
    }

    public sealed class StepAnnotation : Annotation
    {
        public double X { get; set; }
        public double Y { get; set; }
        public int Number { get; set; }
        public double Radius { get; set; }

        public static double RadiusFor(double width)
        {
            return Math.Round(width * 4 + 12);
        }

        public override Annotation Clone()
        {
            return CopyBase(new StepAnnotation { X = X, Y = Y, Number = Number, Radius = Radius });
        }

        public override void Offset(double dx, double dy)
        {
            X += dx;
            Y += dy;
        }
    }

    public sealed class TextAnnotation : Annotation
    {
        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>May contain line breaks.</summary>
        public string Text { get; set; }

        public double Size { get; set; }

        /// <summary>One of the names in <see cref="EditorFonts"/>.</summary>
        public string Font { get; set; }

        public bool Outline { get; set; }

        public string[] Lines
        {
            get
            {
                string text = string.IsNullOrEmpty(Text) ? " " : Text;
                return text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
            }
        }

        public override Annotation Clone()
        {
            return CopyBase(new TextAnnotation { X = X, Y = Y, Text = Text, Size = Size, Font = Font, Outline = Outline });
        }

        public override void Offset(double dx, double dy)
        {
            X += dx;
            Y += dy;
        }
    }

    /// <summary>A freehand stroke that follows the pointer, drawn like a highlighter pen.</summary>
    public sealed class MarkerAnnotation : Annotation
    {
        public MarkerAnnotation()
        {
            Points = new List<Point>();
        }

        public List<Point> Points { get; set; }

        /// <summary>Marker strokes are much wider than outlines at the same thickness setting.</summary>
        public static double StrokeFor(double width)
        {
            return width * 3 + 6;
        }

        public override Annotation Clone()
        {
            return CopyBase(new MarkerAnnotation { Points = new List<Point>(Points) });
        }

        public override void Offset(double dx, double dy)
        {
            for (int i = 0; i < Points.Count; i++)
                Points[i] = new Point(Points[i].X + dx, Points[i].Y + dy);
        }
    }

    public static class EditorFonts
    {
        public static readonly string[] Names = { "Arial", "Segoe UI", "Georgia", "Monospace", "Impact" };

        /// <summary>Font family list WPF resolves, with fallbacks for missing fonts.</summary>
        public static string FamilyFor(string name)
        {
            switch (name)
            {
                case "Segoe UI":
                    return "Segoe UI, Arial";
                case "Georgia":
                    return "Georgia, Times New Roman";
                case "Monospace":
                    return "Cascadia Mono, Consolas, Courier New";
                case "Impact":
                    return "Impact, Arial Black";
                default:
                    return "Arial, Segoe UI";
            }
        }
    }
}
