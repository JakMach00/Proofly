using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Proofly.Core;

namespace Proofly.Views
{
    /// <summary>Lets the user rebind the global shortcuts.</summary>
    public partial class ShortcutsView : UserControl
    {
        private readonly Dictionary<string, Button> _keys = new Dictionary<string, Button>();
        private readonly Dictionary<string, TextBlock> _notes = new Dictionary<string, TextBlock>();
        private Dictionary<string, string> _bindings = AppSettings.DefaultShortcuts();
        private List<string> _failed = new List<string>();
        private string _capturing;

        /// <summary>Stores and registers new bindings, returning the ones Windows refused.</summary>
        public Func<Dictionary<string, string>, List<string>> ApplyBindings { get; set; }

        /// <summary>
        /// Switches the global shortcuts off while a combination is being
        /// recorded, otherwise pressing one would fire its action instead.
        /// </summary>
        public Action SuspendHotkeys { get; set; }

        public Func<List<string>> ResumeHotkeys { get; set; }

        public event Action CloseRequested;

        public ShortcutsView()
        {
            InitializeComponent();
            _keys[AppSettings.ActionCapture] = KeyCapture;
            _keys[AppSettings.ActionRegion] = KeyRegion;
            _keys[AppSettings.ActionRecord] = KeyRecord;
            _keys[AppSettings.ActionPause] = KeyPause;
            _keys[AppSettings.ActionExport] = KeyExport;
            _notes[AppSettings.ActionCapture] = NoteCapture;
            _notes[AppSettings.ActionRegion] = NoteRegion;
            _notes[AppSettings.ActionRecord] = NoteRecord;
            _notes[AppSettings.ActionPause] = NotePause;
            _notes[AppSettings.ActionExport] = NoteExport;
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        public void Open(Dictionary<string, string> bindings, List<string> failed)
        {
            _bindings = new Dictionary<string, string>(bindings);
            _failed = failed ?? new List<string>();
            _capturing = null;
            MessageText.Text = "";
            Visibility = Visibility.Visible;
            Render();
            Focus();
        }

        public void Hide()
        {
            StopCapturing();
            Visibility = Visibility.Collapsed;
        }

        private string BindingOf(string action)
        {
            return _bindings.TryGetValue(action, out string value) ? value ?? "" : "";
        }

        private void Render()
        {
            foreach (string action in AppSettings.Actions)
            {
                string value = BindingOf(action);
                Button key = _keys[action];
                bool active = _capturing == action;
                key.Content = active ? "Press a combination..." : (value.Length == 0 ? "none" : value);
                if (active)
                {
                    key.BorderBrush = (System.Windows.Media.Brush)FindResource("Accent");
                    key.Foreground = (System.Windows.Media.Brush)FindResource("Accent");
                }
                else
                {
                    key.ClearValue(Button.BorderBrushProperty);
                    key.ClearValue(Button.ForegroundProperty);
                }

                var notes = new List<string>();
                if (value.Length > 0 && _failed.Contains(value)) notes.Add("taken by another program");
                if (value.Length > 0 && Accelerators.ConflictsWithAltGr(value)) notes.Add("collides with AltGr typing");
                _notes[action].Text = string.Join(", ", notes);
            }
        }

        private void StartCapturing(string action)
        {
            if (_capturing == null && SuspendHotkeys != null) SuspendHotkeys();
            _capturing = action;
            MessageText.Text = "";
            Render();
            _keys[action].Focus();
        }

        private void StopCapturing()
        {
            if (_capturing == null) return;
            _capturing = null;
            if (ResumeHotkeys != null) _failed = ResumeHotkeys();
            Render();
        }

        private void Commit(Dictionary<string, string> next)
        {
            _bindings = next;
            _capturing = null;
            // Applying registers the new set, which also ends the suspension.
            if (ApplyBindings != null) _failed = ApplyBindings(new Dictionary<string, string>(next));
            Render();
            Focus();
        }

        private void Key_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button == null) return;
            StartCapturing(button.Tag as string);
        }

        private void Defaults_Click(object sender, RoutedEventArgs e)
        {
            MessageText.Text = "";
            Commit(AppSettings.DefaultShortcuts());
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Action handler = CloseRequested;
            if (handler != null) handler();
        }

        private static bool IsModifier(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl || key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift || key == Key.LWin || key == Key.RWin;
        }

        private void Shortcuts_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Alt combinations arrive as a system key.
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (_capturing == null)
            {
                if (key == Key.Escape)
                {
                    e.Handled = true;
                    Action handler = CloseRequested;
                    if (handler != null) handler();
                }
                return;
            }

            e.Handled = true;
            HandleCombination(key);
        }

        private void Shortcuts_PreviewKeyUp(object sender, KeyEventArgs e)
        {
            // Windows never reports Print Screen as a key press, only as a
            // release, so it is picked up here.
            if (_capturing == null) return;
            Key key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key != Key.Snapshot) return;
            e.Handled = true;
            HandleCombination(key);
        }

        private void HandleCombination(Key key)
        {
            if (IsModifier(key) || key == Key.ImeProcessed || key == Key.DeadCharProcessed || key == Key.None) return;
            string action = _capturing;

            if (key == Key.Escape)
            {
                MessageText.Text = "";
                StopCapturing();
                Focus();
                return;
            }
            if (key == Key.Back || key == Key.Delete)
            {
                var cleared = new Dictionary<string, string>(_bindings);
                cleared[action] = "";
                Commit(cleared);
                MessageText.Text = "Shortcut cleared.";
                return;
            }

            ModifierKeys modifiers = Keyboard.Modifiers;
            string accelerator = key == Key.Tab
                ? null
                : Accelerators.Format(
                    (modifiers & ModifierKeys.Control) != 0,
                    (modifiers & ModifierKeys.Alt) != 0,
                    (modifiers & ModifierKeys.Shift) != 0,
                    (modifiers & ModifierKeys.Windows) != 0,
                    (uint)KeyInterop.VirtualKeyFromKey(key));
            if (accelerator == null)
            {
                MessageText.Text = "That combination cannot be a global shortcut. Use a modifier or a function key.";
                return;
            }
            foreach (string other in AppSettings.Actions)
            {
                if (other != action && BindingOf(other) == accelerator)
                {
                    MessageText.Text = accelerator + " is already assigned to another action.";
                    return;
                }
            }

            var next = new Dictionary<string, string>(_bindings);
            next[action] = accelerator;
            Commit(next);
            MessageText.Text = Accelerators.ConflictsWithAltGr(accelerator)
                ? accelerator + " is also produced by AltGr on international layouts, so it can fire while typing."
                : "";
        }
    }
}
