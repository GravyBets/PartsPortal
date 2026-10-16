using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace PartsPortal
{
    public partial class MainWindow
    {
        // Audio is installed along with the client (including ClickOnce).
        // Animation timing comes from MediaPlayer.Position so the drop stays
        // synchronized with playback, not with the time spent loading the MP3.
        private const double HarlemShakeDropSeconds = 14.20;
        private const double HarlemShakeEndSeconds = 28.22;
        private const double SoloWiggleSeconds = 0.38;

        private readonly MediaPlayer _harlemPlayer = new();
        private readonly DispatcherTimer _harlemTimer = new(DispatcherPriority.Render);
        private readonly List<ShakeTarget> _shakeTargets = new();
        private bool _harlemActive;
        private int _lastSoloSecond = -1;
        private int _soloTargetIndex = -1;
        private int _previousSoloTargetIndex = -1;

        private sealed class ShakeTarget
        {
            public FrameworkElement Element { get; }
            public Transform OriginalTransform { get; }
            public Point OriginalOrigin { get; }
            public TranslateTransform Move { get; } = new();
            public RotateTransform Turn { get; } = new();
            public double Phase { get; } = Random.Shared.NextDouble() * Math.PI * 2;
            public double Speed { get; } = 17 + Random.Shared.NextDouble() * 13;
            public double Amount { get; } = 0.7 + Random.Shared.NextDouble() * 0.6;

            public ShakeTarget(FrameworkElement element)
            {
                Element = element;
                OriginalTransform = element.RenderTransform;
                OriginalOrigin = element.RenderTransformOrigin;

                var transforms = new TransformGroup();
                if (!OriginalTransform.Value.IsIdentity)
                    transforms.Children.Add(OriginalTransform);

                transforms.Children.Add(Turn);
                transforms.Children.Add(Move);

                element.RenderTransformOrigin = new Point(0.5, 0.5);
                element.RenderTransform = transforms;
            }

            public void Reset()
            {
                Move.X = 0;
                Move.Y = 0;
                Turn.Angle = 0;
            }

            public void Restore()
            {
                Element.RenderTransform = OriginalTransform;
                Element.RenderTransformOrigin = OriginalOrigin;
            }
        }

        private void StartOrStopHarlemShake()
        {
            // Running the command again or pressing Escape stops the effect.
            if (_harlemActive)
            {
                StopHarlemShake();
                return;
            }

            string audioPath = Path.Combine(
                AppContext.BaseDirectory, "Assets", "shake.mp3");

            if (!File.Exists(audioPath))
            {
                MessageBox.Show(
                    "The bundled Harlem Shake audio was not found.\n\n" +
                    "Reinstall or update PartsPortal to restore Assets\\shake.mp3.",
                    "Harlem Shake",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
                return;
            }

            try
            {
                _harlemActive = true;
                _lastSoloSecond = -1;
                _soloTargetIndex = -1;
                _previousSoloTargetIndex = -1;

                _harlemPlayer.MediaOpened += HarlemMediaOpened;
                _harlemPlayer.MediaEnded += HarlemMediaEnded;
                _harlemPlayer.MediaFailed += HarlemMediaFailed;
                _harlemTimer.Tick += HarlemTimerTick;
                PreviewKeyDown += HarlemPreviewKeyDown;

                _harlemPlayer.Volume = 0.7;
                _harlemPlayer.Open(new Uri(audioPath, UriKind.Absolute));
                SetStatus("Harlem Shake loading... Press Esc to stop.");
            }
            catch (Exception ex)
            {
                StopHarlemShake();
                MessageBox.Show(
                    "Unable to play the bundled audio.\n\n" + ex.Message,
                    "Harlem Shake",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void HarlemMediaOpened(object? sender, EventArgs e)
        {
            if (!_harlemActive)
                return;

            try
            {
                // Every visible UI leaf can dance independently: buttons,
                // text boxes, headers, standalone text and images. Never
                // transform their layout containers or the whole window.
                GatherShakeTargets(ShakeRoot);

                _harlemPlayer.Play();
                _harlemTimer.Interval = TimeSpan.FromMilliseconds(45);
                _harlemTimer.Start();
                SetStatus("Harlem Shake! A little buildup... Esc to stop.");
            }
            catch (Exception ex)
            {
                StopHarlemShake();
                MessageBox.Show(
                    "Unable to start the Harlem Shake.\n\n" + ex.Message,
                    "Harlem Shake",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
        }

        private void GatherShakeTargets(DependencyObject parent)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);

            for (int i = 0; i < count; i++)
            {
                DependencyObject child = VisualTreeHelper.GetChild(parent, i);

                if (child is FrameworkElement element)
                {
                    // Hidden, unmeasured or off-screen elements do not need
                    // animation. WPF virtualizes rows that are not displayed.
                    if (!element.IsVisible ||
                        element.ActualWidth < 2 ||
                        element.ActualHeight < 2)
                        continue;

                    if (IsShakeable(element))
                    {
                        _shakeTargets.Add(new ShakeTarget(element));
                        // Don't animate the text inside a button AND its
                        // parent button. They should move as one unit.
                        continue;
                    }
                }

                GatherShakeTargets(child);
            }
        }

        private static bool IsShakeable(FrameworkElement element)
        {
            return element is ButtonBase
                or TextBoxBase
                or TextBlock
                or ComboBox
                or TabItem
                or Image
                or Label
                or PasswordBox;
        }

        private void HarlemTimerTick(object? sender, EventArgs e)
        {
            if (!_harlemActive)
                return;

            double seconds = _harlemPlayer.Position.TotalSeconds;

            if (seconds >= HarlemShakeEndSeconds)
            {
                StopHarlemShake();
                return;
            }

            if (_shakeTargets.Count == 0)
                return;

            if (seconds < HarlemShakeDropSeconds)
            {
                // Exactly one random visible element gets a quick, subtle
                // wiggle each second during the musical buildup.
                int wholeSecond = (int)Math.Floor(seconds);
                if (wholeSecond != _lastSoloSecond)
                {
                    _lastSoloSecond = wholeSecond;
                    _soloTargetIndex = Random.Shared.Next(_shakeTargets.Count);

                    if (_shakeTargets.Count > 1 &&
                        _soloTargetIndex == _previousSoloTargetIndex)
                        _soloTargetIndex = (_soloTargetIndex + 1) % _shakeTargets.Count;

                    _previousSoloTargetIndex = _soloTargetIndex;
                }

                double fraction = seconds - wholeSecond;
                double envelope = fraction < SoloWiggleSeconds
                    ? Math.Sin(Math.PI * fraction / SoloWiggleSeconds)
                    : 0;
                double wave = Math.Sin(fraction * 44);

                for (int i = 0; i < _shakeTargets.Count; i++)
                {
                    var target = _shakeTargets[i];

                    if (i == _soloTargetIndex && envelope > 0)
                    {
                        target.Move.X = wave * 4.5 * envelope;
                        target.Move.Y = Math.Cos(fraction * 38) * 2 * envelope;
                        target.Turn.Angle = wave * 2.5 * envelope;
                    }
                    else
                    {
                        target.Reset();
                    }
                }

                return;
            }

            // At exactly 14.5 seconds into the recording, all elements
            // join in. Different phases and speeds keep each one moving
            // independently, rather than shaking the entire root Grid.
            double beat = seconds - HarlemShakeDropSeconds;

            foreach (var target in _shakeTargets)
            {
                double x = Math.Sin(beat * target.Speed + target.Phase);
                double y = Math.Cos(beat * target.Speed * 0.83 + target.Phase * 1.7);
                double turn = Math.Sin(beat * target.Speed * 0.7 + target.Phase * 0.6);

                target.Move.X = x * 12 * target.Amount;
                target.Move.Y = y * 8 * target.Amount;
                target.Turn.Angle = turn * 4.5 * target.Amount;
            }
        }

        private void HarlemMediaFailed(object? sender, ExceptionEventArgs e)
        {
            if (!_harlemActive)
                return;

            string detail = e.ErrorException?.Message ??
                "The bundled audio file could not be played.";

            StopHarlemShake();

            MessageBox.Show(
                "Unable to play the bundled Harlem Shake audio.\n\n" + detail,
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
            _harlemTimer.Tick -= HarlemTimerTick;
            PreviewKeyDown -= HarlemPreviewKeyDown;

            _harlemPlayer.MediaOpened -= HarlemMediaOpened;
            _harlemPlayer.MediaEnded -= HarlemMediaEnded;
            _harlemPlayer.MediaFailed -= HarlemMediaFailed;

            _harlemPlayer.Stop();
            _harlemPlayer.Close();

            // Restore every control's previous transform and origin.
            foreach (var target in _shakeTargets)
                target.Restore();

            _shakeTargets.Clear();
            _lastSoloSecond = -1;
            _soloTargetIndex = -1;
            _previousSoloTargetIndex = -1;
        }

        protected override void OnClosed(EventArgs e)
        {
            StopHarlemShake();
            base.OnClosed(e);
        }
    }
}
