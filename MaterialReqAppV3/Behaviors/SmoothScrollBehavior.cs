using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace MaterialReqAppV3.Behaviors
{
    public static class SmoothScrollBehavior
    {
        public static bool GetIsEnabled(DependencyObject obj) => (bool)obj.GetValue(IsEnabledProperty);
        public static void SetIsEnabled(DependencyObject obj, bool value) => obj.SetValue(IsEnabledProperty, value);

        public static readonly DependencyProperty IsEnabledProperty =
            DependencyProperty.RegisterAttached("IsEnabled", typeof(bool), typeof(SmoothScrollBehavior),
                new PropertyMetadata(false, OnIsEnabledChanged));

        private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Control control) return;

            if ((bool)e.NewValue)
                control.PreviewMouseWheel += ControlOnPreviewMouseWheel;
            else
                control.PreviewMouseWheel -= ControlOnPreviewMouseWheel;
        }

        private static void ControlOnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject d) return;

            var sv = FindScrollViewer(d);
            if (sv == null) return;

            // Stop the default jumpy scroll
            e.Handled = true;

            double current = sv.VerticalOffset;

            // Tune this: higher = faster per wheel notch
            double pixelsPerNotch = 300;

            double target = current - Math.Sign(e.Delta) * pixelsPerNotch;

            // Clamp
            target = Math.Max(0, Math.Min(target, sv.ScrollableHeight));

            AnimateTo(sv, target);
        }

        private static void AnimateTo(ScrollViewer sv, double target)
        {
            // Kill any previous animation
            sv.BeginAnimation(AnimatedVerticalOffsetProperty, null);

            var anim = new DoubleAnimation
            {
                From = sv.VerticalOffset,
                To = target,
                Duration = TimeSpan.FromMilliseconds(40),
                EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
            };

            sv.BeginAnimation(AnimatedVerticalOffsetProperty, anim, HandoffBehavior.SnapshotAndReplace);
        }

        private static readonly DependencyProperty AnimatedVerticalOffsetProperty =
            DependencyProperty.RegisterAttached("AnimatedVerticalOffset", typeof(double), typeof(SmoothScrollBehavior),
                new PropertyMetadata(0.0, OnAnimatedVerticalOffsetChanged));

        private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is ScrollViewer sv)
                sv.ScrollToVerticalOffset((double)e.NewValue);
        }

        private static ScrollViewer? FindScrollViewer(DependencyObject d)
        {
            if (d is ScrollViewer sv) return sv;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(d); i++)
            {
                var child = VisualTreeHelper.GetChild(d, i);
                var found = FindScrollViewer(child);
                if (found != null) return found;
            }

            return null;
        }
    }
}
