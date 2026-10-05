using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;
using Proofly.Core;
using Proofly.Models;
using Proofly.Services;
using Proofly.Views;

namespace Proofly
{
    public partial class MainWindow : Window
    {
        private enum RegionPurpose
        {
            Shot,
            Record,
        }

        /// <summary>An entry of a picker: a stored key and the text shown for it.</summary>
        private sealed class Option
        {
            public string Key;
            public string Label;

            public override string ToString()
            {
                return Label;
            }
        }

        /// <summary>An entry of the session picker.</summary>
        private sealed class SessionChoice
        {
            public SessionData Session;
            public string Label;

            public override string ToString()
            {
                return Label;
            }
        }

        /// <summary>A deleted item and where it sat, kept until the next deletion.</summary>
        private sealed class TrashEntry
        {
            public Shot Shot;
            public int Index;
        }

        /// <summary>
        /// A region selected once and reused by every following capture, so the
        /// same part of the screen can be captured repeatedly without reselecting it.
        /// </summary>
        private sealed class LockedRegion
        {
            public Int32Rect Rect;
            public string DeviceName;
        }

        private static readonly Option[] AudioOptions =
        {
            new Option { Key = "none", Label = "No audio" },
            new Option { Key = "mic", Label = "Microphone" },
            new Option { Key = "system", Label = "System audio" },
            new Option { Key = "both", Label = "Microphone and system" },
        };

        private static readonly Option[] FormatOptions =
        {
            new Option { Key = "pdf", Label = "PDF" },
            new Option { Key = "docx", Label = "Word (.docx)" },
        };

        /// <summary>Name of the data a dragged gallery card carries.</summary>
        private const string DragFormat = "ProoflyShot";

        /// <summary>A screenshot on its way into a document.</summary>
        private sealed class PageSource
        {
            public string Path;
            public int Width;
            public int Height;
        }

        private readonly ObservableCollection<Shot> _shots = new ObservableCollection<Shot>();
        private readonly string _sessionRoot = SessionStore.DefaultRoot;
        private List<SessionData> _sessions = new List<SessionData>();
        private SessionData _session;
        private string _sessionListSignature = "";
        private Shot _pressed;
        private Point _pressPoint;
        private readonly List<TrashEntry> _trash = new List<TrashEntry>();
        private readonly DispatcherTimer _recordingTimer;
        private List<DisplayInfo> _displays = new List<DisplayInfo>();
        private List<string> _failedShortcuts = new List<string>();
        private LockedRegion _locked;
        private HotkeyManager _hotkeys;
        private TrayIcon _tray;
        private RecordingService _recorder;
        private ClickHighlighter _clicks;
        private BitmapSource _recordingThumb;
        private Shot _editing;
        private IntPtr _handle;
        private string _lastDir;
        private bool _busy;
        private bool _quitting;

        /// <summary>True only when this window hid itself in order to take a screenshot.</summary>
        private bool _hiddenByCapture;

        /// <summary>True while the code is filling the controls, so their events are not user input.</summary>
        private bool _loading = true;

        public MainWindow()
        {
            InitializeComponent();

            GalleryList.ItemsSource = _shots;
            VersionText.Text = "v" + App.Version;

            _recordingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _recordingTimer.Tick += delegate { UpdateRecordingClock(); };

            foreach (Option option in AudioOptions) AudioCombo.Items.Add(option);
            foreach (RecordingQuality quality in RecordingQuality.All) QualityCombo.Items.Add(quality);
            foreach (Option option in FormatOptions) FormatCombo.Items.Add(option);

            Editor.Dialog = Confirm;
            Editor.Saved += delegate
            {
                SaveSession();
                SetStatus("Annotations saved.");
            };
            Editor.CloseRequested += delegate
            {
                Editor.Hide();
                _editing = null;
            };
            Editor.NavigateRequested += NavigateEditor;

            Player.CloseRequested += delegate { Player.Hide(); };

            ShortcutsPanel.CloseRequested += delegate { ShortcutsPanel.Hide(); };
            ShortcutsPanel.SuspendHotkeys = delegate { if (_hotkeys != null) _hotkeys.Suspend(); };
            ShortcutsPanel.ResumeHotkeys = delegate
            {
                _failedShortcuts = _hotkeys == null ? new List<string>() : _hotkeys.Resume();
                return _failedShortcuts;
            };
            ShortcutsPanel.ApplyBindings = ApplyShortcuts;

            PreviewKeyDown += Window_PreviewKeyDown;
            Application.Current.SessionEnding += delegate { _quitting = true; };
        }

        private static AppSettings Settings
        {
            get { return App.Settings; }
        }

        // ------------------------------------------------------------------
        // Start, tray and window behaviour
        // ------------------------------------------------------------------

        /// <summary>
        /// Brings the app to life. With <paramref name="hidden"/> the window
        /// stays out of sight and only the tray icon and the shortcuts are active.
        /// </summary>
        public void Start(bool hidden)
        {
            // The handle is needed for the global shortcuts even when the
            // window is never shown.
            _handle = new WindowInteropHelper(this).EnsureHandle();
            HwndSource source = HwndSource.FromHwnd(_handle);
            if (source != null) source.AddHook(WindowMessage);
            _hotkeys = new HotkeyManager(_handle);

            LoadSettingsIntoControls();
            RefreshDisplays();
            ApplyShortcuts(Settings.Shortcuts);
            LoadSessions();

            Stream icon = null;
            try
            {
                icon = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/icon.ico")).Stream;
            }
            catch (Exception)
            {
                // The tray falls back to a stock icon.
            }
            _tray = new TrayIcon(icon);
            _tray.OpenRequested += ShowFromTray;
            _tray.RegionRequested += delegate { OnShortcut(AppSettings.ActionRegion); };
            _tray.QuitRequested += Quit;

            ThemeManager.ApplyTitleBar(this);
            UpdateUi();
            _loading = false;

            if (!hidden) Show();
        }

        private void LoadSettingsIntoControls()
        {
            bool dark = ThemeManager.Current == ThemeManager.Dark;
            ThemeToggle.IsChecked = dark;
            ThemeToggle.Content = dark ? "Dark" : "Light";

            HideCheck.IsChecked = Settings.HideOnCapture;
            CursorCheck.IsChecked = Settings.IncludeCursor;
            ClicksCheck.IsChecked = Settings.HighlightClicks;
            NewSessionCheck.IsChecked = Settings.NewSessionAfterExport;
            FormatCombo.SelectedItem = FormatOptions.FirstOrDefault(o => o.Key == Settings.ExportFormat) ?? FormatOptions[0];
            CompressCheck.IsChecked = Settings.CompressPdf;
            FolderCheck.IsChecked = Settings.UseSaveDir;
            AutostartCheck.IsChecked = Autostart.IsEnabled();

            AudioCombo.SelectedItem = AudioOptions.FirstOrDefault(o => o.Key == Settings.AudioSource) ?? AudioOptions[0];
            QualityCombo.SelectedItem = RecordingQuality.Find(Settings.Quality);
        }

        public void ShowFromTray()
        {
            if (!IsVisible) Show();
            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Activate();
        }

        /// <summary>
        /// The X hides the window from the taskbar and leaves the app in the
        /// tray, where Print Screen keeps working. Minimize is untouched.
        /// </summary>
        protected override void OnClosing(CancelEventArgs e)
        {
            if (!_quitting)
            {
                e.Cancel = true;
                _hiddenByCapture = false;
                Hide();
                if (_tray != null) _tray.ShowStillRunningNotice();
            }
            base.OnClosing(e);
        }

        private void Quit()
        {
            _quitting = true;
            try
            {
                if (_recorder != null && _recorder.IsRecording) _recorder.Stop();
            }
            catch (Exception)
            {
                // Quitting anyway.
            }
            if (Player.IsOpen) Player.Hide();
            if (_clicks != null) _clicks.Stop();
            DropTrash();
            SaveSession();
            if (_hotkeys != null) _hotkeys.Dispose();
            if (_tray != null) _tray.Dispose();
            Settings.Save();
            Application.Current.Shutdown();
        }

