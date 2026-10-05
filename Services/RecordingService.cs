using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using ScreenRecorderLib;

namespace Proofly.Services
{
    public sealed class RecordingQuality
    {
        public string Key { get; set; }
        public string Label { get; set; }
        public int Bitrate { get; set; }
        public int Fps { get; set; }
        public double Scale { get; set; }

        public override string ToString()
        {
            return Label;
        }

        /// <summary>Frame rate, bitrate and scale are set together.</summary>
        public static readonly RecordingQuality[] All =
        {
            new RecordingQuality { Key = "low", Label = "Low (smallest file)", Bitrate = 500000, Fps = 10, Scale = 0.6 },
            new RecordingQuality { Key = "medium", Label = "Medium", Bitrate = 1200000, Fps = 15, Scale = 0.8 },
            new RecordingQuality { Key = "high", Label = "High", Bitrate = 3000000, Fps = 20, Scale = 1.0 },
        };

        public static RecordingQuality Find(string key)
        {
            return All.FirstOrDefault(q => q.Key == key) ?? All[1];
        }
    }

    public sealed class RecordingResult
    {
        public string FilePath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public long DurationMs { get; set; }
        public bool HasAudio { get; set; }
    }

    /// <summary>
    /// Screen recording to MP4 (H.264) through ScreenRecorderLib, which encodes
    /// with Media Foundation. This file is the only place that touches the
    /// library, so replacing it later stays a local change.
    /// </summary>
    public sealed class RecordingService
    {
        private Recorder _recorder;
        private readonly Stopwatch _clock = new Stopwatch();
        private string _path;
        private int _width;
        private int _height;
        private bool _hasAudio;

        /// <summary>Set by whichever comes first: the library's event or the watchdog.</summary>
        private int _reported;

        /// <summary>Raised on a worker thread when the file is complete.</summary>
        public event Action<RecordingResult> Completed;

        /// <summary>Raised on a worker thread when the recording could not be written.</summary>
        public event Action<string> Failed;

        public bool IsRecording { get; private set; }

        public bool IsPaused { get; private set; }

        public TimeSpan Elapsed
        {
            get { return _clock.Elapsed; }
        }

        /// <summary>
        /// Starts recording a display, or a region of it given in pixels relative
        /// to that display, into the given file. Returns notes about audio
        /// sources that were left out.
        /// </summary>
        public List<string> Start(
            DisplayInfo display, Int32Rect? region, RecordingQuality quality, string audio, string outputPath)
        {
            if (IsRecording) throw new InvalidOperationException("A recording is already running.");
            var warnings = new List<string>();

            int sourceWidth = region.HasValue ? region.Value.Width : display.Bounds.Width;
            int sourceHeight = region.HasValue ? region.Value.Height : display.Bounds.Height;
            // H.264 needs even dimensions.
            _width = Even(sourceWidth * quality.Scale);
            _height = Even(sourceHeight * quality.Scale);

            var source = new DisplayRecordingSource(display.DeviceName);
            if (region.HasValue)
            {
                source.SourceRect = new ScreenRect(
                    region.Value.X, region.Value.Y, region.Value.Width, region.Value.Height);
            }

            List<AudioSourceBase> audioSources = CollectAudio(audio, warnings);
            _hasAudio = audioSources.Count > 0;

            var options = new RecorderOptions
            {
                SourceOptions = new SourceOptions
                {
                    RecordingSources = new List<RecordingSourceBase> { source },
                },
                OutputOptions = new OutputOptions
                {
                    RecorderMode = RecorderMode.Video,
                    OutputFrameSize = new ScreenSize(_width, _height),
                    Stretch = StretchMode.Uniform,
                },
                AudioOptions = new AudioOptions
                {
                    IsAudioEnabled = _hasAudio,
                    AudioSources = audioSources,
                    Bitrate = AudioBitrate.bitrate_128kbps,
                    Channels = AudioChannels.Stereo,
                },
                VideoEncoderOptions = new VideoEncoderOptions
                {
                    Bitrate = quality.Bitrate,
                    Framerate = quality.Fps,
                    IsFixedFramerate = false,
                    Encoder = new H264VideoEncoder
                    {
                        BitrateMode = H264BitrateControlMode.CBR,
                        EncoderProfile = H264Profile.Main,
                    },
                    IsFragmentedMp4Enabled = false,
                    IsHardwareEncodingEnabled = true,
                },
                MouseOptions = new MouseOptions
                {
                    IsMousePointerEnabled = true,
                    // Clicks are marked by ClickHighlighter, which draws on the
                    // screen and can hold and fade the mark. The library's own
                    // marker only flashes for a fixed time.
                    IsMouseClicksDetected = false,
                },
            };

            string folder = Path.GetDirectoryName(outputPath);
            if (!string.IsNullOrEmpty(folder)) Directory.CreateDirectory(folder);
            _path = outputPath;

            _reported = 0;
            _recorder = Recorder.CreateRecorder(options);
            _recorder.OnRecordingComplete += OnComplete;
            _recorder.OnRecordingFailed += OnFailed;
            _recorder.Record(_path);
            _clock.Restart();
            IsRecording = true;
            IsPaused = false;
            return warnings;
        }

