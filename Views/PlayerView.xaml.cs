using System;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Threading;
using Proofly.Core;
using Proofly.Models;

namespace Proofly.Views
{
    /// <summary>Plays a recording, with a speed preview and a mute switch.</summary>
    public partial class PlayerView : UserControl
    {
        private readonly DispatcherTimer _timer;
        private bool _playing;
        private bool _scrubbing;
        private bool _syncing = true;
        private double _speed = 1.0;

        public event Action CloseRequested;

        public PlayerView()
        {
            InitializeComponent();
            _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _timer.Tick += delegate { UpdateProgress(); };
            _syncing = false;
        }

        public bool IsOpen
        {
            get { return Visibility == Visibility.Visible; }
        }

        public void Open(Shot shot)
        {
            TitleText.Text = shot.Name;
            ErrorText.Visibility = Visibility.Collapsed;

            _speed = 1.0;
            foreach (object child in SpeedRow.Children)
            {
                var radio = child as RadioButton;
                if (radio != null) radio.IsChecked = (radio.Tag as string) == "1";
            }

            // A recording without an audio track has nothing to mute.
            MuteCheck.IsEnabled = shot.HasAudio;
            MuteCheck.IsChecked = false;
            Media.IsMuted = false;

            _syncing = true;
            SeekSlider.Maximum = 1;
            SeekSlider.Value = 0;
            _syncing = false;
            TimeText.Text = "00:00 / " + Files.FormatDuration(shot.DurationMs);

            Visibility = Visibility.Visible;
            Media.Source = new Uri(shot.FilePath);
            Media.Play();
            Media.SpeedRatio = _speed;
            SetPlaying(true);
            _timer.Start();
            Focus();
        }

        /// <summary>Stops playback and lets go of the file, so it can be moved or deleted.</summary>
        public void Hide()
        {
            _timer.Stop();
            _playing = false;
            Media.Stop();
            Media.Close();
            Media.Source = null;
            Visibility = Visibility.Collapsed;
        }

        private void SetPlaying(bool playing)
        {
            _playing = playing;
            PlayButton.Content = playing ? "Pause" : "Play";
        }

        private void TogglePlay()
        {
            if (_playing)
            {
                Media.Pause();
                SetPlaying(false);
            }
            else
            {
                Media.Play();
                Media.SpeedRatio = _speed;
                SetPlaying(true);
            }
        }

        private void UpdateProgress()
        {
            if (_scrubbing || !Media.NaturalDuration.HasTimeSpan) return;
            TimeSpan total = Media.NaturalDuration.TimeSpan;
            TimeSpan position = Media.Position;
            _syncing = true;
            SeekSlider.Maximum = Math.Max(0.1, total.TotalSeconds);
            SeekSlider.Value = Math.Min(SeekSlider.Maximum, position.TotalSeconds);
            _syncing = false;
            TimeText.Text = Files.FormatDuration((long)position.TotalMilliseconds) + " / " +
                            Files.FormatDuration((long)total.TotalMilliseconds);
        }

        private void Media_Opened(object sender, RoutedEventArgs e)
        {
            // The speed only sticks once the media is open.
            Media.SpeedRatio = _speed;
            UpdateProgress();
        }

        private void Media_Ended(object sender, RoutedEventArgs e)
        {
            Media.Stop();
            SetPlaying(false);
            UpdateProgress();
        }

        private void Media_Failed(object sender, ExceptionRoutedEventArgs e)
        {
            _timer.Stop();
            SetPlaying(false);
            string reason = e.ErrorException == null ? "unknown error" : e.ErrorException.Message;
            ErrorText.Text = "This recording cannot be played here (" + reason + "). The file itself is fine and can be exported.";
            ErrorText.Visibility = Visibility.Visible;
        }

        private void Play_Click(object sender, RoutedEventArgs e)
        {
            TogglePlay();
        }

        private void Speed_Click(object sender, RoutedEventArgs e)
        {
            var radio = sender as RadioButton;
            if (radio == null) return;
            double value;
            if (!double.TryParse(radio.Tag as string, NumberStyles.Float, CultureInfo.InvariantCulture, out value)) value = 1.0;
            _speed = value;
            Media.SpeedRatio = value;
        }

        private void Mute_Click(object sender, RoutedEventArgs e)
        {
            Media.IsMuted = MuteCheck.IsChecked == true;
        }

        private void Seek_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_syncing) return;
            Media.Position = TimeSpan.FromSeconds(e.NewValue);
        }

        private void Seek_DragStarted(object sender, DragStartedEventArgs e)
        {
            _scrubbing = true;
        }

        private void Seek_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            _scrubbing = false;
            Media.Position = TimeSpan.FromSeconds(SeekSlider.Value);
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            Action handler = CloseRequested;
            if (handler != null) handler();
        }

        private void Player_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                Action handler = CloseRequested;
                if (handler != null) handler();
            }
            else if (e.Key == Key.Space)
            {
                e.Handled = true;
                TogglePlay();
            }
        }
    }
}
