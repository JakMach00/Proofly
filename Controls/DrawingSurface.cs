using System;
using System.Windows;
using System.Windows.Media;

namespace Proofly.Controls
{
    /// <summary>
    /// An element that paints whatever its owner tells it to. The editor draws
    /// the screenshot and its annotations through this.
    /// </summary>
    public sealed class DrawingSurface : FrameworkElement
    {
        /// <summary>Called on every repaint with the drawing context to use.</summary>
        public Action<DrawingContext> Painter { get; set; }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);
            // A transparent fill makes the whole area receive mouse input, not
            // only the pixels something was drawn on.
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            Action<DrawingContext> painter = Painter;
            if (painter != null) painter(drawingContext);
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            InvalidateVisual();
        }
    }
}
