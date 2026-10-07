using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using PartsPortal.Models;

namespace PartsPortal
{
    public partial class SummaryWindow : Window
    {
        public SummaryWindow(List<PrintTabSummary> rows, string subtitle)
        {
            InitializeComponent();
            SummaryGrid.ItemsSource = rows;
        }
        public enum SummaryAction
        {
            GenerateOnly,
            EmailDraft,
            Email
        }

        public SummaryAction Action { get; private set; } = SummaryAction.GenerateOnly;     


        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            Action = SummaryAction.GenerateOnly;
            DialogResult = true;
            Close();
        }

        private void Email_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                this,
                "Send Email now?",
                "Confirm Email",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
                return; // stay on the Summary window

            Action = SummaryAction.Email;
            DialogResult = true;
            Close();
        }        

        private void EmailDraft_Click(object sender, RoutedEventArgs e)
        {
            Action = SummaryAction.EmailDraft;
            DialogResult = true;
            Close();
        }
        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
        private void SummaryGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not DependencyObject d) return;

            var sv = FindVisualChild<ScrollViewer>(d);
            if (sv == null) return;

            // Tune these two numbers:
            // "partBlockPx" ≈ height of one part entry (the 2-line block in your Parts cell)
            // "blocksPerNotch" = how many part entries per wheel tick
            const double partBlockPx = 36;     // try 32–44
            const double blocksPerNotch = 3;   // try 2–5

            double direction = e.Delta > 0 ? -1 : 1;
            double offset = direction * partBlockPx * blocksPerNotch;

            // Optional modifiers:
            if (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift))
                offset *= 0.5;   // slow
            if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                offset *= 2.0;   // fast

            sv.ScrollToVerticalOffset(sv.VerticalOffset + offset);
            e.Handled = true;
        }

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;

                var found = FindVisualChild<T>(child);
                if (found != null) return found;
            }
            return null;
        }

    }
}
