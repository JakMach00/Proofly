using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Proofly.Controls
{
    /// <summary>
    /// Lays cards out in as many equal columns as fit, each at least
    /// <see cref="MinItemWidth"/> wide, and lets every row keep the height of
    /// its content. When the order changes, cards slide to their new places
    /// instead of jumping, so a reorder is easy to follow.
    /// </summary>
    public sealed class AutoGridPanel : Panel
    {
        private readonly Dictionary<UIElement, Point> _placed = new Dictionary<UIElement, Point>();
        private Size _lastSize;

        public double MinItemWidth { get; set; } = 196;

        public double Gap { get; set; } = 12;

        /// <summary>How long a card takes to slide to a new place.</summary>
        public int SlideMilliseconds { get; set; } = 260;

        private int Columns(double width)
        {
            return Math.Max(1, (int)Math.Floor((width + Gap) / (MinItemWidth + Gap)));
        }

        protected override Size MeasureOverride(Size availableSize)
        {
            double width = double.IsInfinity(availableSize.Width) ? MinItemWidth : availableSize.Width;
            int columns = Columns(width);
            double itemWidth = Math.Max(0, (width - Gap * (columns - 1)) / columns);

            double total = 0;
            double rowHeight = 0;
            int column = 0;
            int rows = 0;
            foreach (UIElement child in InternalChildren)
            {
                child.Measure(new Size(itemWidth, double.PositiveInfinity));
                rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
                column++;
                if (column == columns)
                {
                    total += rowHeight;
                    rows++;
                    rowHeight = 0;
                    column = 0;
                }
            }
            if (column > 0)
            {
                total += rowHeight;
                rows++;
            }
            if (rows > 1) total += Gap * (rows - 1);
            return new Size(width, total);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            int columns = Columns(finalSize.Width);
            double itemWidth = Math.Max(0, (finalSize.Width - Gap * (columns - 1)) / columns);
            int count = InternalChildren.Count;

            // Sliding is for reordering. While the window is being resized the
            // cards would otherwise chase their places on every frame.
            bool slide = SlideMilliseconds > 0 && Math.Abs(finalSize.Width - _lastSize.Width) < 0.5;
            _lastSize = finalSize;
            var present = new HashSet<UIElement>();

            double y = 0;
            for (int start = 0; start < count; start += columns)
            {
                int end = Math.Min(count, start + columns);
                double rowHeight = 0;
                for (int i = start; i < end; i++)
                    rowHeight = Math.Max(rowHeight, InternalChildren[i].DesiredSize.Height);
                for (int i = start; i < end; i++)
                {
                    UIElement child = InternalChildren[i];
                    var place = new Point((i - start) * (itemWidth + Gap), y);
                    child.Arrange(new Rect(place.X, place.Y, itemWidth, rowHeight));
                    present.Add(child);

                    Point before;
                    if (slide && _placed.TryGetValue(child, out before) &&
                        (Math.Abs(before.X - place.X) > 0.5 || Math.Abs(before.Y - place.Y) > 0.5))
                        Slide(child, before.X - place.X, before.Y - place.Y);
                    _placed[child] = place;
                }
                y += rowHeight + Gap;
            }

            foreach (UIElement gone in _placed.Keys.Where(k => !present.Contains(k)).ToList()) _placed.Remove(gone);
            return finalSize;
        }

        /// <summary>
        /// The card is already in its new place. It is shifted back to where it
        /// came from and the shift is animated away.
        /// </summary>
        private void Slide(UIElement child, double fromX, double fromY)
        {
            var shift = new TranslateTransform(fromX, fromY);
            child.RenderTransform = shift;
            var duration = new Duration(TimeSpan.FromMilliseconds(SlideMilliseconds));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            shift.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(fromX, 0, duration) { EasingFunction = ease });
            shift.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(fromY, 0, duration) { EasingFunction = ease });
        }
    }
}
