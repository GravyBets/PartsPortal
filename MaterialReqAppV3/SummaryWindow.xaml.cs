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
    }
}
