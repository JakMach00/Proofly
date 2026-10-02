using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Proofly.Services;

namespace Proofly.Views
{
    /// <summary>
    /// Freezes one display and lets the user drag a rectangle across it, the
    /// way Snipping Tool does. The result is in physical pixels of that display.
    /// </summary>
    public partial class RegionOverlayWindow : Window
    {
        private readonly DisplayInfo _display;
        private readonly BitmapSource _frozen;
        private readonly TaskCompletionSource<Int32Rect?> _result = new TaskCompletionSource<Int32Rect?>();
        private Point? _start;
        private bool _finished;

        private RegionOverlayWindow(DisplayInfo display, BitmapSource frozen)
        {
            InitializeComponent();
            _display = display;
            _frozen = frozen;
            Frozen.Source = frozen;

            // Rough placement. The exact one is done in physical pixels as soon
            // as the window handle exists.
            Left = display.Bounds.X;
            Top = display.Bounds.Y;
            Width = display.Bounds.Width;
            Height = display.Bounds.Height;

            SourceInitialized += delegate { Place(); };
            Loaded += delegate
            {
                Place();
                UpdateDim(null);
                Activate();
                Native.SetForegroundWindow(new WindowInteropHelper(this).Handle);
                Focus();
            };
            ContentRendered += delegate { Place(); };
            SizeChanged += delegate { UpdateDim(null); };
            Closed += delegate { Complete(null); };
        }

        /// <summary>Shows the overlay and completes with the selection, or null when cancelled.</summary>
        public static Task<Int32Rect?> PickAsync(DisplayInfo display, BitmapSource frozen)
        {
            var window = new RegionOverlayWindow(display, frozen);
            window.Show();
            return window._result.Task;
        }

        /// <summary>
        /// WPF sizes windows in scaled units, which do not line up with a
        /// monitor on mixed DPI setups, so the bounds are set through Win32.
        /// </summary>
        private void Place()
        {
            IntPtr handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero) return;
            Native.SetWindowPos(
                handle,
                Native.HwndTopmost,
                _display.Bounds.X,
                _display.Bounds.Y,
                _display.Bounds.Width,
                _display.Bounds.Height,
                Native.SwpNoActivate);
        }

        protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
        {
            base.OnDpiChanged(oldDpi, newDpi);
            Dispatcher.BeginInvoke(new Action(Place));
        }

        /// <summary>Image pixels per layout unit.</summary>
        private double Scale
        {
            get { return Root.ActualWidth > 0 ? _frozen.PixelWidth / Root.ActualWidth : 1.0; }
        }

        private void UpdateDim(Rect? selection)
        {
            var whole = new RectangleGeometry(new Rect(0, 0, Root.ActualWidth, Root.ActualHeight));
            if (selection.HasValue)
                Dim.Data = new CombinedGeometry(GeometryCombineMode.Exclude, whole, new RectangleGeometry(selection.Value));
            else
                Dim.Data = whole;
        }

        private void ShowSelection(Rect box)
        {
            SelectionBox.Margin = new Thickness(box.X, box.Y, 0, 0);
            SelectionBox.Width = box.Width;
            SelectionBox.Height = box.Height;
            double k = Scale;
            SizeText.Text = Math.Round(box.Width * k) + " x " + Math.Round(box.Height * k);
            SizeLabel.Margin = new Thickness(box.X, Math.Max(4, box.Y - 26), 0, 0);
            UpdateDim(box);
        }

        private void ResetSelection()
        {
            _start = null;
            SelectionBox.Visibility = Visibility.Collapsed;
            SizeLabel.Visibility = Visibility.Collapsed;
            HintLabel.Visibility = Visibility.Visible;
            UpdateDim(null);
        }

        protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonDown(e);
            if (_finished) return;
            _start = e.GetPosition(Root);
            CaptureMouse();
            HintLabel.Visibility = Visibility.Collapsed;
            SelectionBox.Visibility = Visibility.Visible;
            SizeLabel.Visibility = Visibility.Visible;
            ShowSelection(new Rect(_start.Value, _start.Value));
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!_start.HasValue) return;
            ShowSelection(new Rect(_start.Value, e.GetPosition(Root)));
        }

        protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
        {
            base.OnMouseLeftButtonUp(e);
            if (!_start.HasValue || _finished) return;
            var box = new Rect(_start.Value, e.GetPosition(Root));
            ReleaseMouseCapture();
            if (box.Width < 4 || box.Height < 4)
            {
                // A plain click is not a selection, start over instead of closing.
                ResetSelection();
                return;
            }
            double k = Scale;
            var raw = new Int32Rect(
                (int)Math.Round(box.X * k),
                (int)Math.Round(box.Y * k),
                (int)Math.Round(box.Width * k),
                (int)Math.Round(box.Height * k));
            Complete(Bitmaps.ClampTo(raw, _frozen.PixelWidth, _frozen.PixelHeight));
        }

        protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
        {
            base.OnMouseRightButtonDown(e);
            Complete(null);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.Escape) Complete(null);
        }

        private void Complete(Int32Rect? rect)
        {
            if (_finished) return;
            _finished = true;
            if (IsMouseCaptured) ReleaseMouseCapture();
            _result.TrySetResult(rect);
            Close();
        }
    }
}
