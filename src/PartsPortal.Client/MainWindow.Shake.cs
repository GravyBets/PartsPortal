using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PartsPortal
{
    public partial class MainWindow
    {
        // A harmless 30-second Easter egg. The song is supplied by the user,
        // not distributed with PartsPortal or its ClickOnce package.
        private static readonly TimeSpan HarlemShakeDrop = TimeSpan.FromSeconds(15);
        private static readonly TimeSpan HarlemShakeDuration = TimeSpan.FromSeconds(30);

        private readonly MediaPlayer _harlemPlayer = new();
        private readonly DispatcherTimer _harlemTimer = new(DispatcherPriority.Render);
        private readonly Stopwatch _harlemClock = new();

        private TranslateTransform? _harlemOffset;
        private RotateTransform? _harlemTilt;
        private Transform? _originalShakeRootTransform;
        private bool _harlemActive;

        private static string HarlemAudioPreferencePath
        {
            get
            {
                var directory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "PartsPortal");

                return Path.Combine(directory, "harlem-shake-audio.txt");
            }
        }

        private void StartOrStopHarlemShake()
        {
            // Entering the command again toggles the effect off.
            if (_harlemActive)
            {
                StopHarlemShake();
                return;
            }

            string? audioPath = FindHarlemShakeAudio();
            if (audioPath is null)
            {
                SetStatus("Harlem Shake canceled: no audio selected.");
                return;
            }

            try
            {
                _harlemActive = true;
                _originalShakeRootTransform = ShakeRoot.RenderTransform;

                // Move the rendered content, never the native window/layout.
                // That keeps tab sizing, hit-testing, and window position intact.
                _harlemOffset = new TranslateTransform();
                _harlemTilt = new RotateTransform();

                var group = new TransformGroup();
                if (_originalShakeRootTransform is not null &&
                    !_originalShakeRootTransform.Value.IsIdentity)
                {
                    group.Children.Add(_originalShakeRootTransform);
                }
                group.Children.Add(_harlemTilt);
                group.Children.Add(_harlemOffset);
                ShakeRoot.RenderTransform = group;

                _harlemPlayer.MediaOpened += HarlemMediaOpened;
                _harlemPlayer.MediaEnded += HarlemMediaEnded;
                _harlemPlayer.MediaFailed += HarlemMediaFailed;
                _harlemTimer.Tick += HarlemTimerTick;
                PreviewKeyDown += HarlemPreviewKeyDown;

                _harlemPlayer.Volume = 0.7;
                _harlemPlayer.Open(new Uri(Path.GetFullPath(audioPath)));
                SetStatus("Harlem Shake loading... Press Esc to stop.");
            }
            catch (Exception ex)
            {
                StopHarlemShake();
                MessageBox.Show(
                    "Unable to play that audio file.\n\n" + ex.Message,
                    "Harlem Shake",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private static string? FindHarlemShakeAudio()
        {
            var preferencePath = HarlemAudioPreferencePath;

            try
            {
                if (File.Exists(preferencePath))
                {
                    var previous = File.ReadAllText(preferencePath).Trim();
                    if (File.Exists(previous))
                        return previous;
                }
            }
            catch (IOException)
            {
                // Not critical; prompt to select a file instead.
            }
            catch (UnauthorizedAccessException)
            {
                // Not critical; prompt to select a file instead.
            }

            var picker = new OpenFileDialog
            {
                Title = "Choose your copy of the Harlem Shake song",
                Filter = "Audio files (*.mp3;*.wav;*.wma)|*.mp3;*.wav;*.wma|All files (*.*)|*.*",
                CheckFileExists = true,
                Multiselect = false,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyMusic)
            };

            if (picker.ShowDialog() != true)
                return null;

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(preferencePath)!);
                File.WriteAllText(preferencePath, picker.FileName);
            }
            catch (IOException)
            {
                // Audio still plays this time; choosing again may be needed later.
            }
            catch (UnauthorizedAccessException)
            {
                // Audio still plays this time; choosing again may be needed later.
            }

            return picker.FileName;
        }

        private void HarlemMediaOpened(object? sender, EventArgs e)
        {
            if (!_harlemActive)
                return;

            _harlemClock.Restart();
            _harlemPlayer.Play();
            _harlemTimer.Interval = TimeSpan.FromMilliseconds(65);
            _harlemTimer.Start();
            SetStatus("Harlem Shake! Wait for the drop... Esc to stop.");
        }

        private void HarlemTimerTick(object? sender, EventArgs e)
        {
            if (!_harlemActive)
                return;

            if (_harlemClock.Elapsed >= HarlemShakeDuration)
            {
                StopHarlemShake();
                return;
            }

            if (_harlemOffset is null || _harlemTilt is null)
                return;

            // The classic meme: the music plays first, then the whole UI dances.
            if (_harlemClock.Elapsed < HarlemShakeDrop)
            {
                _harlemOffset.X = 0;
                _harlemOffset.Y = 0;
                _harlemTilt.Angle = 0;
                return;
            }

            _harlemOffset.X = (Random.Shared.NextDouble() - 0.5) * 22;
            _harlemOffset.Y = (Random.Shared.NextDouble() - 0.5) * 16;
            _harlemTilt.Angle = (Random.Shared.NextDouble() - 0.5) * 1.8;
        }

        private void HarlemMediaFailed(object? sender, ExceptionEventArgs e)
        {
            if (!_harlemActive)
                return;

            var detail = e.ErrorException?.Message ?? "The selected audio file could not be played.";
            StopHarlemShake();
            MessageBox.Show(
                "Unable to play the selected audio.\n\n" + detail,
                "Harlem Shake",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void HarlemMediaEnded(object? sender, EventArgs e)
        {
            StopHarlemShake();
        }

        private void HarlemPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (_harlemActive && e.Key == Key.Escape)
            {
                e.Handled = true;
                StopHarlemShake();
            }
        }

        private void StopHarlemShake()
        {
            if (!_harlemActive)
                return;

            _harlemActive = false;
            _harlemTimer.Stop();
            _harlemClock.Reset();
            _harlemTimer.Tick -= HarlemTimerTick;
            PreviewKeyDown -= HarlemPreviewKeyDown;

            _harlemPlayer.MediaOpened -= HarlemMediaOpened;
            _harlemPlayer.MediaEnded -= HarlemMediaEnded;
            _harlemPlayer.MediaFailed -= HarlemMediaFailed;
            _harlemPlayer.Stop();
            _harlemPlayer.Close();

            ShakeRoot.RenderTransform = _originalShakeRootTransform ?? Transform.Identity;
            _originalShakeRootTransform = null;
            _harlemOffset = null;
            _harlemTilt = null;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopHarlemShake();
            base.OnClosed(e);
        }
    }
}
