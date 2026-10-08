using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace Proofly.Services
{
    /// <summary>
    /// Shows on the screen that a recording is running: a small badge with the
    /// elapsed time in the corner of the recorded area, and a large symbol in
    /// its middle for a moment whenever the recording starts, pauses or
    /// resumes.
    ///
    /// Both are windows that Windows leaves out of every capture, so they never
    /// appear in the recording or in a screenshot taken meanwhile. Windows
    /// older than Windows 10 version 2004 cannot do that, and there nothing is
    /// shown at all rather than something that would end up in the video.
    /// </summary>
    public sealed class RecordingIndicator
    {
        public enum Symbol
        {
            Record,
            Pause,
            Play,
        }

        private BadgeWindow _badge;
        private FlashWindow _flash;

        public static bool IsSupported
        {
            get { return Environment.OSVersion.Version >= new Version(10, 0, 19041); }
        }

        /// <summary>
        /// Puts the indicator over an area given in physical screen pixels.
        /// Returns false when it cannot be kept out of the recording, in which
        /// case nothing is shown.
        /// </summary>
        public bool Show(Int32Rect area)
        {
            Close();
            if (!IsSupported) return false;

            _badge = new BadgeWindow();
            _flash = new FlashWindow();
            // Both start far off screen, so nothing is visible before the
            // exclusion is known to work.
            _badge.Show();
            _flash.Show();
            if (!_badge.Excluded || !_flash.Excluded)
            {
                Close();
                return false;
            }

            double scale = ScaleAt(area);
            _badge.Place(area, scale);
            _flash.Place(area, scale);
            return true;
        }

        /// <summary>Updates the badge. Called by the recording clock.</summary>
        public void Update(string elapsed, bool paused)
        {
            if (_badge != null) _badge.SetState(elapsed, paused);
        }

        /// <summary>Shows a large symbol in the middle of the area, which then fades away.</summary>
        public void Flash(Symbol symbol)
        {
            if (_flash != null) _flash.Play(symbol);
        }

        public void Close()
        {
            if (_badge != null) _badge.Close();
            if (_flash != null) _flash.Close();
            _badge = null;
            _flash = null;
        }

        /// <summary>The display scale where the area is, 1.0 for 100%.</summary>
        private static double ScaleAt(Int32Rect area)
        {
            try
            {
                var centre = new Native.POINT { X = area.X + area.Width / 2, Y = area.Y + area.Height / 2 };
                IntPtr monitor = Native.MonitorFromPoint(centre, Native.MonitorDefaultToNearest);
                uint dpiX;
                uint dpiY;
                if (monitor != IntPtr.Zero && Native.GetDpiForMonitor(monitor, Native.MdtEffectiveDpi, out dpiX, out dpiY) == 0 && dpiX > 0)
                    return dpiX / 96.0;
            }
            catch (Exception)
            {
                // Falls back to 100%, which only makes the indicator smaller.
            }
            return 1.0;
        }

        private static Brush Frozen(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static readonly Brush Plate = Frozen(Color.FromArgb(185, 0x14, 0x14, 0x16));
        private static readonly Brush RecordRed = Frozen(Color.FromArgb(255, 0xFF, 0x3B, 0x30));
        private static readonly Brush PauseAmber = Frozen(Color.FromArgb(255, 0xFF, 0xB0, 0x20));
        private static readonly Brush Light = Frozen(Color.FromArgb(255, 0xF2, 0xF2, 0xF2));

        /// <summary>
        /// A borderless see-through window that lets every click through, never
        /// takes focus and is excluded from screen capture.
        /// </summary>
        private abstract class OverlayWindow : Window
        {
            protected IntPtr Handle;

            /// <summary>Whether Windows accepted to leave this window out of captures.</summary>
            public bool Excluded { get; private set; }

            protected OverlayWindow(string title)
            {
                Title = title;
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
            }

            protected override void OnSourceInitialized(EventArgs e)
            {
                base.OnSourceInitialized(e);
                Handle = new WindowInteropHelper(this).Handle;
                long style = Native.GetWindowLongPtr(Handle, Native.GwlExStyle).ToInt64();
                style |= Native.WsExTransparent | Native.WsExToolWindow | Native.WsExNoActivate;
                Native.SetWindowLongPtr(Handle, Native.GwlExStyle, new IntPtr(style));
                Excluded = Native.SetWindowDisplayAffinity(Handle, Native.WdaExcludeFromCapture);
            }

            /// <summary>
            /// Moves the window to a rectangle in physical pixels. The first move
            /// may cross onto a display with a different scale, and WPF then
            /// resizes the window on its own, so the size is set once more.
            /// </summary>
            protected void MoveTo(int x, int y, int width, int height)
            {
                if (Handle == IntPtr.Zero) return;
                Native.SetWindowPos(Handle, Native.HwndTopmost, x, y, width, height, Native.SwpNoActivate);
                Native.SetWindowPos(Handle, Native.HwndTopmost, x, y, width, height, Native.SwpNoActivate);
            }
        }

        /// <summary>"REC 01:23" with a red dot, or "PAUSED 01:23" with a pause sign.</summary>
        private sealed class BadgeWindow : OverlayWindow
        {
            private const double WidthDip = 172;
            private const double HeightDip = 34;
            private const double MarginDip = 14;

            private readonly Ellipse _dot;
            private readonly StackPanel _pauseSign;
            private readonly TextBlock _text;
            private bool _paused;

            public BadgeWindow() : base("Proofly recording indicator")
            {
                Width = WidthDip;
                Height = HeightDip;

                _dot = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Fill = RecordRed,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                _pauseSign = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    VerticalAlignment = VerticalAlignment.Center,
                    Visibility = Visibility.Collapsed,
                };
                _pauseSign.Children.Add(new Rectangle { Width = 3, Height = 10, Fill = PauseAmber, Margin = new Thickness(0, 0, 3, 0) });
                _pauseSign.Children.Add(new Rectangle { Width = 3, Height = 10, Fill = PauseAmber });

                _text = new TextBlock
                {
                    Margin = new Thickness(8, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Foreground = Light,
                    FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New"),
                    FontSize = 13,
                    Text = "REC 00:00",
                };

                var row = new StackPanel { Orientation = Orientation.Horizontal };
                row.Children.Add(_dot);
                row.Children.Add(_pauseSign);
                row.Children.Add(_text);

                // Anchored to the right, so a longer time grows to the left and
                // the empty part of the window stays see-through.
                Content = new Border
                {
                    HorizontalAlignment = HorizontalAlignment.Right,
                    VerticalAlignment = VerticalAlignment.Top,
                    Height = HeightDip,
                    Padding = new Thickness(12, 0, 14, 0),
                    CornerRadius = new CornerRadius(HeightDip / 2),
                    Background = Plate,
                    Opacity = 0.9,
                    Child = row,
                };
                Pulse(true);
            }

            public void Place(Int32Rect area, double scale)
            {
                int width = (int)Math.Round(WidthDip * scale);
                int height = (int)Math.Round(HeightDip * scale);
                int margin = (int)Math.Round(MarginDip * scale);
                MoveTo(area.X + area.Width - width - margin, area.Y + margin, width, height);
            }

            public void SetState(string elapsed, bool paused)
            {
                _text.Text = (paused ? "PAUSED " : "REC ") + elapsed;
                if (paused == _paused) return;
                _paused = paused;
                _dot.Visibility = paused ? Visibility.Collapsed : Visibility.Visible;
                _pauseSign.Visibility = paused ? Visibility.Visible : Visibility.Collapsed;
                Pulse(!paused);
            }

            /// <summary>The red dot breathes slowly while recording, the usual sign that it is live.</summary>
            private void Pulse(bool on)
            {
                if (!on)
                {
                    _dot.BeginAnimation(OpacityProperty, null);
                    return;
                }
                var breathe = new DoubleAnimation(1.0, 0.35, new Duration(TimeSpan.FromMilliseconds(900)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut },
                };
                _dot.BeginAnimation(OpacityProperty, breathe);
            }
        }

        /// <summary>A large record, pause or play symbol that shows briefly and fades.</summary>
        private sealed class FlashWindow : OverlayWindow
        {
            private const double SizeDip = 128;

            private readonly Border _plate;
            private readonly ScaleTransform _scale = new ScaleTransform(1, 1);

            public FlashWindow() : base("Proofly recording state")
            {
                Width = SizeDip;
                Height = SizeDip;
                _plate = new Border
                {
                    Width = 112,
                    Height = 112,
                    CornerRadius = new CornerRadius(56),
                    Background = Plate,
                    Opacity = 0,
                    RenderTransformOrigin = new Point(0.5, 0.5),
                    RenderTransform = _scale,
                };
                Content = _plate;
            }

            public void Place(Int32Rect area, double scale)
            {
                int size = (int)Math.Round(SizeDip * scale);
                MoveTo(area.X + (area.Width - size) / 2, area.Y + (area.Height - size) / 2, size, size);
            }

            public void Play(Symbol symbol)
            {
                _plate.Child = Draw(symbol);

                var fade = new DoubleAnimationUsingKeyFrames();
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.Zero)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(120))));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.92, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(700))));
                fade.KeyFrames.Add(new EasingDoubleKeyFrame(0.0, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(1250)),
                    new QuadraticEase { EasingMode = EasingMode.EaseIn }));
                _plate.BeginAnimation(OpacityProperty, fade);

                var pop = new DoubleAnimation(0.8, 1.0, new Duration(TimeSpan.FromMilliseconds(160)))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                };
                _scale.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
                _scale.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
            }

            private static UIElement Draw(Symbol symbol)
            {
                if (symbol == Symbol.Pause)
                {
                    var bars = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                    };
                    bars.Children.Add(new Rectangle { Width = 13, Height = 44, RadiusX = 2, RadiusY = 2, Fill = Light, Margin = new Thickness(0, 0, 12, 0) });
                    bars.Children.Add(new Rectangle { Width = 13, Height = 44, RadiusX = 2, RadiusY = 2, Fill = Light });
                    return bars;
                }
                if (symbol == Symbol.Play)
                {
                    var triangle = new Polygon
                    {
                        Fill = Light,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        // Nudged right, so the triangle looks centred in the circle.
                        Margin = new Thickness(8, 0, 0, 0),
                        StrokeLineJoin = PenLineJoin.Round,
                        Stroke = Light,
                        StrokeThickness = 3,
                    };
                    triangle.Points.Add(new Point(0, 0));
                    triangle.Points.Add(new Point(38, 22));
                    triangle.Points.Add(new Point(0, 44));
                    return triangle;
                }
                return new Ellipse
                {
                    Width = 44,
                    Height = 44,
                    Fill = RecordRed,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                };
            }
        }
    }
}