        /// <summary>
        /// Holds the recording without ending it. Nothing is written while it
        /// is paused, so the pause simply does not appear in the file.
        /// </summary>
        public void Pause()
        {
            if (!IsRecording || IsPaused || _recorder == null) return;
            _recorder.Pause();
            _clock.Stop();
            IsPaused = true;
        }

        public void Resume()
        {
            if (!IsRecording || !IsPaused || _recorder == null) return;
            _recorder.Resume();
            _clock.Start();
            IsPaused = false;
        }

        public void Stop()
        {
            if (!IsRecording || _recorder == null) return;
            _clock.Stop();
            _recorder.Stop();
            WatchForSilence(_path);
        }

        /// <summary>
        /// The result normally arrives through the library's event. Should that
        /// event never come, the file on disk is reported instead, so a finished
        /// recording is not lost behind a status that never changes.
        /// </summary>
        private void WatchForSilence(string path)
        {
            Task.Run(async delegate
            {
                await Task.Delay(15000);
                if (Interlocked.Exchange(ref _reported, 1) != 0) return;

                bool usable = false;
                try
                {
                    usable = File.Exists(path) && new FileInfo(path).Length > 0;
                }
                catch (Exception)
                {
                    usable = false;
                }

                Action<RecordingResult> completed = Completed;
                Action<string> failed = Failed;
                var result = new RecordingResult
                {
                    FilePath = path,
                    Width = _width,
                    Height = _height,
                    DurationMs = (long)_clock.Elapsed.TotalMilliseconds,
                    HasAudio = _hasAudio,
                };
                Release();
                if (usable)
                {
                    if (completed != null) completed(result);
                }
                else if (failed != null)
                {
                    failed("The recorder did not produce a file.");
                }
            });
        }

        private void OnComplete(object sender, RecordingCompleteEventArgs e)
        {
            if (Interlocked.Exchange(ref _reported, 1) != 0) return;
            var result = new RecordingResult
            {
                FilePath = string.IsNullOrEmpty(e.FilePath) ? _path : e.FilePath,
                Width = _width,
                Height = _height,
                DurationMs = (long)_clock.Elapsed.TotalMilliseconds,
                HasAudio = _hasAudio,
            };
            Action<RecordingResult> handler = Completed;
            Release();
            if (handler != null) handler(result);
        }

        private void OnFailed(object sender, RecordingFailedEventArgs e)
        {
            if (Interlocked.Exchange(ref _reported, 1) != 0) return;
            string error = e.Error;
            _clock.Stop();
            Release();
            Action<string> handler = Failed;
            if (handler != null) handler(error);
        }

        private void Release()
        {
            IsRecording = false;
            IsPaused = false;
            Recorder recorder = _recorder;
            _recorder = null;
            if (recorder == null) return;
            recorder.OnRecordingComplete -= OnComplete;
            recorder.OnRecordingFailed -= OnFailed;
            var disposable = recorder as IDisposable;
            if (disposable == null) return;
            // The library raises its events on its own worker thread, and
            // disposing the recorder waits for that thread. Disposing from
            // inside the event would therefore wait for itself and never
            // return, so it happens a moment later on another thread.
            Task.Run(async delegate
            {
                await Task.Delay(500);
                try
                {
                    disposable.Dispose();
                }
                catch (Exception)
                {
                    // The file is already complete at this point.
                }
            });
        }

        /// <summary>
        /// A missing microphone or playback device must never abort a recording,
        /// so an unavailable source is dropped and reported instead.
        /// </summary>
        private static List<AudioSourceBase> CollectAudio(string audio, List<string> warnings)
        {
            var sources = new List<AudioSourceBase>();
            bool wantMic = audio == "mic" || audio == "both";
            bool wantSystem = audio == "system" || audio == "both";

            if (wantSystem)
            {
                if (HasDevice(() => Recorder.GetSystemAudioLoopbackDevices().Any()))
                    sources.Add(LoopbackAudioSource.Default);
                else
                    warnings.Add("System audio is unavailable, recording without it.");
            }
            if (wantMic)
            {
                if (HasDevice(() => Recorder.GetSystemAudioCaptureDevices().Any()))
                    sources.Add(CaptureAudioSource.Default);
                else
                    warnings.Add("No microphone was found, recording without it.");
            }
            return sources;
        }

        private static bool HasDevice(Func<bool> probe)
        {
            try
            {
                return probe();
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int Even(double value)
        {
            int rounded = Math.Max(2, (int)Math.Round(value));
            return rounded - (rounded % 2);
        }
    }
}
