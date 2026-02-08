using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using MaterialReqAppV3.Models;

namespace MaterialReqAppV3
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
private void PartsScroll_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        // If the inner list can still scroll, let it scroll normally.
        bool scrollingUp = e.Delta > 0;
        bool atTop = sv.VerticalOffset <= 0;
        bool atBottom = sv.VerticalOffset >= sv.ScrollableHeight;

        if ((scrollingUp && !atTop) || (!scrollingUp && !atBottom))
            return;

        // Otherwise, pass the wheel to the DataGrid so the page keeps moving
        e.Handled = true;

        var grid = FindAncestor<DataGrid>(sv);
        if (grid == null) return;

        var evt = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
        {
            RoutedEvent = UIElement.MouseWheelEvent,
            Source = sv
        };

        grid.RaiseEvent(evt);
    }

    private static T? FindAncestor<T>(DependencyObject start) where T : DependencyObject
    {
        DependencyObject current = start;
        while (current != null)
        {
            if (current is T match) return match;
            current = VisualTreeHelper.GetParent(current);
        }
        return null;
    }

}
}
