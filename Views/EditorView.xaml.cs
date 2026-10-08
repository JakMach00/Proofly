using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Proofly.Core;
using Proofly.Models;
using Proofly.Services;

namespace Proofly.Views
{
    /// <summary>
    /// The annotation editor. Annotations stay separate objects until Save
    /// flattens them into the screenshot, so everything can be moved, reshaped
    /// and undone up to that point.
    /// </summary>
    public partial class EditorView : UserControl
    {
        private const string DefaultColor = "#ff3b30";
        private const string HighlightColor = "#ffcc00";
        private const double HandleScreenRadius = 6;
        private const int MaxHistory = 50;
        private const double MaxZoom = 6.0;
        private const double ZoomStep = 1.25;

        private static readonly Brush SelectionBrush = MakeBrush(Color.FromRgb(0x00, 0xE5, 0xFF));
        private static readonly Brush CropShade = MakeBrush(Color.FromArgb(150, 0, 0, 0));

        /// <summary>A crop cannot be made smaller than this, in image pixels.</summary>
        private const double MinCrop = 12;

        private enum DragMode
        {
            Create,
            Move,
            Handle,
            Crop,
            Draw,
        }

        private sealed class Drag
        {
            public DragMode Mode;
            public Annotation Target;
            public string Handle;

            /// <summary>The crop frame as it was when the gesture began.</summary>
            public Rect StartRect;

            public double Ox;
            public double Oy;

            /// <summary>Set once the pointer actually moves during the gesture.</summary>
            public bool Moved;

            /// <summary>The gesture placed this annotation, so it is a change even unmoved.</summary>
            public bool Created;
        }

        /// <summary>One undo step. The crop is part of it, so undo also reverses a crop.</summary>
        private sealed class Snapshot
        {
            public Annotation[] Items;
            public int NextStep;
            public Int32Rect? Crop;

            /// <summary>Whether the editor was clean before this step, restored on undo.</summary>
            public bool Dirty;
        }

        /// <summary>
        /// A text box placed by the current edit, with the undo step taken just
        /// before it. Leaving the box empty rolls the placement back entirely.
        /// </summary>
        private sealed class PlacedText
        {
            public string Id;
            public int HistoryIndex;
        }

        private Shot _shot;
        private BitmapSource _image;
        private int _index;
        private int _total;

        private List<Annotation> _items = new List<Annotation>();
        private readonly List<Snapshot> _history = new List<Snapshot>();
        private Int32Rect? _crop;
        /// <summary>
        /// The crop frame while the Crop tool is open, in image pixels. Nothing
        /// is cropped until it is applied.
        /// </summary>
        private Rect? _cropDraft;
        private int _nextStep = 1;
        private string _selectedId;
        private bool _dirty;
        private string _editingId;
        private string _coalesceKey;
        private PlacedText _placed;
        private Drag _drag;

        /// <summary>Screen units per image pixel, or null to fit the whole image in view.</summary>
        private double? _zoom;
        private double _panX;
        private double _panY;
        private Point? _panGrab;

        private string _tool;
        private string _color;
        private double _width;
        private int _fontSize;
        private string _font;
        private bool _outline;

        /// <summary>True while the code is writing state into the controls.</summary>
        private bool _syncing = true;

        private readonly DispatcherTimer _noticeTimer;

        /// <summary>Raised after the screenshot was replaced with its annotated version.</summary>
        public event Action<Shot> Saved;

        public event Action CloseRequested;

        /// <summary>Raised when the note of the open screenshot was changed and should be stored.</summary>
        public event Action<Shot> NoteChanged;

        /// <summary>Asks for the previous (-1) or next (+1) screenshot.</summary>
        public event Action<int> NavigateRequested;

        /// <summary>The dialog used to ask about unsaved changes.</summary>
        public ConfirmOverlay Dialog { get; set; }

        public EditorView()
        {
            InitializeComponent();
            Surface.Painter = Paint;

            foreach (string name in EditorFonts.Names) FontCombo.Items.Add(name);

            _noticeTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(2600) };
            _noticeTimer.Tick += delegate
            {
                _noticeTimer.Stop();
                NoticeText.Text = "";
            };

            // The drawing setup is remembered, so the editor does not fall back
            // to the same tool for every screenshot.
            AppSettings settings = App.Settings ?? new AppSettings();
            _tool = settings.EditorTool;
            _color = settings.EditorColor;
            _width = settings.EditorWidth;
            _fontSize = settings.EditorFontSize;
            _font = settings.EditorFont;
            _outline = settings.EditorOutline;

            // Controls raise change events while they are being built, before
            // the rest of the view exists. From here on they are real input.
            _syncing = false;
        }

        private static Brush MakeBrush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        // ------------------------------------------------------------------
        // Opening and state
        // ------------------------------------------------------------------

