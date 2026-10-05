using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Proofly.Services
{
    /// <summary>
    /// Marks mouse clicks while a recording runs. A ring appears around the
    /// pointer when a button goes down, stays for as long as it is held and
    /// fades out after it is released. The ring is a small see-through window
    /// on the screen itself, so the recording simply picks it up.
    ///
    /// The buttons are read by asking Windows for their state a few dozen
    /// times a second. No input hook is installed.
    /// </summary>
    public sealed class ClickHighlighter
    {
        private readonly DispatcherTimer _timer;
        private RingWindow _ring;
        private bool _leftDown;
        private bool _rightDown;

        public ClickHighlighter()
        {
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            _timer.Tick += delegate { Poll(); };
        }

        public void Start()
        {
            if (_ring == null)
            {
                _ring = new RingWindow();
                _ring.Show();
            }
            // A button already held when the recording starts is not a click.
            bool ignored;
            _leftDown = Read(Native.VkLeftButton, out ignored);
            _rightDown = Read(Native.VkRightButton, out ignored);
            _timer.Start();
        }

        /// <summary>Stops watching, for example while the recording is paused.</summary>
        public void Suspend()
        {
            _timer.Stop();
            if (_ring != null) _ring.Clear();
        }

        public void Stop()
        {
            _timer.Stop();
            if (_ring == null) return;
            _ring.Close();
            _ring = null;
        }

        /// <summary>
        /// Whether a button is down now. <paramref name="tapped"/> reports a press
        /// that began and ended between two checks.
        /// </summary>
        private static bool Read(int key, out bool tapped)
        {
            short state = Native.GetAsyncKeyState(key);
            tapped = (state & 0x0001) != 0;
            return (state & 0x8000) != 0;
        }

        private void Poll()
        {
            if (_ring == null) return;
            bool leftTap;
            bool rightTap;
            bool left = Read(Native.VkLeftButton, out leftTap);
            bool right = Read(Native.VkRightButton, out rightTap);
            Native.POINT pointer;
            if (!Native.GetCursorPos(out pointer)) return;

            bool leftPressed = (left || leftTap) && !_leftDown;
            bool rightPressed = (right || rightTap) && !_rightDown;
            if (leftPressed) _ring.Press(pointer, false);
            else if (rightPressed) _ring.Press(pointer, true);
            else if (left || right) _ring.Follow(pointer);

            bool wasHeld = _leftDown || _rightDown || leftPressed || rightPressed;
            if (wasHeld && !left && !right) _ring.Release();

            _leftDown = left;
            _rightDown = right;
        }

        /// <summary>The ring itself: a borderless window that lets every click through.</summary>
        private sealed class RingWindow : Window
        {
            private const double SizeAtNormalScale = 76;

            private static readonly Brush LeftFill = Frozen(Color.FromArgb(90, 0xFF, 0xD4, 0x00));
            private static readonly Brush LeftEdge = Frozen(Color.FromArgb(255, 0xFF, 0xD4, 0x00));
            private static readonly Brush RightFill = Frozen(Color.FromArgb(90, 0xFF, 0x5A, 0x5A));
            private static readonly Brush RightEdge = Frozen(Color.FromArgb(255, 0xFF, 0x5A, 0x5A));

            private readonly System.Windows.Shapes.Ellipse _dot;
            private readonly ScaleTransform _scale = new ScaleTransform(1, 1);
            private IntPtr _handle;

            public RingWindow()
            {
                Title = "Proofly click marker";
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                AllowsTransparency = true;
                Background = Brushes.Transparent;
                ShowInTaskbar = false;
                ShowActivated = false;
                Topmost = true;
                Focusable = false;
                IsHitTestVisible = false;
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = -10000;
                Top = -10000;
                Width = SizeAtNormalScale;
                Height = SizeAtNormalScale;

                _dot = new System.Windows.Shapes.Ellipse
                {
                    Margin = new Thickness(6),
                    StrokeThickness = 4,
                    Opacity = 0,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = _scale,
                };
                Content = _dot;
            }

            private static Brush Frozen(Color color)
            {
                var brush = new SolidColorBrush(color);
                brush.Freeze();
                return brush;
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                _handle = new WindowInteropHelper(this).Handle;
                // Mouse input passes straight through to whatever is underneath,
                // and the window never takes focus or shows up in Alt+Tab.
                long style = Native.GetWindowLongPtr(_handle, Native.GwlExStyle).ToInt64();
                style |= Native.WsExTransparent | Native.WsExToolWindow | Native.WsExNoActivate;
                Native.SetWindowLongPtr(_handle, Native.GwlExStyle, new IntPtr(style));
            }

            public void Press(Native.POINT pointer, bool rightButton)
            {
                _dot.Fill = rightButton ? RightFill : LeftFill;
                _dot.Stroke = rightButton ? RightEdge : LeftEdge;
                MoveTo(pointer);

                // Any fade still running from the click before is dropped.
                _dot.BeginAnimation(OpacityProperty, null);
                _dot.Opacity = 1;

                var pop = new DoubleAnimation(0.55, 1.0, new Duration(TimeSpan.FromMilliseconds(140)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }

            /// <summary>Keeps the ring on the pointer while the button is held.</summary>
            public void Follow(Native.POINT pointer)
            {
                MoveTo(pointer);
            }

            /// <summary>The ring lingers for a moment after the button comes up, then fades away.</summary>
            public void Release()
            {
                var fade = new DoubleAnimation(1.0, 0.0, new Duration(TimeSpan.FromMilliseconds(600)))
                {
                    BeginTime = TimeSpan.FromMilliseconds(150),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseIn },
                };
                _dot.BeginAnimation(OpacityProperty, fade);
            }

            public void Clear()
            {
                _dot.BeginAnimation(OpacityProperty, null);
                _dot.Opacity = 0;
            }

            /// <summary>Centres the window on a screen position given in physical pixels.</summary>
            private void MoveTo(Native.POINT pointer)
            {
                if (_handle == IntPtr.Zero) return;
                double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
                int size = (int)Math.Round(SizeAtNormalScale * (scale > 0 ? scale : 1.0));
                Native.SetWindowPos(
                    _handle, Native.HwndTopmost, pointer.X - size / 2, pointer.Y - size / 2, size, size,
                    Native.SwpNoActivate);
            }
        }
    }
}
