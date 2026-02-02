using System.Collections.Generic;
using System.Windows;
using MaterialReqAppV3.Models;

namespace MaterialReqAppV3
{
    public partial class SummaryWindow : Window
    {
        public SummaryWindow(List<PrintTabSummary> rows, string subtitle)
        {
            InitializeComponent();
            SubText.Text = subtitle;
            SummaryGrid.ItemsSource = rows;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void Generate_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }
    }
}