        public void Open(Shot shot, int index, int total)
        {
            // The note of the screenshot being left is kept before anything changes.
            CommitNote();
            _shot = shot;
            _index = index;
            _total = total;
            _image = Bitmaps.Decode(File.ReadAllBytes(shot.FilePath));
            _zoom = null;
            _panX = 0;
            _panY = 0;

            _items = new List<Annotation>();
            _history.Clear();
            _crop = null;
            _cropDraft = null;
            if (_tool == "crop") _tool = "select";
            _selectedId = null;
            _nextStep = 1;
            _dirty = false;
            _editingId = null;
            _coalesceKey = null;
            _placed = null;
            _drag = null;
            InlineHost.Visibility = Visibility.Collapsed;
            LoadNote();

            Visibility = Visibility.Visible;
            Refresh();
            Focus();
            // Once more after layout, for the first time the view is shown.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                if (IsOpen && _editingId == null && !IsKeyboardFocusWithin) Focus();
            }));
        }

        public void Hide()
        {
            CommitNote();
            Persist();
            Visibility = Visibility.Collapsed;
            _image = null;
            _shot = null;
            _items = new List<Annotation>();
            _history.Clear();
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        private void Persist()
        {
            AppSettings settings = App.Settings;
            if (settings == null) return;
            // Crop is a one-off action, so the editor reopens on Select after it.
            settings.EditorTool = _tool == "crop" ? "select" : _tool;
            settings.EditorColor = _color;
            settings.EditorWidth = _width;
            settings.EditorFontSize = _fontSize;
            settings.EditorFont = _font;
            settings.EditorOutline = _outline;
            settings.Save();
        }

        private Annotation Find(string id)
        {
            if (id == null) return null;
            foreach (Annotation item in _items)
                if (item.Id == id) return item;
            return null;
        }

        private Annotation Selected
        {
            get { return Find(_selectedId); }
        }

        /// <summary>
        /// Swaps an annotation for a copy and returns the copy. Undo snapshots
        /// share annotation objects, so anything a snapshot may hold has to be
        /// copied before it is changed.
        /// </summary>
        private Annotation Detach(string id)
        {
            for (int i = 0; i < _items.Count; i++)
            {
                if (_items[i].Id != id) continue;
                Annotation copy = _items[i].Clone();
                _items[i] = copy;
                return copy;
            }
            return null;
        }

        private void Remove(string id)
        {
            _items.RemoveAll(delegate(Annotation a) { return a.Id == id; });
        }

        /// <summary>Records the state before a change so undo can step back through it.</summary>
        private void Push(string coalesceKey = null)
        {
            if (coalesceKey != null && _coalesceKey == coalesceKey) return;
            _coalesceKey = coalesceKey;
            _history.Add(new Snapshot { Items = _items.ToArray(), NextStep = _nextStep, Crop = _crop, Dirty = _dirty });
            if (_history.Count > MaxHistory)
            {
                _history.RemoveAt(0);
                if (_placed != null) _placed.HistoryIndex--;
            }
            _dirty = true;
        }

        /// <summary>
        /// Takes back the undo step of a gesture that turned out to change
        /// nothing, together with the unsaved flag it raised.
        /// </summary>
        private void DropLastStep()
        {
            if (_history.Count == 0) return;
            Snapshot last = _history[_history.Count - 1];
            _dirty = last.Dirty;
            _history.RemoveAt(_history.Count - 1);
        }

        private void Restore(Snapshot snapshot)
        {
            _items = new List<Annotation>(snapshot.Items);
            _nextStep = snapshot.NextStep;
            _crop = snapshot.Crop;
            _dirty = snapshot.Dirty;
        }

        // ------------------------------------------------------------------
        // Geometry
        // ------------------------------------------------------------------

        /// <summary>True while the crop frame is open.</summary>
        private bool Cropping
        {
            get { return _cropDraft.HasValue && _tool == "crop"; }
        }

        /// <summary>The visible part of the screenshot, in image pixels.</summary>
        private Int32Rect Area
        {
            get
            {
                // The whole screenshot is shown while the crop frame is open, so
                // an earlier crop can be widened again.
                if (_crop.HasValue && !Cropping) return _crop.Value;
                return new Int32Rect(0, 0, _image.PixelWidth, _image.PixelHeight);
            }
        }

        /// <summary>The scale at which the whole image fits. It never enlarges.</summary>
        private double FitScale
        {
            get
            {
                Int32Rect area = Area;
                double availableWidth = Surface.ActualWidth;
                double availableHeight = Surface.ActualHeight;
                if (availableWidth <= 0 || availableHeight <= 0 || area.Width <= 0 || area.Height <= 0) return 1.0;
                return Math.Min(1.0, Math.Min(availableWidth / area.Width, availableHeight / area.Height));
            }
        }

        /// <summary>
        /// Where the image is drawn inside the surface. Zoomed in, it is larger
        /// than the surface and shifted by the pan offset.
        /// </summary>
        private Rect ViewRect
        {
            get
            {
                if (_image == null) return new Rect(0, 0, 0, 0);
                Int32Rect area = Area;
                double availableWidth = Surface.ActualWidth;
                double availableHeight = Surface.ActualHeight;
                if (availableWidth <= 0 || availableHeight <= 0 || area.Width <= 0 || area.Height <= 0)
                    return new Rect(0, 0, 0, 0);
                double scale = _zoom ?? FitScale;
                double width = area.Width * scale;
                double height = area.Height * scale;
                double x = width <= availableWidth
                    ? (availableWidth - width) / 2
                    : -Math.Max(0, Math.Min(_panX, width - availableWidth));
                double y = height <= availableHeight
                    ? (availableHeight - height) / 2
                    : -Math.Max(0, Math.Min(_panY, height - availableHeight));
                return new Rect(x, y, width, height);
            }
        }

        /// <summary>
        /// Changes the zoom while keeping the image point under the anchor where
        /// it is on screen. Null fits the whole image again.
        /// </summary>
        private void SetZoom(double? zoom, Point anchor)
        {
            if (_image == null) return;
            Point pinned = ToImage(anchor);
            double fit = FitScale;
            if (zoom.HasValue)
            {
                double clamped = Math.Max(fit, Math.Min(MaxZoom, zoom.Value));
                _zoom = clamped <= fit * 1.001 ? (double?)null : clamped;
            }
            else
            {
                _zoom = null;
            }

            double scale = _zoom ?? fit;
            Int32Rect area = Area;
            _panX = (pinned.X - area.X) * scale - anchor.X;
            _panY = (pinned.Y - area.Y) * scale - anchor.Y;
            ClampPan();
            Refresh();
        }

        private void ClampPan()
        {
            Int32Rect area = Area;
            double scale = _zoom ?? FitScale;
            _panX = Math.Max(0, Math.Min(_panX, Math.Max(0, area.Width * scale - Surface.ActualWidth)));
            _panY = Math.Max(0, Math.Min(_panY, Math.Max(0, area.Height * scale - Surface.ActualHeight)));
        }

        private Point SurfaceCentre
        {
            get { return new Point(Surface.ActualWidth / 2, Surface.ActualHeight / 2); }
        }

        private void ZoomBy(double factor, Point anchor)
        {
            SetZoom((_zoom ?? FitScale) * factor, anchor);
        }

        /// <summary>Image pixels per screen unit at the current zoom.</summary>
        private double ViewScale
        {
            get
            {
                Rect view = ViewRect;
                return view.Width > 0 ? Area.Width / view.Width : 1.0;
            }
        }

        private Point ToImage(Point surfacePoint)
        {
            Rect view = ViewRect;
            Int32Rect area = Area;
            double k = ViewScale;
            return new Point(area.X + (surfacePoint.X - view.X) * k, area.Y + (surfacePoint.Y - view.Y) * k);
        }

        private struct HandlePoint
        {
            public string Id;
            public Point Position;
        }

        private static List<HandlePoint> HandlePoints(Annotation annotation)
        {
            var points = new List<HandlePoint>();
            var shape = annotation as ShapeAnnotation;
            if (shape == null) return points;
            if (shape.Kind == ShapeKind.Arrow)
            {
                points.Add(new HandlePoint { Id = "p1", Position = new Point(shape.X1, shape.Y1) });
                points.Add(new HandlePoint { Id = "p2", Position = new Point(shape.X2, shape.Y2) });
                return points;
            }
            points.Add(new HandlePoint { Id = "x1y1", Position = new Point(shape.X1, shape.Y1) });
            points.Add(new HandlePoint { Id = "x2y1", Position = new Point(shape.X2, shape.Y1) });
            points.Add(new HandlePoint { Id = "x1y2", Position = new Point(shape.X1, shape.Y2) });
            points.Add(new HandlePoint { Id = "x2y2", Position = new Point(shape.X2, shape.Y2) });
            return points;
        }

        private static void MoveHandle(ShapeAnnotation shape, string handle, Point p)
        {
            switch (handle)
            {
                case "p1":
                case "x1y1":
                    shape.X1 = p.X;
                    shape.Y1 = p.Y;
                    break;
                case "p2":
                case "x2y2":
                    shape.X2 = p.X;
                    shape.Y2 = p.Y;
                    break;
                case "x2y1":
                    shape.X2 = p.X;
                    shape.Y1 = p.Y;
                    break;
                case "x1y2":
                    shape.X1 = p.X;
                    shape.Y2 = p.Y;
                    break;
            }
        }

        private string HitHandle(Point p)
        {
            Annotation selected = Selected;
            if (selected == null) return null;
            double radius = HandleScreenRadius * ViewScale * 1.6;
            foreach (HandlePoint handle in HandlePoints(selected))
            {
                double dx = handle.Position.X - p.X;
                double dy = handle.Position.Y - p.Y;
                if (Math.Sqrt(dx * dx + dy * dy) <= radius) return handle.Id;
            }
            return null;
        }

        private Annotation HitTest(Point p)
        {
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                Rect bounds = AnnotationRenderer.Bounds(_items[i]);
                if (!bounds.IsEmpty && bounds.Contains(p)) return _items[i];
            }
            return null;
        }

        // ------------------------------------------------------------------
        // Painting
        // ------------------------------------------------------------------

        private void Paint(DrawingContext dc)
        {
            if (_image == null) return;
            Rect view = ViewRect;
            if (view.Width <= 0 || view.Height <= 0) return;
            Int32Rect area = Area;
            double k = area.Width / view.Width;

            // Zoomed in, the image is larger than the surface, so it is cut off
            // at the surface edge as well.
            Rect visible = view;
            visible.Intersect(new Rect(0, 0, Surface.ActualWidth, Surface.ActualHeight));
            if (visible.IsEmpty) return;
            dc.PushClip(new RectangleGeometry(visible));
            dc.PushTransform(new TranslateTransform(view.X, view.Y));
            dc.PushTransform(new ScaleTransform(1 / k, 1 / k));
            // Annotations are kept in the coordinates of the original image, so
            // a crop never has to move them.
            dc.PushTransform(new TranslateTransform(-area.X, -area.Y));

            dc.DrawImage(_image, new Rect(0, 0, _image.PixelWidth, _image.PixelHeight));

            // While a label is edited in place the field over the image shows
            // it, so it is not painted twice.
            foreach (Annotation item in _items)
            {
                if (item.Id == _editingId) continue;
                AnnotationRenderer.Draw(dc, item);
            }

            if (Cropping)
            {
                PaintCropFrame(dc, k);
                dc.Pop();
                dc.Pop();
                dc.Pop();
                dc.Pop();
                return;
            }

            Annotation selected = Selected;
            if (selected != null && selected.Id != _editingId)
            {
                Rect bounds = AnnotationRenderer.Bounds(selected);
                if (!bounds.IsEmpty)
                {
                    var frame = new Pen(SelectionBrush, 1.5 * k) { DashStyle = new DashStyle(new double[] { 5.3, 4 }, 0) };
                    dc.DrawRectangle(null, frame, bounds);
                }
                var ring = new Pen(SelectionBrush, 1.5 * k);
                double radius = HandleScreenRadius * k;
                foreach (HandlePoint handle in HandlePoints(selected))
                    dc.DrawEllipse(Brushes.White, ring, handle.Position, radius, radius);
            }

            dc.Pop();
            dc.Pop();
            dc.Pop();
            dc.Pop();
        }

        /// <summary>
        /// Darkens what the crop would cut away and draws the frame with a grip
        /// on every corner and every side.
        /// </summary>
        private void PaintCropFrame(DrawingContext dc, double k)
        {
            Rect frame = _cropDraft.Value;
            var whole = new RectangleGeometry(new Rect(0, 0, _image.PixelWidth, _image.PixelHeight));
            dc.DrawGeometry(CropShade, null, new CombinedGeometry(GeometryCombineMode.Exclude, whole, new RectangleGeometry(frame)));
            dc.DrawRectangle(null, new Pen(Brushes.White, 1.5 * k), frame);

            var outline = new Pen(SelectionBrush, 1.5 * k);
            foreach (HandlePoint grip in CropGrips(frame))
            {
                // Corner grips are a little larger than the ones on the sides.
                double half = (grip.Id.Length == 2 ? 6.5 : 5) * k;
                dc.DrawRectangle(
                    Brushes.White, outline,
                    new Rect(grip.Position.X - half, grip.Position.Y - half, half * 2, half * 2));
            }
        }

        /// <summary>Repaints the image and brings every control in line with the state.</summary>
        private void Refresh()
        {
            Surface.InvalidateVisual();
            UpdateBars();
            if (_editingId != null) LayoutInline();
        }

        private void UpdateBars()
        {
            _syncing = true;
            try
            {
                foreach (object child in ToolRow.Children)
                {
                    var radio = child as RadioButton;
                    if (radio != null) radio.IsChecked = (radio.Tag as string) == _tool;
                }
                foreach (object child in SwatchBar.Children)
                {
                    var radio = child as RadioButton;
                    if (radio != null)
                        radio.IsChecked = string.Equals(radio.Tag as string, _color, StringComparison.OrdinalIgnoreCase);
                }

                bool cropping = Cropping;
                UndoCropButton.Visibility = _crop.HasValue && !cropping ? Visibility.Visible : Visibility.Collapsed;
                CropOption.Visibility = cropping ? Visibility.Visible : Visibility.Collapsed;
                ColorOption.Visibility = cropping ? Visibility.Collapsed : Visibility.Visible;
                if (cropping) ShowCropSize();
                // With the crop frame open, Undo closes it.
                UndoButton.IsEnabled = _history.Count > 0 || Cropping;
                DeleteSelectedButton.IsEnabled = _selectedId != null;
                DirtyText.Visibility = _dirty ? Visibility.Visible : Visibility.Collapsed;
                SaveButton.IsEnabled = _dirty;
                PreviousButton.IsEnabled = _index > 0;
                NextButton.IsEnabled = _index < _total - 1;
                PositionText.Text = (_index + 1) + " / " + _total;

                double shown = _zoom ?? FitScale;
                ZoomText.Text = _zoom.HasValue
                    ? Math.Round(shown * 100).ToString(CultureInfo.InvariantCulture) + "%"
                    : "Fit";
                // Enlarged pixels stay sharp, a reduced image is smoothed.
                RenderOptions.SetBitmapScalingMode(
                    Surface, shown > 1.0 ? BitmapScalingMode.NearestNeighbor : BitmapScalingMode.HighQuality);

                Annotation selected = Selected;
                var selectedShape = selected as ShapeAnnotation;
                var selectedStep = selected as StepAnnotation;
                var selectedText = selected as TextAnnotation;

                // Redaction and highlighting are filled blocks, so a stroke
                // width means nothing for either of them.
                bool solid = _tool == "redact" || _tool == "highlight" || (selectedShape != null && selectedShape.IsSolid);
                // Text has a size of its own and no stroke, so Thickness makes
                // way for the text settings. That also keeps the row on one line.
                bool textInUse = _tool == "text" || selectedText != null;
                WidthOption.Visibility = solid || cropping || textInUse ? Visibility.Collapsed : Visibility.Visible;
                SolidNote.Visibility = solid && !cropping ? Visibility.Visible : Visibility.Collapsed;
                WidthSlider.Value = _width;

                bool stepActive = _tool == "step" || selectedStep != null;
                StepOption.Visibility = stepActive && !cropping ? Visibility.Visible : Visibility.Collapsed;
                StepLabel.Text = selectedStep != null ? "Step number" : "Next step number";
                SetBoxText(StepBox, selectedStep != null ? selectedStep.Number : _nextStep);

                bool textActive = _tool == "text" || selectedText != null;
                TextOption.Visibility = textActive && !cropping ? Visibility.Visible : Visibility.Collapsed;
                FontCombo.SelectedItem = selectedText != null ? selectedText.Font : _font;
                SetBoxText(SizeBox, selectedText != null ? (int)Math.Round(selectedText.Size) : _fontSize);
                OutlineCheck.IsChecked = selectedText != null ? selectedText.Outline : _outline;
            }
            finally
            {
                _syncing = false;
            }
        }

        /// <summary>Leaves a field alone while the user is typing in it.</summary>
        private static void SetBoxText(TextBox box, int value)
        {
            if (box.IsKeyboardFocusWithin) return;
            string text = value.ToString(CultureInfo.InvariantCulture);
            if (box.Text != text) box.Text = text;
        }

        private void ShowNotice(string text)
        {
            NoticeText.Text = text;
            _noticeTimer.Stop();
            _noticeTimer.Start();
        }

        // ------------------------------------------------------------------
        // Mouse
        // ------------------------------------------------------------------

        private void Surface_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (_image == null) return;

            // Clicking away from a label being edited commits it and does
            // nothing else, the way a text box in any drawing program behaves.
            if (_editingId != null)
            {
                FinishEditing(true);
                e.Handled = true;
                return;
            }

            Focus();
            Point raw = e.GetPosition(Surface);
            if (!ViewRect.Contains(raw)) return;
            Point p = ToImage(raw);

            // Double clicking a text label opens it for editing in place.
            if (e.ClickCount == 2 && !Cropping)
            {
                var label = HitTest(p) as TextAnnotation;
                if (label != null)
                {
                    _placed = null;
                    BeginEditing(label.Id);
                    e.Handled = true;
                    return;
                }
            }

            if (Cropping)
            {
                // A grip reshapes the frame, the inside moves it, and dragging
                // outside it draws a new frame from scratch.
                Rect frame = _cropDraft.Value;
                string grip = HitCropGrip(p);
                if (grip == null) grip = frame.Contains(p) ? "move" : "new";
                StartDrag(new Drag { Mode = DragMode.Crop, Handle = grip, StartRect = frame, Ox = p.X, Oy = p.Y });
                return;
            }

            Annotation selected = Selected;
            string handle = HitHandle(p);
            if (handle != null && selected != null)
            {
                Push();
                StartDrag(new Drag { Mode = DragMode.Handle, Target = Detach(selected.Id), Handle = handle, Ox = p.X, Oy = p.Y });
                return;
            }

            if (_tool == "select")
            {
                Annotation hit = HitTest(p);
                _selectedId = hit == null ? null : hit.Id;
                if (hit != null)
                {
                    Push();
                    StartDrag(new Drag { Mode = DragMode.Move, Target = Detach(hit.Id), Ox = p.X, Oy = p.Y });
                }
                else
                {
                    // On empty space of a zoomed image, holding the button
                    // drags the view around.
                    if (_zoom.HasValue)
                    {
                        _panGrab = raw;
                        Surface.CaptureMouse();
                    }
                    Refresh();
                }
                return;
            }

            // Clicking an existing step or text label with the same tool picks
            // it up instead of stacking another one on top.
            if (_tool == "step" || _tool == "text")
            {
                Annotation existing = HitTest(p);
                bool sameKind = (_tool == "step" && existing is StepAnnotation) ||
                                (_tool == "text" && existing is TextAnnotation);
                if (sameKind)
                {
                    _selectedId = existing.Id;
                    Push();
                    StartDrag(new Drag { Mode = DragMode.Move, Target = Detach(existing.Id), Ox = p.X, Oy = p.Y });
                    return;
                }
            }

            Push();

            if (_tool == "step")
            {
                var step = new StepAnnotation
                {
                    Color = _color,
                    Width = _width,
                    X = p.X,
                    Y = p.Y,
                    Number = _nextStep,
                    Radius = StepAnnotation.RadiusFor(_width),
                };
                _items.Add(step);
                _nextStep++;
                _selectedId = step.Id;
                StartDrag(new Drag { Mode = DragMode.Move, Target = step, Ox = p.X, Oy = p.Y, Created = true });
                return;
            }

            if (_tool == "text")
            {
                var text = new TextAnnotation
                {
                    Color = _color,
                    Width = _width,
                    X = p.X,
                    Y = p.Y,
                    Text = "",
                    Size = _fontSize,
                    Font = _font,
                    Outline = _outline,
                };
                _items.Add(text);
                _placed = new PlacedText { Id = text.Id, HistoryIndex = _history.Count - 1 };
                // Typing joins the placement, so one undo removes the whole label.
                _coalesceKey = "text:" + text.Id;
                BeginEditing(text.Id);
                e.Handled = true;
                return;
            }

            if (_tool == "marker")
            {
                var stroke = new MarkerAnnotation { Color = _color, Width = _width };
                stroke.Points.Add(p);
                _items.Add(stroke);
                _selectedId = null;
                StartDrag(new Drag { Mode = DragMode.Draw, Target = stroke, Ox = p.X, Oy = p.Y, Created = true });
                return;
            }

            var shape = new ShapeAnnotation
            {
                Kind = KindFor(_tool),
                Color = _color,
                Width = _width,
                X1 = p.X,
                Y1 = p.Y,
                X2 = p.X,
                Y2 = p.Y,
            };
            _items.Add(shape);
            _selectedId = shape.Id;
            StartDrag(new Drag { Mode = DragMode.Create, Target = shape, Ox = p.X, Oy = p.Y });
        }

        private static ShapeKind KindFor(string tool)
        {
            switch (tool)
            {
                case "rect":
                    return ShapeKind.Rect;
                case "ellipse":
                    return ShapeKind.Ellipse;
                case "highlight":
                    return ShapeKind.Highlight;
                case "redact":
                    return ShapeKind.Redact;
                default:
                    return ShapeKind.Arrow;
            }
        }

        private void StartDrag(Drag drag)
        {
            _drag = drag;
            Surface.CaptureMouse();
            Refresh();
        }

        private void Surface_MouseMove(object sender, MouseEventArgs e)
        {
            if (_image == null) return;
            if (_panGrab.HasValue)
            {
                Point now = e.GetPosition(Surface);
                _panX -= now.X - _panGrab.Value.X;
                _panY -= now.Y - _panGrab.Value.Y;
                _panGrab = now;
                ClampPan();
                Refresh();
                return;
            }

            Point p = ToImage(e.GetPosition(Surface));
            Drag drag = _drag;

            if (drag == null && Cropping)
            {
                Surface.Cursor = CropCursor(HitCropGrip(p), _cropDraft.Value.Contains(p));
                return;
            }

            if (drag == null)
            {
                if (HitHandle(p) != null) Surface.Cursor = Cursors.Hand;
                else if (_tool == "select")
                    Surface.Cursor = _zoom.HasValue && HitTest(p) == null ? Cursors.SizeAll : Cursors.Arrow;
                else Surface.Cursor = Cursors.Cross;
                return;
            }

            if (drag.Mode == DragMode.Crop)
            {
                drag.Moved = true;
                _cropDraft = ReshapeCrop(drag, p);
                ShowCropSize();
                Surface.InvalidateVisual();
                return;
            }

            if (drag.Mode == DragMode.Draw)
            {
                // Points closer than a couple of pixels add nothing but size.
                double dx = p.X - drag.Ox;
                double dy = p.Y - drag.Oy;
                if (Math.Sqrt(dx * dx + dy * dy) < 2 * ViewScale) return;
                drag.Moved = true;
                drag.Ox = p.X;
                drag.Oy = p.Y;
                ((MarkerAnnotation)drag.Target).Points.Add(p);
                Surface.InvalidateVisual();
                return;
            }

            if (drag.Target == null) return;
            drag.Moved = true;
            var shape = drag.Target as ShapeAnnotation;
            if (drag.Mode == DragMode.Handle && shape != null)
            {
                MoveHandle(shape, drag.Handle, p);
            }
            else if (drag.Mode == DragMode.Create && shape != null)
            {
                shape.X2 = p.X;
                shape.Y2 = p.Y;
            }
            else
            {
                drag.Target.Offset(p.X - drag.Ox, p.Y - drag.Oy);
                drag.Ox = p.X;
                drag.Oy = p.Y;
            }
            Surface.InvalidateVisual();
        }

        private void Surface_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_panGrab.HasValue)
            {
                _panGrab = null;
                if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();
            }
            EndDrag();
        }

        private void Surface_LostCapture(object sender, MouseEventArgs e)
        {
            _panGrab = null;
            EndDrag();
        }

        private void EndDrag()
        {
            Drag drag = _drag;
            _drag = null;
            if (drag == null) return;
            if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();

            if (drag.Mode == DragMode.Crop)
            {
                // A frame drawn too small to be meant is taken back. The crop
                // itself only happens on Apply or Enter.
                if (_cropDraft.HasValue && (_cropDraft.Value.Width < MinCrop || _cropDraft.Value.Height < MinCrop))
                    _cropDraft = drag.StartRect;
                Refresh();
                return;
            }

            // Selecting or grabbing something without moving it is not an edit.
            if ((drag.Mode == DragMode.Move || drag.Mode == DragMode.Handle) && !drag.Moved && !drag.Created)
            {
                DropLastStep();
                Refresh();
                return;
            }

            if (drag.Mode == DragMode.Create)
            {
                var shape = drag.Target as ShapeAnnotation;
                bool tooSmall = shape != null &&
                                Math.Abs(shape.X2 - shape.X1) <= 3 && Math.Abs(shape.Y2 - shape.Y1) <= 3;
                if (tooSmall)
                {
                    Remove(shape.Id);
                    _selectedId = null;
                    DropLastStep();
                }
            }
            Refresh();
        }

        // ------------------------------------------------------------------
        // Crop
        // ------------------------------------------------------------------

        private Rect ImageBounds
        {
            get { return new Rect(0, 0, _image.PixelWidth, _image.PixelHeight); }
        }

        /// <summary>Opens the crop frame on the current crop, or on the whole screenshot.</summary>
        private void BeginCrop()
        {
            _selectedId = null;
            _cropDraft = _crop.HasValue
                ? new Rect(_crop.Value.X, _crop.Value.Y, _crop.Value.Width, _crop.Value.Height)
                : ImageBounds;
            // The visible area changes, so the view goes back to showing all of it.
            _zoom = null;
        }

        /// <summary>The eight grips of the frame: corners have two letter names, sides one.</summary>
        private static List<HandlePoint> CropGrips(Rect frame)
        {
            double midX = frame.Left + frame.Width / 2;
            double midY = frame.Top + frame.Height / 2;
            return new List<HandlePoint>
            {
                new HandlePoint { Id = "nw", Position = new Point(frame.Left, frame.Top) },
                new HandlePoint { Id = "ne", Position = new Point(frame.Right, frame.Top) },
                new HandlePoint { Id = "sw", Position = new Point(frame.Left, frame.Bottom) },
                new HandlePoint { Id = "se", Position = new Point(frame.Right, frame.Bottom) },
                new HandlePoint { Id = "n", Position = new Point(midX, frame.Top) },
                new HandlePoint { Id = "s", Position = new Point(midX, frame.Bottom) },
                new HandlePoint { Id = "w", Position = new Point(frame.Left, midY) },
                new HandlePoint { Id = "e", Position = new Point(frame.Right, midY) },
            };
        }

        private string HitCropGrip(Point p)
        {
            if (!_cropDraft.HasValue) return null;
            double reach = 11 * ViewScale;
            foreach (HandlePoint grip in CropGrips(_cropDraft.Value))
            {
                if (Math.Abs(grip.Position.X - p.X) <= reach && Math.Abs(grip.Position.Y - p.Y) <= reach)
                    return grip.Id;
            }
            return null;
        }

        private static Cursor CropCursor(string grip, bool inside)
        {
            switch (grip)
            {
                case "nw":
                case "se":
                    return Cursors.SizeNWSE;
                case "ne":
                case "sw":
                    return Cursors.SizeNESW;
                case "n":
                case "s":
                    return Cursors.SizeNS;
                case "w":
                case "e":
                    return Cursors.SizeWE;
                default:
                    return inside ? Cursors.SizeAll : Cursors.Cross;
            }
        }

        /// <summary>The frame a crop gesture produces for the current pointer position.</summary>
        private Rect ReshapeCrop(Drag drag, Point p)
        {
            Rect bounds = ImageBounds;
            double x = Math.Max(bounds.Left, Math.Min(bounds.Right, p.X));
            double y = Math.Max(bounds.Top, Math.Min(bounds.Bottom, p.Y));
            Rect start = drag.StartRect;

            if (drag.Handle == "new")
            {
                double ox = Math.Max(bounds.Left, Math.Min(bounds.Right, drag.Ox));
                double oy = Math.Max(bounds.Top, Math.Min(bounds.Bottom, drag.Oy));
                return new Rect(new Point(ox, oy), new Point(x, y));
            }

            if (drag.Handle == "move")
            {
                // The frame keeps its size and stops at the edges of the image.
                double left = start.Left + (p.X - drag.Ox);
                double top = start.Top + (p.Y - drag.Oy);
                left = Math.Max(bounds.Left, Math.Min(bounds.Right - start.Width, left));
                top = Math.Max(bounds.Top, Math.Min(bounds.Bottom - start.Height, top));
                return new Rect(left, top, start.Width, start.Height);
            }

            double l = start.Left;
            double t = start.Top;
            double r = start.Right;
            double b = start.Bottom;
            // A side cannot be dragged past the opposite one.
            if (drag.Handle.Contains("w")) l = Math.Min(x, r - MinCrop);
            if (drag.Handle.Contains("e")) r = Math.Max(x, l + MinCrop);
            if (drag.Handle.Contains("n")) t = Math.Min(y, b - MinCrop);
            if (drag.Handle.Contains("s")) b = Math.Max(y, t + MinCrop);
            return new Rect(new Point(l, t), new Point(r, b));
        }

        private void ShowCropSize()
        {
            if (!_cropDraft.HasValue) return;
            CropSizeText.Text = Math.Round(_cropDraft.Value.Width).ToString(CultureInfo.InvariantCulture) + " x " +
                                Math.Round(_cropDraft.Value.Height).ToString(CultureInfo.InvariantCulture);
        }

        /// <summary>Makes the framed area the visible part of the screenshot.</summary>
        private void CommitCrop()
        {
            if (!Cropping) return;
            Rect frame = _cropDraft.Value;
            int imageWidth = _image.PixelWidth;
            int imageHeight = _image.PixelHeight;
            int left = (int)Math.Max(0, Math.Min(imageWidth, Math.Round(frame.Left)));
            int top = (int)Math.Max(0, Math.Min(imageHeight, Math.Round(frame.Top)));
            int right = (int)Math.Max(0, Math.Min(imageWidth, Math.Round(frame.Right)));
            int bottom = (int)Math.Max(0, Math.Min(imageHeight, Math.Round(frame.Bottom)));

            _cropDraft = null;
            _zoom = null;
            SetTool("select");

            bool usable = right - left >= MinCrop && bottom - top >= MinCrop;
            bool whole = left == 0 && top == 0 && right == imageWidth && bottom == imageHeight;
            Int32Rect? wanted = !usable || whole ? (Int32Rect?)null : new Int32Rect(left, top, right - left, bottom - top);
            if (!usable) wanted = _crop;

            // Applying the frame as it already was changes nothing and so
            // leaves no undo step behind.
            if (!Nullable.Equals(wanted, _crop))
            {
                Push();
                _crop = wanted;
            }
            Persist();
            Refresh();
            Focus();
        }

        /// <summary>Closes the crop frame and leaves the screenshot as it was.</summary>
        private void CancelCrop()
        {
            if (!_cropDraft.HasValue && _tool != "crop") return;
            _cropDraft = null;
            _zoom = null;
            SetTool("select");
            Persist();
            Refresh();
            Focus();
        }

        private void ApplyCrop_Click(object sender, RoutedEventArgs e)
        {
            CommitCrop();
        }

        private void CancelCrop_Click(object sender, RoutedEventArgs e)
        {
            CancelCrop();
        }

        // ------------------------------------------------------------------
        // Zoom and pan
        // ------------------------------------------------------------------

        private void Surface_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_image == null) return;
            e.Handled = true;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl)
            {
                ZoomBy(e.Delta > 0 ? ZoomStep : 1 / ZoomStep, e.GetPosition(Surface));
                return;
            }
            if (!_zoom.HasValue) return;
            // The wheel scrolls up and down, with Shift it scrolls sideways.
            double step = e.Delta > 0 ? -80 : 80;
            if ((Keyboard.Modifiers & ModifierKeys.Shift) != 0) _panX += step;
            else _panY += step;
            ClampPan();
            Refresh();
        }

        /// <summary>The middle button drags the zoomed image around.</summary>
        private void Surface_ButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || !_zoom.HasValue || _drag != null) return;
            _panGrab = e.GetPosition(Surface);
            Surface.CaptureMouse();
            e.Handled = true;
        }

        private void Surface_ButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || !_panGrab.HasValue) return;
            _panGrab = null;
            if (Surface.IsMouseCaptured) Surface.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void ZoomIn_Click(object sender, RoutedEventArgs e)
        {
            ZoomBy(ZoomStep, SurfaceCentre);
        }

        private void ZoomOut_Click(object sender, RoutedEventArgs e)
        {
            ZoomBy(1 / ZoomStep, SurfaceCentre);
        }

        private void ZoomActual_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(1.0, SurfaceCentre);
        }

        private void ZoomFit_Click(object sender, RoutedEventArgs e)
        {
            SetZoom(null, SurfaceCentre);
        }

        // ------------------------------------------------------------------
        // Text editing in place
        // ------------------------------------------------------------------

        private void BeginEditing(string id)
        {
            var text = Find(id) as TextAnnotation;
            if (text == null) return;
            _selectedId = id;
            _editingId = id;

            _syncing = true;
            InlineBox.Text = text.Text ?? "";
            _syncing = false;

            InlineHost.Visibility = Visibility.Visible;
            Refresh();

            // Focus lands after the click has been fully handled, otherwise the
            // surface would take it straight back.
            Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(delegate
            {
                if (_editingId != id) return;
                InlineBox.Focus();
                InlineBox.CaretIndex = InlineBox.Text.Length;
            }));
        }

        /// <summary>Places the field exactly over the label it edits.</summary>
        private void LayoutInline()
        {
            var text = Find(_editingId) as TextAnnotation;
            if (text == null || _image == null) return;
            Rect view = ViewRect;
            Int32Rect area = Area;
            double k = ViewScale;
            double fontPx = Math.Max(4, text.Size / k);
            var family = new FontFamily(EditorFonts.FamilyFor(text.Font));
            Brush brush = AnnotationRenderer.BrushFor(text.Color);

            InlineBox.FontFamily = family;
            InlineBox.FontSize = fontPx;
            InlineBox.Foreground = brush;
            InlineBox.CaretBrush = brush;
            InlineBox.MinWidth = fontPx * 2;
            InlineBox.MinHeight = fontPx * 1.25;
            System.Windows.Documents.Block.SetLineHeight(InlineBox, fontPx * 1.25);

            InlinePlaceholder.FontFamily = family;
            InlinePlaceholder.FontSize = fontPx;
            InlinePlaceholder.Foreground = brush;
            InlinePlaceholder.Margin = new Thickness(2, 0, 0, 0);
            InlinePlaceholder.Visibility = string.IsNullOrEmpty(InlineBox.Text) ? Visibility.Visible : Visibility.Collapsed;

            // The text field insets its content by two units on the left.
            Canvas.SetLeft(InlineHost, view.X + (text.X - area.X) / k - 2);
            Canvas.SetTop(InlineHost, view.Y + (text.Y - area.Y) / k);
        }

        private void FinishEditing(bool takeFocus)
        {
            string id = _editingId;
            if (id == null) return;
            _editingId = null;
            _coalesceKey = null;
            InlineHost.Visibility = Visibility.Collapsed;

            var target = Find(id) as TextAnnotation;
            bool empty = target == null || string.IsNullOrWhiteSpace(target.Text);
            PlacedText placed = _placed;
            _placed = null;

            if (empty)
            {
                bool placedNow = placed != null && placed.Id == id;
                if (placedNow && placed.HistoryIndex >= 0 && placed.HistoryIndex < _history.Count)
                {
                    // A box that never received any text leaves no trace, not
                    // even in undo.
                    Restore(_history[placed.HistoryIndex]);
                    _history.RemoveRange(placed.HistoryIndex, _history.Count - placed.HistoryIndex);
                }
                else if (placedNow)
                {
                    Remove(id);
                }
                else
                {
                    Push();
                    Remove(id);
                }
                _selectedId = null;
            }

            if (takeFocus) Focus();
            Refresh();
        }

        private void InlineBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing || _editingId == null) return;
            string id = _editingId;
            Push("text:" + id);
            var text = Detach(id) as TextAnnotation;
            if (text == null) return;
            text.Text = InlineBox.Text.Replace("\r\n", "\n");
            UpdateBars();
            LayoutInline();
        }

        private void InlineBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Enter is a new line. Escape, or clicking elsewhere, finishes.
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            FinishEditing(true);
        }

        private void InlineBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            FinishEditing(false);
        }

        // ------------------------------------------------------------------
        // Changes to the selection and the tool setup
        // ------------------------------------------------------------------

        private void UpdateSelected(Action<Annotation> change, string coalesceKey = null)
        {
            if (_selectedId == null || Find(_selectedId) == null) return;
            Push(coalesceKey);
            change(Detach(_selectedId));
        }

        private void SetTool(string next)
        {
            // The highlighter and the marker are pens, so they start yellow, and
            // leaving them returns to the normal annotation colour.
            bool nextPen = next == "highlight" || next == "marker";
            bool currentPen = _tool == "highlight" || _tool == "marker";
            if (nextPen && !currentPen) _color = HighlightColor;
            else if (!nextPen && currentPen) _color = DefaultColor;
            bool wasCropping = _cropDraft.HasValue;
            _tool = next;
            if (next == "crop")
            {
                BeginCrop();
            }
            else if (wasCropping)
            {
                // Picking another tool closes the frame without cropping.
                _cropDraft = null;
                _zoom = null;
            }
        }

        private void Undo()
        {
            if (Cropping)
            {
                CancelCrop();
                return;
            }
            if (_history.Count == 0) return;
            Snapshot last = _history[_history.Count - 1];
            _history.RemoveAt(_history.Count - 1);
            Restore(last);
            _selectedId = null;
            _coalesceKey = null;
            Refresh();
        }

        private void RemoveSelected()
        {
            if (_selectedId == null) return;
            Push();
            Remove(_selectedId);
            _selectedId = null;
            Refresh();
        }

        private void Tool_Click(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            FinishEditing(true);
            var radio = sender as RadioButton;
            if (radio == null) return;
            string next = radio.Tag as string ?? "select";
            if (next == "crop" && Cropping)
            {
                Refresh();
                return;
            }
            SetTool(next);
            // Picking a drawing tool lets go of the selection, so the options
            // row shows the settings of the tool and nothing else.
            if (next != "select") _selectedId = null;
            Persist();
            Refresh();
        }

        private void Swatch_Click(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            FinishEditing(true);
            var radio = sender as RadioButton;
            if (radio == null) return;
            string value = radio.Tag as string ?? DefaultColor;
            _color = value;
            UpdateSelected(delegate(Annotation a) { a.Color = value; });
            Persist();
            Refresh();
        }

        private void WidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            double value = Math.Round(e.NewValue);
            _width = value;
            UpdateSelected(
                delegate(Annotation a)
                {
                    a.Width = value;
                    var step = a as StepAnnotation;
                    if (step != null) step.Radius = StepAnnotation.RadiusFor(value);
                },
                "width:" + _selectedId);
            Refresh();
        }

        private void StepBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            int parsed;
            if (!int.TryParse(StepBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) parsed = 1;
            int number = Math.Max(1, parsed);
            if (Selected is StepAnnotation)
                UpdateSelected(delegate(Annotation a) { ((StepAnnotation)a).Number = number; }, "step:" + _selectedId);
            else
                _nextStep = number;
            Refresh();
        }

        private void SizeBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_syncing) return;
            int parsed;
            if (!int.TryParse(SizeBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) parsed = 10;
            int size = Math.Max(10, Math.Min(200, parsed));
            _fontSize = size;
            if (Selected is TextAnnotation)
                UpdateSelected(delegate(Annotation a) { ((TextAnnotation)a).Size = size; }, "size:" + _selectedId);
            Refresh();
        }

        /// <summary>Up and Down change a number field, Enter leaves it.</summary>
        private void NumberBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var box = sender as TextBox;
            if (box == null) return;
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                Focus();
                return;
            }
            if (e.Key != Key.Up && e.Key != Key.Down) return;
            e.Handled = true;
            int value;
            if (!int.TryParse(box.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value)) value = 1;
            int step = box == SizeBox ? 2 : 1;
            value += e.Key == Key.Up ? step : -step;
            box.Text = Math.Max(1, value).ToString(CultureInfo.InvariantCulture);
            box.CaretIndex = box.Text.Length;
        }

        private void OptionBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            UpdateBars();
        }

        private void FontCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_syncing) return;
            string value = FontCombo.SelectedItem as string;
            if (value == null) return;
            _font = value;
            if (Selected is TextAnnotation)
                UpdateSelected(delegate(Annotation a) { ((TextAnnotation)a).Font = value; });
            Persist();
            Refresh();
        }

        private void OutlineCheck_Click(object sender, RoutedEventArgs e)
        {
            if (_syncing) return;
            FinishEditing(true);
            bool value = OutlineCheck.IsChecked == true;
            _outline = value;
            if (Selected is TextAnnotation)
                UpdateSelected(delegate(Annotation a) { ((TextAnnotation)a).Outline = value; });
            Persist();
            Refresh();
        }

        /// <summary>Brings the whole screenshot back. Like any other change, it can be undone.</summary>
        private void UndoCrop_Click(object sender, RoutedEventArgs e)
        {
            FinishEditing(true);
            if (!_crop.HasValue || Cropping) return;
            Push();
            _crop = null;
            _zoom = null;
            Refresh();
        }

        private void Undo_Click(object sender, RoutedEventArgs e)
        {
            FinishEditing(true);
            Undo();
        }

        private void DeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            FinishEditing(true);
            RemoveSelected();
        }

        // ------------------------------------------------------------------
        // Saving, copying, leaving
        // ------------------------------------------------------------------

        /// <summary>The image as it looks now, annotations and crop included, saved or not.</summary>
        private BitmapSource RenderCurrent()
        {
            FinishEditing(false);
            if (Cropping) CommitCrop();
            return AnnotationRenderer.Render(_image, Area, _items);
        }

        /// <summary>Flattens the annotations into the screenshot file. False when writing failed.</summary>
        private bool Save()
        {
            if (_image == null || _shot == null) return false;
            BitmapSource result = RenderCurrent();
            byte[] png = Bitmaps.EncodePng(result);
            BitmapSource thumb = Bitmaps.MakeThumb(result);
            try
            {
                // Through a temporary file, so a failure half way never leaves a
                // damaged screenshot where the good one was.
                string temporary = _shot.FilePath + ".tmp";
                File.WriteAllBytes(temporary, png);
                File.Move(temporary, _shot.FilePath, true);
                if (!string.IsNullOrEmpty(_shot.ThumbPath)) Bitmaps.SaveThumb(thumb, _shot.ThumbPath);
            }
            catch (Exception error)
            {
                ShowNotice("Could not save the changes: " + error.Message);
                return false;
            }

            _shot.Width = result.PixelWidth;
            _shot.Height = result.PixelHeight;
            _shot.Size = png.Length;
            _shot.Thumb = thumb;

            Persist();
            Action<Shot> saved = Saved;
            if (saved != null) saved(_shot);

            // The annotations are part of the image now, so the editor starts
            // over on the saved result, looking at the same spot as before.
            double? zoom = _zoom;
            double panX = _panX;
            double panY = _panY;
            Open(_shot, _index, _total);
            _zoom = zoom;
            _panX = panX;
            _panY = panY;
            ClampPan();
            Refresh();
            return true;
        }

        private void CopyCurrent()
        {
            if (_image == null) return;
            bool ok = Bitmaps.CopyToClipboard(RenderCurrent());
            ShowNotice(ok ? "Copied to the clipboard." : "Could not copy the image.");
        }

        private void SaveCurrentAs()
        {
            if (_image == null || _shot == null) return;
            BitmapSource current = RenderCurrent();
            var dialog = new SaveFileDialog
            {
                Title = "Save image",
                FileName = Path.GetFileNameWithoutExtension(_shot.Name) + ".png",
                Filter = "PNG image|*.png|JPG image|*.jpg;*.jpeg",
                AddExtension = true,
                DefaultExt = ".png",
                OverwritePrompt = true,
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;

            string target = dialog.FileName;
            string extension = Path.GetExtension(target).ToLowerInvariant();
            bool jpeg = extension == ".jpg" || extension == ".jpeg";
            if (!jpeg && extension != ".png") target += ".png";
            try
            {
                File.WriteAllBytes(target, jpeg ? Bitmaps.EncodeJpeg(current, 92) : Bitmaps.EncodePng(current));
                ShowNotice("Saved " + target);
            }
            catch (Exception error)
            {
                ShowNotice("Could not save the image: " + error.Message);
            }
            Focus();
        }

        /// <summary>Runs an action that leaves this screenshot, asking first when there are unsaved changes.</summary>
        private void RequestLeave(Action leave)
        {
            FinishEditing(true);
            if (!_dirty || Dialog == null)
            {
                Persist();
                leave();
                return;
            }
            Dialog.Open(
                "Unsaved changes",
                "This screenshot has changes that have not been saved. Leaving now discards them.",
                "Save and continue",
                false,
                delegate
                {
                    // Leaving after a failed save would throw the changes away.
                    if (Save()) leave();
                },
                "Discard changes",
                delegate
                {
                    Persist();
                    leave();
                },
                delegate { Focus(); });
        }

        private void RequestClose()
        {
            RequestLeave(delegate
            {
                Action handler = CloseRequested;
                if (handler != null) handler();
            });
        }

        private void RequestNavigate(int delta)
        {
            int next = _index + delta;
            if (next < 0 || next >= _total) return;
            RequestLeave(delegate
            {
                Action<int> handler = NavigateRequested;
                if (handler != null) handler(delta);
            });
        }

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            RequestNavigate(-1);
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            RequestNavigate(1);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            RequestClose();
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Save();
        }

        private void SaveAs_Click(object sender, RoutedEventArgs e)
        {
            SaveCurrentAs();
        }

        private void Copy_Click(object sender, RoutedEventArgs e)
        {
            CopyCurrent();
        }

        private void Stage_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_image != null) Refresh();
        }

        // ------------------------------------------------------------------
        // Note
        // ------------------------------------------------------------------

        private void LoadNote()
        {
            NoteBox.Text = _shot == null ? "" : (_shot.Note ?? "");
            UpdateNoteHint();
        }

        /// <summary>Stores the note on the screenshot when it differs from what is kept.</summary>
        private void CommitNote()
        {
            if (_shot == null) return;
            string text = CaptionText.Clean(NoteBox.Text);
            if (text == (_shot.Note ?? "")) return;
            _shot.Note = text;
            Action<Shot> handler = NoteChanged;
            if (handler != null) handler(_shot);
        }

        private void UpdateNoteHint()
        {
            int length = NoteBox.Text.Length;
            NotePlaceholder.Visibility = length == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoteCountText.Text = length + " / " + CaptionText.MaxNoteLength;
        }

        private void NoteBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            UpdateNoteHint();
        }

        private void NoteBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Escape leaves the field instead of closing the editor, so a note
            // being typed is never lost to a reflex.
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                CommitNote();
                Focus();
            }
        }

        private void NoteBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            CommitNote();
        }

        private void Editor_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_image == null) return;
            if (Dialog != null && Dialog.IsOpen) return;

            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.S)
            {
                e.Handled = true;
                if (_dirty) Save();
                return;
            }

            // Fields keep their own keys: arrows move a caret, a selection or a
            // list, Delete removes a character.
            object source = e.OriginalSource;
            if (source is TextBox || source is ComboBox || source is ComboBoxItem) return;

            if (Cropping && (e.Key == Key.Enter || e.Key == Key.Escape))
            {
                // Enter keeps the framed area, Escape closes the frame. Neither
                // leaves the editor.
                e.Handled = true;
                if (e.Key == Key.Enter) CommitCrop();
                else CancelCrop();
                return;
            }

            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                RequestClose();
            }
            else if (e.Key == Key.Delete || e.Key == Key.Back)
            {
                e.Handled = true;
                RemoveSelected();
            }
            else if (ctrl && e.Key == Key.Z)
            {
                e.Handled = true;
                Undo();
            }
            else if (ctrl && e.Key == Key.C)
            {
                e.Handled = true;
                CopyCurrent();
            }
            else if (ctrl && (e.Key == Key.D0 || e.Key == Key.NumPad0))
            {
                e.Handled = true;
                SetZoom(null, SurfaceCentre);
            }
            else if (ctrl && (e.Key == Key.D1 || e.Key == Key.NumPad1))
            {
                e.Handled = true;
                SetZoom(1.0, SurfaceCentre);
            }
            else if (ctrl && (e.Key == Key.OemPlus || e.Key == Key.Add))
            {
                e.Handled = true;
                ZoomBy(ZoomStep, SurfaceCentre);
            }
            else if (ctrl && (e.Key == Key.OemMinus || e.Key == Key.Subtract))
            {
                e.Handled = true;
                ZoomBy(1 / ZoomStep, SurfaceCentre);
            }
            else if (e.Key == Key.Right)
            {
                e.Handled = true;
                RequestNavigate(1);
            }
            else if (e.Key == Key.Left)
            {
                e.Handled = true;
                RequestNavigate(-1);
            }
        }
    }
}