        private IntPtr WindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == Native.WmHotkey)
            {
                string action = _hotkeys == null ? null : _hotkeys.ActionFor(wParam.ToInt32());
                if (action != null)
                {
                    handled = true;
                    // Out of the message handler first, so a slow action never
                    // blocks the window procedure.
                    Dispatcher.BeginInvoke(new Action(delegate { OnShortcut(action); }));
                }
            }
            else if (message == Native.WmDisplayChange)
            {
                // Resolution changes, docking and unplugging a monitor all land
                // here, so the display list refreshes without a restart.
                Dispatcher.BeginInvoke(new Action(RefreshDisplays), DispatcherPriority.Background);
            }
            return IntPtr.Zero;
        }

        private bool AnyViewOpen
        {
            get { return Editor.IsOpen || Player.IsOpen || ShortcutsPanel.IsOpen || Confirm.IsOpen; }
        }

        private void OnShortcut(string action)
        {
            // Global shortcuts are ignored while another view owns the window.
            if (AnyViewOpen) return;
            if (action == AppSettings.ActionCapture)
            {
                Run(CaptureFullAsync());
            }
            else if (action == AppSettings.ActionRegion)
            {
                Run(OpenRegionAsync(RegionPurpose.Shot, true));
            }
            else if (action == AppSettings.ActionRecord)
            {
                if (IsRecording) StopRecording();
                else StartRecording(SelectedDisplay, null);
            }
            else if (action == AppSettings.ActionPause)
            {
                TogglePause();
            }
            else if (action == AppSettings.ActionExport)
            {
                RunExport();
            }
        }

        /// <summary>Starts a task from an event handler and reports anything it throws.</summary>
        private async void Run(Task task)
        {
            try
            {
                await task;
            }
            catch (Exception error)
            {
                SetBusy(false);
                SetStatus("Something went wrong: " + error.Message);
            }
        }

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (AnyViewOpen) return;
            if (e.OriginalSource is TextBox) return;
            bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
            if (ctrl && e.Key == Key.Z)
            {
                e.Handled = true;
                RestoreTrash();
            }
            else if (ctrl && e.Key == Key.V)
            {
                e.Handled = true;
                Run(PasteAsync());
            }
        }

        // ------------------------------------------------------------------
        // State shown in the window
        // ------------------------------------------------------------------

        private int ImageCount
        {
            get { return _shots.Count(s => !s.IsVideo); }
        }

        private int VideoCount
        {
            get { return _shots.Count(s => s.IsVideo); }
        }

        private bool IsRecording
        {
            get { return _recorder != null && _recorder.IsRecording; }
        }

        private DisplayInfo SelectedDisplay
        {
            get
            {
                var picked = DisplayCombo.SelectedItem as DisplayInfo;
                if (picked != null) return picked;
                return _displays.FirstOrDefault(d => d.Primary) ?? _displays.FirstOrDefault();
            }
        }

        private void SetStatus(string text)
        {
            StatusText.Text = text;
        }

        public void ReportError(string text)
        {
            _busy = false;
            SetStatus(text);
            UpdateUi();
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            UpdateUi();
        }

        /// <summary>Brings every control in line with the current state.</summary>
        private void UpdateUi()
        {
            int images = ImageCount;
            int videos = VideoCount;
            bool any = _shots.Count > 0;
            bool recording = IsRecording;
            bool locked = _locked != null;
            DisplayInfo source = SelectedDisplay;

            ShotCountText.Text = images + (images == 1 ? " screenshot" : " screenshots");
            ClipCountText.Text = videos + (videos == 1 ? " recording" : " recordings");
            DeleteAllButton.IsEnabled = any;
            EmptyState.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            GalleryScroll.Visibility = any ? Visibility.Visible : Visibility.Collapsed;

            string captureKey = Settings.Shortcut(AppSettings.ActionCapture);
            string recordKey = Settings.Shortcut(AppSettings.ActionRecord);

            CaptureFullButton.Content = locked ? "Locked region" : "Full screen";
            CaptureFullButton.Tag = captureKey;
            CaptureFullButton.IsEnabled = !_busy;
            CaptureRegionButton.Tag = Settings.Shortcut(AppSettings.ActionRegion);
            CaptureRegionButton.IsEnabled = !_busy;
            LockHint.Visibility = locked ? Visibility.Collapsed : Visibility.Visible;
            LockButton.IsEnabled = !_busy;
            LockedRow.Visibility = locked ? Visibility.Visible : Visibility.Collapsed;
            if (locked) LockedText.Text = "Locked " + _locked.Rect.Width + " x " + _locked.Rect.Height;

            RecordFullButton.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
            RecordFullButton.Tag = recordKey;
            RecordFullButton.IsEnabled = !_busy;
            RecordRegionButton.Visibility = recording ? Visibility.Collapsed : Visibility.Visible;
            RecordRegionButton.IsEnabled = !_busy;
            RecordStopButton.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
            RecordStopButton.Tag = recordKey;
            bool paused = recording && _recorder.IsPaused;
            RecordPauseButton.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
            RecordPauseButton.Content = paused ? "Resume" : "Pause";
            RecordPauseButton.Tag = Settings.Shortcut(AppSettings.ActionPause);
            PasteButton.IsEnabled = !_busy;
            AudioCombo.IsEnabled = !recording;
            QualityCombo.IsEnabled = !recording;

            FolderRow.Visibility = Settings.UseSaveDir ? Visibility.Visible : Visibility.Collapsed;
            FolderPathText.Text = string.IsNullOrEmpty(Settings.SaveDir) ? "No folder selected" : Settings.SaveDir;
            FolderPathText.ToolTip = string.IsNullOrEmpty(Settings.SaveDir) ? null : Settings.SaveDir;

            // Kept short so it never collides with the shortcut badge.
            ExportButton.Content = images == 0 && videos > 0
                ? "Save recordings"
                : (Settings.ExportFormat == "docx" ? "Save Word" : "Save PDF");
            ExportButton.Tag = Settings.Shortcut(AppSettings.ActionExport);
            ExportButton.IsEnabled = !_busy && any;
            ExportVideosHint.Visibility = images > 0 && videos > 0 ? Visibility.Visible : Visibility.Collapsed;
            ExportVideosButton.IsEnabled = !_busy;
            OpenFolderButton.Visibility = _lastDir == null ? Visibility.Collapsed : Visibility.Visible;

            EmptyCaptureButton.Content = locked ? "Capture locked region" : "Capture full screen";
            EmptyCaptureButton.Tag = captureKey;
            EmptyCaptureButton.IsEnabled = !_busy;
            EmptyRecordButton.Tag = recordKey;
            EmptyRecordButton.IsEnabled = !_busy && !recording;
            EmptyDisplayText.Text = source == null
                ? "No display found"
                : source.Name + " " + source.Bounds.Width + " x " + source.Bounds.Height;
            var audio = AudioCombo.SelectedItem as Option;
            EmptyAudioText.Text = audio == null ? "No audio" : audio.Label;
            var quality = QualityCombo.SelectedItem as RecordingQuality;
            EmptyQualityText.Text = quality == null ? "Medium" : quality.Label;

            UndoDeleteButton.Visibility = _trash.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            StatusText.SetResourceReference(TextBlock.ForegroundProperty, _busy ? "Text" : "Muted");

            RecLive.Visibility = recording ? Visibility.Visible : Visibility.Collapsed;
            StateDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, recording ? "Danger" : "Accent");
            Pulse(StateDot, recording && !paused);
            Pulse(RecLiveDot, recording && !paused);
            if (recording) UpdateRecordingClock();

            // Sessions cannot change under a running recording, which is being
            // written into the current one.
            bool sessionFree = !_busy && !recording;
            SessionCombo.IsEnabled = sessionFree;
            NewSessionButton.IsEnabled = sessionFree;
            RenameSessionButton.IsEnabled = sessionFree && _session != null;
            DeleteSessionButton.IsEnabled = sessionFree && _session != null;
            RefreshSessionList();
        }

        /// <summary>The slow blink of the recording indicators.</summary>
        private static void Pulse(UIElement element, bool on)
        {
            bool running = element.HasAnimatedProperties;
            if (on && !running)
            {
                var blink = new DoubleAnimation(1.0, 0.25, new Duration(TimeSpan.FromMilliseconds(700)))
                {
                    AutoReverse = true,
                    RepeatBehavior = RepeatBehavior.Forever,
                };
                element.BeginAnimation(OpacityProperty, blink);
            }
            else if (!on && running)
            {
                element.BeginAnimation(OpacityProperty, null);
            }
        }

        private void UpdateRecordingClock()
        {
            if (!IsRecording) return;
            string elapsed = Files.FormatDuration((long)_recorder.Elapsed.TotalMilliseconds);
            RecordStopButton.Content = "Stop " + elapsed;
            RecLiveText.Text = (_recorder.IsPaused ? "Paused " : "Recording ") + elapsed;
        }

        // ------------------------------------------------------------------
        // Displays
        // ------------------------------------------------------------------

        /// <summary>
        /// Rebuilds the display list. The current pick is matched again by its
        /// device name, which survives resolution changes.
        /// </summary>
        private void RefreshDisplays()
        {
            bool wasLoading = _loading;
            _loading = true;
            try
            {
                var previous = DisplayCombo.SelectedItem as DisplayInfo;
                string wanted = previous != null ? previous.DeviceName : Settings.Display;

                _displays = Displays.List();
                DisplayCombo.Items.Clear();
                foreach (DisplayInfo display in _displays) DisplayCombo.Items.Add(display);

                DisplayInfo match =
                    _displays.FirstOrDefault(d => string.Equals(d.DeviceName, wanted, StringComparison.OrdinalIgnoreCase)) ??
                    _displays.FirstOrDefault(d => d.Primary) ??
                    _displays.FirstOrDefault();
                DisplayCombo.SelectedItem = match;

                // A locked region belongs to one display layout.
                if (_locked != null)
                {
                    DisplayInfo owner = _displays.FirstOrDefault(d => d.DeviceName == _locked.DeviceName);
                    if (owner == null ||
                        _locked.Rect.X + _locked.Rect.Width > owner.Bounds.Width ||
                        _locked.Rect.Y + _locked.Rect.Height > owner.Bounds.Height)
                        _locked = null;
                }
            }
            catch (Exception error)
            {
                SetStatus("Could not read the list of screens: " + error.Message);
            }
            finally
            {
                _loading = wasLoading;
            }
            UpdateUi();
        }

        private void DisplayCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var picked = DisplayCombo.SelectedItem as DisplayInfo;
            if (picked == null) return;
            Settings.Display = picked.DeviceName;
            Settings.Save();
            UpdateUi();
        }

        // ------------------------------------------------------------------
        // Hiding the window around a capture
        // ------------------------------------------------------------------

        /// <summary>
        /// Takes the window off the screen so it does not appear in the
        /// screenshot. Pass null to hide it whatever display it is on.
        /// </summary>
        private async Task HideForCaptureAsync(DisplayInfo target)
        {
            _hiddenByCapture = false;
            if (!Settings.HideOnCapture) return;
            // A minimized or tray hidden window is already off screen, hiding it
            // would only force an unwanted restore afterwards.
            if (!IsVisible || WindowState == WindowState.Minimized) return;
            // On a different monitor than the one being captured it cannot show
            // up in the screenshot, so it is left alone.
            if (target != null)
            {
                DisplayInfo own = Displays.OfWindow(_displays, _handle);
                if (own != null && !string.Equals(own.DeviceName, target.DeviceName, StringComparison.OrdinalIgnoreCase))
                    return;
            }
            Hide();
            _hiddenByCapture = true;
            // Gives the compositor time to actually remove the window.
            await Task.Delay(220);
        }

        /// <summary>
        /// Brings the window back without taking focus from whatever the user
        /// was working in. A window that was not hidden by a capture is left alone.
        /// </summary>
        private void RestoreAfterCapture()
        {
            if (!_hiddenByCapture) return;
            _hiddenByCapture = false;
            ShowActivated = false;
            Show();
            ShowActivated = true;
        }

        /// <summary>
        /// Going straight to minimized avoids the window flashing up between a
        /// selection and the start of a recording.
        /// </summary>
        private void MinimizeWindow()
        {
            bool wasHiddenByCapture = _hiddenByCapture;
            _hiddenByCapture = false;
            if (IsVisible)
            {
                WindowState = WindowState.Minimized;
            }
            else if (wasHiddenByCapture)
            {
                ShowActivated = false;
                WindowState = WindowState.Minimized;
                Show();
                ShowActivated = true;
            }
        }

        // ------------------------------------------------------------------
        // Screenshots
        // ------------------------------------------------------------------

        /// <summary>
        /// Writes a screenshot into the session folder and shows it in the
        /// gallery. From this point on it survives a crash or a restart.
        /// </summary>
        private async Task AddImageAsync(BitmapSource image)
        {
            SessionData session = _session;
            if (session == null) throw new InvalidOperationException("There is no session to store the screenshot in.");

            var shot = new Shot { Kind = ShotKind.Image };
            string stamp = Files.Stamp();
            string stem = "shot_" + stamp + "_" + shot.Id.Substring(0, 6);
            shot.Name = "screenshot_" + stamp + ".png";
            shot.FilePath = session.PathOf(stem + ".png");
            shot.ThumbPath = session.PathOf(stem + ".thumb.jpg");

            // Encoding a large screenshot takes a moment, so it runs off the
            // interface thread. Frozen bitmaps are safe to read from anywhere.
            long size = 0;
            BitmapSource thumb = null;
            await Task.Run(delegate
            {
                byte[] png = Bitmaps.EncodePng(image);
                File.WriteAllBytes(shot.FilePath, png);
                size = png.Length;
                thumb = Bitmaps.MakeThumb(image);
                Bitmaps.SaveThumb(thumb, shot.ThumbPath);
            });

            shot.Width = image.PixelWidth;
            shot.Height = image.PixelHeight;
            shot.Size = size;
            shot.Thumb = thumb;
            _shots.Add(shot);
            Renumber();
            SaveSession();
            UpdateUi();
        }

        /// <summary>Screenshot of the whole selected screen, or of the locked region.</summary>
        private async Task CaptureFullAsync()
        {
            DisplayInfo source = SelectedDisplay;
            if (source == null || _busy) return;
            SetBusy(true);
            try
            {
                await HideForCaptureAsync(source);
                // A locked region belongs to one display, so it is only applied
                // while that display is the selected source.
                bool useLock = _locked != null && _locked.DeviceName == source.DeviceName;
                Int32Rect area = source.Bounds;
                if (useLock)
                {
                    Int32Rect inside = Bitmaps.ClampTo(_locked.Rect, source.Bounds.Width, source.Bounds.Height);
                    area = new Int32Rect(source.Bounds.X + inside.X, source.Bounds.Y + inside.Y, inside.Width, inside.Height);
                }
                BitmapSource image = Bitmaps.CaptureScreen(area, Settings.IncludeCursor);
                RestoreAfterCapture();
                await AddImageAsync(image);
                // Locked captures are usually taken many times in a row, so they
                // stay out of the clipboard. The editor has Copy for the one
                // that matters.
                SetStatus(useLock
                    ? "Captured the locked region, " + image.PixelWidth + " x " + image.PixelHeight + "."
                    : "Captured " + image.PixelWidth + " x " + image.PixelHeight + ".");
            }
            catch (Exception error)
            {
                SetStatus("Capture failed: " + error.Message);
            }
            finally
            {
                RestoreAfterCapture();
                SetBusy(false);
            }
        }

        /// <summary>Picks the region that every following capture will reuse.</summary>
        private async Task LockRegionAsync()
        {
            DisplayInfo source = SelectedDisplay;
            if (source == null || _busy) return;
            SetBusy(true);
            try
            {
                await HideForCaptureAsync(source);
                BitmapSource frozen = Bitmaps.CaptureScreen(source.Bounds);
                Int32Rect? rect = await RegionOverlayWindow.PickAsync(source, frozen);
                RestoreAfterCapture();
                if (!rect.HasValue)
                {
                    SetStatus("Region lock cancelled.");
                    return;
                }
                _locked = new LockedRegion { Rect = rect.Value, DeviceName = source.DeviceName };
                SetStatus("Locked " + rect.Value.Width + " x " + rect.Value.Height +
                          ". Full screen captures now take this area.");
            }
            catch (Exception error)
            {
                SetStatus("Could not lock the region: " + error.Message);
            }
            finally
            {
                RestoreAfterCapture();
                SetBusy(false);
            }
        }

        /// <summary>
        /// Selecting a region happens on the screen itself, in a frozen full
        /// screen overlay. A shortcut targets the display under the pointer, a
        /// button targets the display chosen in the sidebar.
        /// </summary>
        private async Task OpenRegionAsync(RegionPurpose purpose, bool fromShortcut)
        {
            DisplayInfo source = SelectedDisplay;
            if (source == null || _busy) return;
            SetBusy(true);
            try
            {
                DisplayInfo target = source;
                if (fromShortcut && purpose == RegionPurpose.Shot)
                    target = Displays.UnderPointer(_displays) ?? source;

                await HideForCaptureAsync(fromShortcut ? null : source);
                bool withPointer = Settings.IncludeCursor && purpose == RegionPurpose.Shot;
                BitmapSource frozen = Bitmaps.CaptureScreen(target.Bounds, withPointer);
                Int32Rect? rect = await RegionOverlayWindow.PickAsync(target, frozen);
                if (!rect.HasValue)
                {
                    RestoreAfterCapture();
                    SetStatus("Region selection cancelled.");
                    return;
                }

                if (purpose == RegionPurpose.Record)
                {
                    if (Settings.HideOnCapture) MinimizeWindow();
                    else RestoreAfterCapture();
                    SetBusy(false);
                    StartRecording(target, rect.Value);
                    return;
                }

                BitmapSource cropped = Bitmaps.Crop(frozen, rect.Value);
                // Straight into the clipboard, so a region can be pasted anywhere at once.
                bool copied = Bitmaps.CopyToClipboard(cropped);
                RestoreAfterCapture();
                await AddImageAsync(cropped);
                SetStatus("Region " + cropped.PixelWidth + " x " + cropped.PixelHeight +
                          (copied ? " copied to the clipboard." : " captured. The clipboard was busy, so it was not copied."));
            }
            catch (Exception error)
            {
                SetStatus("Capture failed: " + error.Message);
            }
            finally
            {
                RestoreAfterCapture();
                SetBusy(false);
            }
        }

        // ------------------------------------------------------------------
        // Recording
        // ------------------------------------------------------------------

        private void StartRecording(DisplayInfo display, Int32Rect? region)
        {
            if (display == null || IsRecording || _busy) return;
            try
            {
                if (_recorder == null)
                {
                    _recorder = new RecordingService();
                    _recorder.Completed += delegate(RecordingResult result)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { OnRecordingCompleted(result); }));
                    };
                    _recorder.Failed += delegate(string reason)
                    {
                        Dispatcher.BeginInvoke(new Action(delegate { OnRecordingFailed(reason); }));
                    };
                }

                Int32Rect area = region.HasValue
                    ? new Int32Rect(display.Bounds.X + region.Value.X, display.Bounds.Y + region.Value.Y,
                        region.Value.Width, region.Value.Height)
                    : display.Bounds;
                _recordingThumb = Bitmaps.MakeThumb(Bitmaps.CaptureScreen(area));

                if (_session == null) throw new InvalidOperationException("There is no session to store the recording in.");
                // Straight into the session folder, so a finished recording is
                // already where it has to be.
                string output = _session.PathOf("recording_" + Files.Stamp() + ".mp4");
                RecordingQuality quality = RecordingQuality.Find(Settings.Quality);
                List<string> warnings = _recorder.Start(display, region, quality, Settings.AudioSource, output);
                _recordingTimer.Start();
                if (Settings.HighlightClicks)
                {
                    try
                    {
                        if (_clicks == null) _clicks = new ClickHighlighter();
                        _clicks.Start();
                    }
                    catch (Exception)
                    {
                        // The recording itself is running. It just carries no
                        // click marks.
                        _clicks = null;
                    }
                }
                if (Settings.HideOnCapture) MinimizeWindow();

                string text = region.HasValue ? "Recording a region of the screen." : "Recording the whole screen.";
                SetStatus(warnings.Count > 0 ? text + " " + string.Join(" ", warnings) : text);
            }
            catch (Exception error)
            {
                SetStatus("Could not start recording: " + error.Message +
                          " Recording needs the Visual C++ Redistributable (x64) and Media Foundation.");
            }
            UpdateUi();
        }

        private void StopRecording()
        {
            if (!IsRecording) return;
            try
            {
                SetStatus("Finishing the recording...");
                _recorder.Stop();
            }
            catch (Exception error)
            {
                SetStatus("Could not stop the recording: " + error.Message);
            }
        }

        /// <summary>Holds or continues the running recording.</summary>
        private void TogglePause()
        {
            if (!IsRecording) return;
            try
            {
                if (_recorder.IsPaused)
                {
                    _recorder.Resume();
                    if (_clicks != null && Settings.HighlightClicks) _clicks.Start();
                    SetStatus("Recording resumed.");
                }
                else
                {
                    _recorder.Pause();
                    if (_clicks != null) _clicks.Suspend();
                    SetStatus("Recording paused. Nothing is recorded until you resume.");
                }
            }
            catch (Exception error)
            {
                SetStatus("Could not pause the recording: " + error.Message);
            }
            UpdateUi();
        }

        private void OnRecordingCompleted(RecordingResult result)
        {
            _recordingTimer.Stop();
            if (_clicks != null) _clicks.Stop();
            long size = 0;
            try
            {
                size = new FileInfo(result.FilePath).Length;
            }
            catch (Exception)
            {
                // The size is only a label.
            }

            var shot = new Shot
            {
                Kind = ShotKind.Video,
                Name = Path.GetFileName(result.FilePath),
                FilePath = result.FilePath,
                Width = result.Width,
                Height = result.Height,
                Size = size,
                DurationMs = result.DurationMs,
                HasAudio = result.HasAudio,
                Thumb = _recordingThumb,
            };
            if (_recordingThumb != null)
            {
                try
                {
                    shot.ThumbPath = Path.ChangeExtension(result.FilePath, ".thumb.jpg");
                    Bitmaps.SaveThumb(_recordingThumb, shot.ThumbPath);
                }
                catch (Exception)
                {
                    shot.ThumbPath = "";
                }
            }
            _recordingThumb = null;

            _shots.Add(shot);
            Renumber();
            SaveSession();
            SetStatus("Recording saved, length " + Files.FormatDuration(result.DurationMs) + ".");
            UpdateUi();
        }

        private void OnRecordingFailed(string reason)
        {
            _recordingTimer.Stop();
            if (_clicks != null) _clicks.Stop();
            _recordingThumb = null;
            SetStatus("The recording failed: " + (string.IsNullOrEmpty(reason) ? "unknown error" : reason));
            UpdateUi();
        }

        // ------------------------------------------------------------------
        // Sessions
        // ------------------------------------------------------------------

        /// <summary>
        /// Reads the sessions kept on disk and reopens the one that was current
        /// when the app last closed. This is also what brings the evidence back
        /// after a crash or a restart.
        /// </summary>
        private void LoadSessions()
        {
            try
            {
                _sessions = SessionStore.LoadAll(_sessionRoot);
                SessionData wanted =
                    _sessions.FirstOrDefault(s => Path.GetFileName(s.Folder) == Settings.LastSession) ??
                    _sessions.LastOrDefault();

                // Empty sessions nobody named are leftovers of earlier runs.
                foreach (SessionData stale in _sessions.Where(s => s != wanted && s.Items.Count == 0 && IsDefaultName(s.Name)).ToList())
                {
                    SessionStore.Delete(stale);
                    _sessions.Remove(stale);
                }

                if (wanted == null)
                {
                    wanted = SessionStore.Create(_sessionRoot, SessionStore.NextName(_sessions));
                    _sessions.Add(wanted);
                }
                OpenSession(wanted);
                if (_shots.Count > 0)
                    SetStatus("Restored \"" + DisplayName(wanted) + "\" with " + _shots.Count + " item(s).");
            }
            catch (Exception error)
            {
                SetStatus("Sessions cannot be stored on this computer: " + error.Message);
            }
        }

        private static bool IsDefaultName(string name)
        {
            return SessionStore.DefaultNumber(name) > 0;
        }

        /// <summary>
        /// The name of a session as the user sees it. A lone session that was
        /// never renamed is simply "Session": the number only starts to matter,
        /// and to show, once there is more than one.
        /// </summary>
        private string DisplayName(SessionData session)
        {
            if (session == null) return "";
            if (_sessions.Count <= 1 && IsDefaultName(session.Name)) return "Session";
            return session.Name;
        }

        /// <summary>Shows a session in the gallery and makes it the one captures go into.</summary>
        private void OpenSession(SessionData session)
        {
            DropTrash();
            if (_session != null && _session != session) SaveSession();

            _session = null;
            _shots.Clear();
            SessionStore.RemoveStrayFiles(session);

            foreach (SessionItem item in session.Items)
            {
                var shot = new Shot
                {
                    Id = string.IsNullOrEmpty(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
                    Kind = item.Kind == "video" ? ShotKind.Video : ShotKind.Image,
                    FilePath = session.PathOf(item.File),
                    ThumbPath = string.IsNullOrEmpty(item.Thumb) ? "" : session.PathOf(item.Thumb),
                    Name = item.Name,
                    Width = item.Width,
                    Height = item.Height,
                    Size = item.Size,
                    DurationMs = item.DurationMs,
                    HasAudio = item.HasAudio,
                };
                shot.Thumb = Bitmaps.LoadThumb(shot.ThumbPath) ?? RebuildThumb(shot);
                _shots.Add(shot);
            }

            _session = session;
            Renumber();
            Settings.LastSession = Path.GetFileName(session.Folder);
            Settings.Save();
            UpdateUi();
        }

        /// <summary>A screenshot whose stored preview is missing gets a new one from the image itself.</summary>
        private static BitmapSource RebuildThumb(Shot shot)
        {
            if (shot.IsVideo) return null;
            try
            {
                BitmapSource thumb = Bitmaps.MakeThumb(Bitmaps.Decode(File.ReadAllBytes(shot.FilePath)));
                shot.ThumbPath = Path.ChangeExtension(shot.FilePath, ".thumb.jpg");
                Bitmaps.SaveThumb(thumb, shot.ThumbPath);
                return thumb;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>Writes the list of items of the current session to disk.</summary>
        private void SaveSession()
        {
            if (_session == null) return;
            try
            {
                _session.Items = _shots.Select(shot => new SessionItem
                {
                    Id = shot.Id,
                    Kind = shot.IsVideo ? "video" : "image",
                    File = Path.GetFileName(shot.FilePath),
                    Thumb = string.IsNullOrEmpty(shot.ThumbPath) ? "" : Path.GetFileName(shot.ThumbPath),
                    Name = shot.Name,
                    Width = shot.Width,
                    Height = shot.Height,
                    Size = shot.Size,
                    DurationMs = shot.DurationMs,
                    HasAudio = shot.HasAudio,
                }).ToList();
                SessionStore.Save(_session);
            }
            catch (Exception error)
            {
                SetStatus("Could not save the session: " + error.Message);
            }
        }

        /// <summary>Creates an empty session and makes it current. False when it could not be created.</summary>
        private bool StartNewSession()
        {
            try
            {
                SessionData fresh = SessionStore.Create(_sessionRoot, SessionStore.NextName(_sessions));
                _sessions.Add(fresh);
                OpenSession(fresh);
                return true;
            }
            catch (Exception error)
            {
                SetStatus("Could not start a new session: " + error.Message);
                return false;
            }
        }

        private void DeleteCurrentSession()
        {
            SessionData doomed = _session;
            if (doomed == null) return;
            if (Player.IsOpen) Player.Hide();
            _trash.Clear();
            _shots.Clear();
            _session = null;
            try
            {
                SessionStore.Delete(doomed);
            }
            catch (Exception error)
            {
                SetStatus("Some files of the session could not be removed: " + error.Message);
            }
            _sessions.Remove(doomed);

            SessionData next = _sessions.LastOrDefault();
            if (next != null) OpenSession(next);
            else StartNewSession();
            if (_session != null) SetStatus("Session \"" + doomed.Name + "\" deleted. \"" + DisplayName(_session) + "\" is open.");
        }

        /// <summary>Rebuilds the session picker when a name, a count or the selection changed.</summary>
        private void RefreshSessionList()
        {
            List<SessionChoice> choices = _sessions
                .OrderByDescending(s => s.Created)
                .Select(s => new SessionChoice { Session = s, Label = DisplayName(s) })
                .ToList();
            string signature = string.Join("|", choices.Select(c => c.Label)) + "#" +
                               (_session == null ? "" : _session.Folder);
            if (signature == _sessionListSignature) return;
            _sessionListSignature = signature;

            bool wasLoading = _loading;
            _loading = true;
            SessionCombo.Items.Clear();
            foreach (SessionChoice choice in choices) SessionCombo.Items.Add(choice);
            SessionCombo.SelectedItem = choices.FirstOrDefault(c => c.Session == _session);
            _loading = wasLoading;
        }

        // ------------------------------------------------------------------
        // Order of the screenshots
        // ------------------------------------------------------------------

        /// <summary>
        /// Gives every screenshot the number of the page it will be on.
        /// Recordings are not part of the document and carry no number.
        /// </summary>
        private void Renumber()
        {
            int page = 0;
            foreach (Shot shot in _shots) shot.Order = shot.IsVideo ? 0 : ++page;
        }

        private static bool IsInsideButton(object source)
        {
            var node = source as DependencyObject;
            while (node != null)
            {
                if (node is ButtonBase) return true;
                node = node is Visual ? VisualTreeHelper.GetParent(node) : null;
            }
            return false;
        }

        private void ClearDropHints()
        {
            foreach (Shot shot in _shots) shot.DropHint = 0;
        }

        private void Card_PressStart(object sender, MouseButtonEventArgs e)
        {
            var card = sender as FrameworkElement;
            // The Delete button on a card is a button, not a handle.
            _pressed = card == null || IsInsideButton(e.OriginalSource) ? null : card.DataContext as Shot;
            _pressPoint = e.GetPosition(this);
        }

        private void Card_PressMove(object sender, MouseEventArgs e)
        {
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                _pressed = null;
                return;
            }
            if (_pressed == null) return;
            Point now = e.GetPosition(this);
            // A few units of slack keep an ordinary click from starting a drag.
            if (Math.Abs(now.X - _pressPoint.X) < 6 && Math.Abs(now.Y - _pressPoint.Y) < 6) return;

            Shot dragged = _pressed;
            _pressed = null;
            var source = (UIElement)sender;

            // While it is being dragged the card fades in place and a small copy
            // follows the pointer, so it is obvious that something is moving.
            DragGhostImage.Source = dragged.Thumb;
            DragGhostText.Text = dragged.OrderText;
            DragGhostBadge.Visibility = dragged.HasOrder ? Visibility.Visible : Visibility.Collapsed;
            GiveFeedbackEventHandler follow = delegate(object s, GiveFeedbackEventArgs args)
            {
                MoveDragGhost();
                args.UseDefaultCursors = true;
                args.Handled = true;
            };
            source.GiveFeedback += follow;
            dragged.IsDragging = true;
            MoveDragGhost();
            DragGhost.IsOpen = true;
            try
            {
                DragDrop.DoDragDrop(source, new DataObject(DragFormat, dragged.Id), DragDropEffects.Move);
            }
            finally
            {
                source.GiveFeedback -= follow;
                DragGhost.IsOpen = false;
                DragGhostImage.Source = null;
                dragged.IsDragging = false;
                ClearDropHints();
            }
        }

        /// <summary>Puts the drag preview next to the pointer.</summary>
        private void MoveDragGhost()
        {
            Native.POINT pointer;
            if (!Native.GetCursorPos(out pointer)) return;
            try
            {
                Point local = Shell.PointFromScreen(new Point(pointer.X, pointer.Y));
                DragGhost.HorizontalOffset = local.X + 18;
                DragGhost.VerticalOffset = local.Y + 18;
            }
            catch (InvalidOperationException)
            {
                // The window is not on screen. There is nothing to follow.
            }
        }

        /// <summary>Marks a card for a moment after it landed in a new place.</summary>
        private void FlashMoved(Shot shot)
        {
            foreach (Shot other in _shots) other.IsHighlighted = false;
            shot.IsHighlighted = true;
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1500) };
            timer.Tick += delegate
            {
                timer.Stop();
                shot.IsHighlighted = false;
            };
            timer.Start();
        }

        private Shot DraggedShot(DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(DragFormat)) return null;
            string id = e.Data.GetData(DragFormat) as string;
            return _shots.FirstOrDefault(s => s.Id == id);
        }

        /// <summary>True when the pointer is over the right half of a card.</summary>
        private static bool OverSecondHalf(FrameworkElement card, DragEventArgs e)
        {
            return e.GetPosition(card).X > card.ActualWidth / 2;
        }

        private void Card_DragOver(object sender, DragEventArgs e)
        {
            var card = sender as FrameworkElement;
            Shot target = card == null ? null : card.DataContext as Shot;
            Shot dragged = DraggedShot(e);
            e.Handled = true;
            if (target == null || dragged == null)
            {
                e.Effects = DragDropEffects.None;
                return;
            }
            e.Effects = DragDropEffects.Move;

            int hint = target == dragged ? 0 : (OverSecondHalf(card, e) ? 1 : -1);
            foreach (Shot shot in _shots) shot.DropHint = shot == target ? hint : 0;

            // Near the top or bottom edge the gallery scrolls along.
            double y = e.GetPosition(GalleryScroll).Y;
            if (y < 40) GalleryScroll.ScrollToVerticalOffset(GalleryScroll.VerticalOffset - 18);
            else if (y > GalleryScroll.ActualHeight - 40) GalleryScroll.ScrollToVerticalOffset(GalleryScroll.VerticalOffset + 18);
        }

        private void Card_DragLeave(object sender, DragEventArgs e)
        {
            var card = sender as FrameworkElement;
            Shot target = card == null ? null : card.DataContext as Shot;
            if (target != null) target.DropHint = 0;
        }

        private void Card_Drop(object sender, DragEventArgs e)
        {
            var card = sender as FrameworkElement;
            Shot target = card == null ? null : card.DataContext as Shot;
            Shot dragged = DraggedShot(e);
            e.Handled = true;
            ClearDropHints();
            if (target == null || dragged == null || target == dragged) return;

            int from = _shots.IndexOf(dragged);
            int to = _shots.IndexOf(target) + (OverSecondHalf(card, e) ? 1 : 0);
            // Taking the card out first shifts everything after it by one.
            if (from < to) to--;
            if (from < 0 || to < 0 || from == to) return;

            _shots.Move(from, to);
            Renumber();
            SaveSession();
            FlashMoved(dragged);
            SetStatus(dragged.IsVideo
                ? "Order changed."
                : "Order changed. The screenshot is now page " + dragged.Order + ".");
            UpdateUi();
        }

        // ------------------------------------------------------------------
        // Paste
        // ------------------------------------------------------------------

        /// <summary>
        /// Adds what is on the clipboard to the session: a copied image, or
        /// image files copied in Explorer.
        /// </summary>
        private async Task PasteAsync()
        {
            if (_busy) return;
            SetBusy(true);
            try
            {
                BitmapSource image = Bitmaps.FromClipboard();
                if (image != null)
                {
                    await AddImageAsync(image);
                    SetStatus("Pasted " + image.PixelWidth + " x " + image.PixelHeight + " from the clipboard.");
                    return;
                }

                int added = 0;
                foreach (string path in Bitmaps.ClipboardImageFiles())
                {
                    BitmapSource fromFile = Bitmaps.Decode(File.ReadAllBytes(path));
                    await AddImageAsync(fromFile);
                    added++;
                }
                SetStatus(added > 0
                    ? "Pasted " + added + " image file(s) from the clipboard."
                    : "The clipboard holds no image.");
            }
            catch (Exception error)
            {
                SetStatus("Could not paste: " + error.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        // ------------------------------------------------------------------
        // Gallery
        // ------------------------------------------------------------------

        private List<Shot> Images
        {
            get { return _shots.Where(s => !s.IsVideo).ToList(); }
        }

        private void OpenShot(Shot shot)
        {
            if (shot == null || AnyViewOpen) return;
            if (shot.IsVideo)
            {
                Player.Open(shot);
                return;
            }
            List<Shot> images = Images;
            int position = images.IndexOf(shot);
            if (position < 0) return;
            _editing = shot;
            Editor.Open(shot, position, images.Count);
        }

        private void NavigateEditor(int delta)
        {
            List<Shot> images = Images;
            int position = _editing == null ? -1 : images.IndexOf(_editing);
            int next = position + delta;
            if (position < 0 || next < 0 || next >= images.Count) return;
            _editing = images[next];
            Editor.Open(_editing, next, images.Count);
        }

        private static void DeleteFileOf(Shot shot)
        {
            foreach (string path in new[] { shot.FilePath, shot.ThumbPath })
            {
                if (string.IsNullOrEmpty(path)) continue;
                try
                {
                    File.Delete(path);
                }
                catch (Exception)
                {
                    // Still open somewhere. Stray files are removed the next
                    // time the session is opened.
                }
            }
        }

        /// <summary>Frees whatever is in the trash. Called when the trash is replaced.</summary>
        private void DropTrash()
        {
            foreach (TrashEntry entry in _trash) DeleteFileOf(entry.Shot);
            _trash.Clear();
        }

        // A deletion is recoverable: the material waits in a one step trash
        // instead of being freed straight away.
        private void DeleteShot(Shot shot)
        {
            int index = _shots.IndexOf(shot);
            if (index < 0) return;
            DropTrash();
            _trash.Add(new TrashEntry { Shot = shot, Index = index });
            _shots.RemoveAt(index);
            Renumber();
            SaveSession();
            SetStatus("Deleted. Ctrl+Z restores it.");
            UpdateUi();
        }

        private void RestoreTrash()
        {
            if (_trash.Count == 0) return;
            foreach (TrashEntry entry in _trash.OrderBy(t => t.Index))
                _shots.Insert(Math.Min(entry.Index, _shots.Count), entry.Shot);
            _trash.Clear();
            Renumber();
            SaveSession();
            SetStatus("Restored.");
            UpdateUi();
        }

        private void DeleteAll()
        {
            DropTrash();
            for (int i = 0; i < _shots.Count; i++) _trash.Add(new TrashEntry { Shot = _shots[i], Index = i });
            _shots.Clear();
            SaveSession();
            SetStatus("Session cleared. Ctrl+Z restores it.");
            UpdateUi();
        }

        private void Card_Open(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element != null) OpenShot(element.DataContext as Shot);
        }

        private void Card_Delete(object sender, RoutedEventArgs e)
        {
            var element = sender as FrameworkElement;
            var shot = element == null ? null : element.DataContext as Shot;
            if (shot != null) DeleteShot(shot);
        }

        private void DeleteAll_Click(object sender, RoutedEventArgs e)
        {
            if (_shots.Count == 0) return;
            Confirm.Open(
                "Delete everything",
                "This removes " + ImageCount + " screenshot(s) and " + VideoCount +
                " recording(s) from the session. Anything already exported to disk is untouched.",
                "Delete all",
                true,
                DeleteAll);
        }

        private void UndoDelete_Click(object sender, RoutedEventArgs e)
        {
            RestoreTrash();
        }

        // ------------------------------------------------------------------
        // Export
        // ------------------------------------------------------------------

        /// <summary>The folder exports go to without a dialog, or null when the dialog is needed.</summary>
        private string UsableSaveDir
        {
            get
            {
                if (!Settings.UseSaveDir || string.IsNullOrEmpty(Settings.SaveDir)) return null;
                return Directory.Exists(Settings.SaveDir) ? Settings.SaveDir : null;
            }
        }

        /// <summary>Writes the screenshots into a PDF or a Word file, one page each.</summary>
        private static void WriteDocument(string path, List<PageSource> pages, bool compress, string title, bool word)
        {
            using (FileStream output = File.Create(path))
            {
                if (word)
                {
                    DocxWriter.Write(
                        output,
                        pages.Count,
                        delegate(int index)
                        {
                            PageSource page = pages[index];
                            byte[] png = File.ReadAllBytes(page.Path);
                            // Word takes PNG files as they are. JPEG is only
                            // produced when the smaller file is asked for.
                            return new DocxImage
                            {
                                Width = page.Width,
                                Height = page.Height,
                                IsJpeg = compress,
                                Data = compress ? Bitmaps.EncodeJpeg(Bitmaps.Decode(png), 85) : png,
                            };
                        },
                        title);
                    return;
                }

                PdfWriter.Write(
                    output,
                    pages.Count,
                    delegate(int index)
                    {
                        BitmapSource image = Bitmaps.Decode(File.ReadAllBytes(pages[index].Path));
                        return new PdfImage
                        {
                            Width = image.PixelWidth,
                            Height = image.PixelHeight,
                            IsJpeg = compress,
                            Data = compress ? Bitmaps.EncodeJpeg(image, 85) : Bitmaps.ToRgb24(image),
                        };
                    },
                    title);
            }
        }

        private void RunExport()
        {
            if (ImageCount == 0) Run(ExportVideosAsync(true));
            else Run(ExportAllAsync());
        }

        /// <summary>
        /// Marks the session as saved and, when the option is on, makes a fresh
        /// session current. The saved one stays in the list for later additions.
        /// Returns the text to add to the status line.
        /// </summary>
        private string FinishExport()
        {
            if (_session == null) return "";
            _session.Exported = DateTime.Now;
            SaveSession();
            if (!Settings.NewSessionAfterExport) return "";
            string saved = DisplayName(_session);
            return StartNewSession() ? " \"" + saved + "\" is kept in the session list, a new session is open." : "";
        }

        /// <summary>Writes the document and every recording into one folder.</summary>
        private async Task ExportAllAsync()
        {
            if (_shots.Count == 0)
            {
                SetStatus("There is nothing to export.");
                return;
            }
            if (_busy || IsRecording)
            {
                if (IsRecording) SetStatus("Stop the recording before saving.");
                return;
            }
            SetBusy(true);
            try
            {
                List<Shot> images = Images;
                List<Shot> videos = _shots.Where(s => s.IsVideo).ToList();
                bool word = Settings.ExportFormat == "docx";
                string extension = word ? ".docx" : ".pdf";
                string sessionName = _session == null ? "documentation" : DisplayName(_session);
                string defaultName = SessionStore.SafeFileName(sessionName) + extension;

                // A configured folder skips the dialog, but only while it is
                // still usable.
                string folder = UsableSaveDir;
                string documentPath;
                if (folder != null)
                {
                    documentPath = Files.UniquePath(Path.Combine(folder, defaultName));
                }
                else
                {
                    var dialog = new SaveFileDialog
                    {
                        Title = "Save documentation",
                        FileName = defaultName,
                        Filter = word ? "Word document|*.docx" : "PDF|*.pdf",
                        DefaultExt = extension,
                        AddExtension = true,
                        OverwritePrompt = true,
                    };
                    if (dialog.ShowDialog(this) != true)
                    {
                        SetStatus("Export cancelled.");
                        return;
                    }
                    documentPath = dialog.FileName;
                    if (!documentPath.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) documentPath += extension;
                }

                SetStatus("Building the document...");
                string dir = Path.GetDirectoryName(documentPath);
                string stem = Path.GetFileNameWithoutExtension(documentPath);
                bool compress = Settings.CompressPdf;
                List<PageSource> pages = images
                    .Select(s => new PageSource { Path = s.FilePath, Width = s.Width, Height = s.Height })
                    .ToList();
                if (pages.Count > 0)
                    await Task.Run(() => WriteDocument(documentPath, pages, compress, sessionName, word));

                // Recordings are saved as their own files next to the document.
                var videoPaths = new List<string>();
                for (int i = 0; i < videos.Count; i++)
                {
                    string source = videos[i].FilePath;
                    string target = Files.UniquePath(
                        Path.Combine(dir, stem + "_recording_" + (i + 1).ToString("00") + Path.GetExtension(source)));
                    await Task.Run(() => File.Copy(source, target));
                    videoPaths.Add(target);
                }

                _lastDir = dir;
                string note = FinishExport();
                SetStatus(
                    "Saved: " + documentPath +
                    (videoPaths.Count > 0 ? " and " + videoPaths.Count + " recording(s)." : ".") +
                    (Settings.UseSaveDir && folder == null ? " The chosen folder was unavailable, so the dialog was used." : "") +
                    note);
            }
            catch (Exception error)
            {
                SetStatus("Export failed: " + error.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        /// <summary>
        /// Writes the recordings on their own, without a document. With
        /// <paramref name="wholeSession"/> the session holds nothing else, so
        /// this counts as saving it.
        /// </summary>
        private async Task ExportVideosAsync(bool wholeSession)
        {
            List<Shot> videos = _shots.Where(s => s.IsVideo).ToList();
            if (videos.Count == 0)
            {
                SetStatus("There are no recordings to export.");
                return;
            }
            if (_busy || IsRecording)
            {
                if (IsRecording) SetStatus("Stop the recording before saving.");
                return;
            }
            SetBusy(true);
            try
            {
                // No document is produced, so the user picks a folder rather
                // than a file name.
                string dir = UsableSaveDir;
                if (dir == null)
                {
                    var dialog = new OpenFolderDialog { Title = "Choose a folder for the recordings" };
                    if (dialog.ShowDialog(this) != true)
                    {
                        SetStatus("Export cancelled.");
                        return;
                    }
                    dir = dialog.FolderName;
                }

                SetStatus("Saving recordings...");
                int saved = 0;
                foreach (Shot video in videos)
                {
                    string source = video.FilePath;
                    string target = Files.UniquePath(Path.Combine(dir, video.Name));
                    await Task.Run(() => File.Copy(source, target));
                    saved++;
                }

                _lastDir = dir;
                string note = wholeSession ? FinishExport() : "";
                SetStatus("Saved " + saved + " recording(s) to " + dir + "." + note);
            }
            catch (Exception error)
            {
                SetStatus("Export failed: " + error.Message);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private bool ChooseSaveDir()
        {
            var dialog = new OpenFolderDialog { Title = "Choose the default save folder" };
            if (!string.IsNullOrEmpty(Settings.SaveDir) && Directory.Exists(Settings.SaveDir))
                dialog.InitialDirectory = Settings.SaveDir;
            if (dialog.ShowDialog(this) != true) return false;
            Settings.SaveDir = dialog.FolderName;
            Settings.UseSaveDir = true;
            Settings.Save();
            SetStatus("Exports will go to " + Settings.SaveDir + ".");
            return true;
        }

        private void OpenOutputFolder()
        {
            if (_lastDir == null) return;
            try
            {
                if (!Directory.Exists(_lastDir)) throw new DirectoryNotFoundException("The folder no longer exists.");
                Process.Start(new ProcessStartInfo { FileName = _lastDir, UseShellExecute = true });
            }
            catch (Exception error)
            {
                SetStatus("Could not open the folder: " + error.Message);
            }
        }

        // ------------------------------------------------------------------
        // Shortcuts
        // ------------------------------------------------------------------

        private List<string> ApplyShortcuts(Dictionary<string, string> bindings)
        {
            Settings.Shortcuts = new Dictionary<string, string>(bindings);
            Settings.Save();
            _failedShortcuts = _hotkeys == null ? new List<string>() : _hotkeys.Apply(Settings.Shortcuts);
            if (_failedShortcuts.Count > 0)
            {
                SetStatus("The system refused these shortcuts: " + string.Join(", ", _failedShortcuts) +
                          ". Pick different ones in the shortcut settings.");
            }
            UpdateUi();
            return _failedShortcuts;
        }

        // ------------------------------------------------------------------
        // Sidebar events
        // ------------------------------------------------------------------

        private void ThemeToggle_Click(object sender, RoutedEventArgs e)
        {
            bool dark = ThemeToggle.IsChecked == true;
            ThemeManager.Apply(dark ? ThemeManager.Dark : ThemeManager.Light);
            ThemeManager.ApplyTitleBar(this);
            ThemeToggle.Content = dark ? "Dark" : "Light";
            Settings.Theme = ThemeManager.Current;
            Settings.Save();
        }

        private void HideCheck_Click(object sender, RoutedEventArgs e)
        {
            Settings.HideOnCapture = HideCheck.IsChecked == true;
            Settings.Save();
        }

        private void NewSessionCheck_Click(object sender, RoutedEventArgs e)
        {
            Settings.NewSessionAfterExport = NewSessionCheck.IsChecked == true;
            Settings.Save();
        }

        private void CursorCheck_Click(object sender, RoutedEventArgs e)
        {
            Settings.IncludeCursor = CursorCheck.IsChecked == true;
            Settings.Save();
        }

        private void ClicksCheck_Click(object sender, RoutedEventArgs e)
        {
            Settings.HighlightClicks = ClicksCheck.IsChecked == true;
            Settings.Save();
        }

        private void FormatCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var option = FormatCombo.SelectedItem as Option;
            if (option == null) return;
            Settings.ExportFormat = option.Key;
            Settings.Save();
            UpdateUi();
        }

        private void Paste_Click(object sender, RoutedEventArgs e)
        {
            Run(PasteAsync());
        }

        private void RecordPause_Click(object sender, RoutedEventArgs e)
        {
            TogglePause();
        }

        private void NewSession_Click(object sender, RoutedEventArgs e)
        {
            if (_session != null && _shots.Count == 0 && _trash.Count == 0)
            {
                SetStatus("This session is still empty, so it is used as the new one.");
                return;
            }
            if (StartNewSession()) SetStatus("Started \"" + DisplayName(_session) + "\". The previous session is kept in the list.");
        }

        private void RenameSession_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            Confirm.OpenPrompt(
                "Rename session",
                "The name is shown in the session list and used as the file name of the saved document.",
                DisplayName(_session),
                "Rename",
                delegate(string name)
                {
                    _session.Name = name;
                    SaveSession();
                    SetStatus("Session renamed to \"" + name + "\".");
                    UpdateUi();
                });
        }

        private void DeleteSession_Click(object sender, RoutedEventArgs e)
        {
            if (_session == null) return;
            Confirm.Open(
                "Delete session",
                "This removes the session \"" + DisplayName(_session) + "\" with " + ImageCount + " screenshot(s) and " +
                VideoCount + " recording(s) from this computer. It cannot be undone. Documents already saved elsewhere are untouched.",
                "Delete session",
                true,
                DeleteCurrentSession);
        }

        private void SessionCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var choice = SessionCombo.SelectedItem as SessionChoice;
            SessionData picked = choice == null ? null : choice.Session;
            if (picked == null || picked == _session) return;
            // Opening rebuilds this very list, so it waits until the selection
            // change has been fully handled.
            Dispatcher.BeginInvoke(new Action(delegate
            {
                if (picked == _session || !_sessions.Contains(picked) || IsRecording) return;
                OpenSession(picked);
                SetStatus("Opened \"" + DisplayName(picked) + "\" with " + _shots.Count + " item(s).");
            }));
        }

        private void CompressCheck_Click(object sender, RoutedEventArgs e)
        {
            Settings.CompressPdf = CompressCheck.IsChecked == true;
            Settings.Save();
        }

        private void FolderCheck_Click(object sender, RoutedEventArgs e)
        {
            if (FolderCheck.IsChecked != true)
            {
                Settings.UseSaveDir = false;
                Settings.Save();
            }
            else if (!string.IsNullOrEmpty(Settings.SaveDir))
            {
                Settings.UseSaveDir = true;
                Settings.Save();
            }
            else if (!ChooseSaveDir())
            {
                // No folder was picked, so the option stays off.
                FolderCheck.IsChecked = false;
            }
            UpdateUi();
        }

        private void FolderChange_Click(object sender, RoutedEventArgs e)
        {
            ChooseSaveDir();
            UpdateUi();
        }

        private void AudioCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var option = AudioCombo.SelectedItem as Option;
            if (option == null) return;
            Settings.AudioSource = option.Key;
            Settings.Save();
            UpdateUi();
        }

        private void QualityCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_loading) return;
            var quality = QualityCombo.SelectedItem as RecordingQuality;
            if (quality == null) return;
            Settings.Quality = quality.Key;
            Settings.Save();
            UpdateUi();
        }

        private void AutostartCheck_Click(object sender, RoutedEventArgs e)
        {
            bool wanted = AutostartCheck.IsChecked == true;
            try
            {
                Autostart.Set(wanted);
                SetStatus(wanted
                    ? "Proofly will start in the tray when you sign in."
                    : "Proofly will no longer start with Windows.");
            }
            catch (Exception error)
            {
                SetStatus("Could not change the startup setting: " + error.Message);
            }
            AutostartCheck.IsChecked = Autostart.IsEnabled();
        }

        private void CaptureFull_Click(object sender, RoutedEventArgs e)
        {
            Run(CaptureFullAsync());
        }

        private void CaptureRegion_Click(object sender, RoutedEventArgs e)
        {
            Run(OpenRegionAsync(RegionPurpose.Shot, false));
        }

        private void LockRegion_Click(object sender, RoutedEventArgs e)
        {
            Run(LockRegionAsync());
        }

        private void ResetLock_Click(object sender, RoutedEventArgs e)
        {
            _locked = null;
            SetStatus("Region lock cleared, captures take the whole screen again.");
            UpdateUi();
        }

        private void RecordFull_Click(object sender, RoutedEventArgs e)
        {
            StartRecording(SelectedDisplay, null);
        }

        private void RecordRegion_Click(object sender, RoutedEventArgs e)
        {
            Run(OpenRegionAsync(RegionPurpose.Record, false));
        }

        private void RecordStop_Click(object sender, RoutedEventArgs e)
        {
            StopRecording();
        }

        private void Export_Click(object sender, RoutedEventArgs e)
        {
            RunExport();
        }

        private void ExportVideos_Click(object sender, RoutedEventArgs e)
        {
            Run(ExportVideosAsync(false));
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            OpenOutputFolder();
        }

        private void Shortcuts_Click(object sender, RoutedEventArgs e)
        {
            ShortcutsPanel.Open(Settings.Shortcuts, _failedShortcuts);
        }
    }
}
