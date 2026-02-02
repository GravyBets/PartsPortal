using MaterialReqAppV3.Models;
using MaterialReqAppV3.Services;
using System;
using System.Reflection;
using System.Windows;

namespace MaterialReqAppV3
{
    public partial class BugFeatureWindow : Window
    {
        private readonly UserSettings _settings;

        public BugFeatureWindow(UserSettings settings)
        {
            InitializeComponent();
            _settings = settings;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

        private void Send_Click(object sender, RoutedEventArgs e)
        {
            string kind = ((TypeCombo.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content?.ToString() ?? "Bug").Trim();
            string text = (DetailsBox.Text ?? "").Trim();

            if (string.IsNullOrWhiteSpace(text))
            {
                MessageBox.Show("Please enter details first.", "Bug/Feature", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Pick recipient: from settings if provided, otherwise you’ll replace this once
            string to = (_settings.BugReportToEmail ?? "").Trim();
            if (string.IsNullOrWhiteSpace(to))
                to = "YOUR.EMAIL@COMPANY.COM"; // <-- replace once, or set in Settings later

            var v = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "";
            string subject = $"MaterialReqApp - {kind} - v{v} - {Environment.UserName} - {DateTime.Now:MM/dd/yyyy}";

            string body =
                $"{kind} report\n\n" +
                $"User: {Environment.UserName}\n" +
                $"Machine: {Environment.MachineName}\n" +
                $"Time: {DateTime.Now}\n" +
                $"Version: {v}\n\n" +
                "Details:\n" +
                text;

            try
            {
                object outlookApp = OutlookCom.GetOrStartOutlook();

                object mailItem = outlookApp.GetType().InvokeMember(
                    "CreateItem",
                    BindingFlags.InvokeMethod,
                    null,
                    outlookApp,
                    new object[] { 0 });

                // basic COM set
                mailItem.GetType().InvokeMember("To", BindingFlags.SetProperty, null, mailItem, new object[] { to });
                mailItem.GetType().InvokeMember("Subject", BindingFlags.SetProperty, null, mailItem, new object[] { subject });
                mailItem.GetType().InvokeMember("Body", BindingFlags.SetProperty, null, mailItem, new object[] { body });

                // open draft
                mailItem.GetType().InvokeMember("Display", BindingFlags.InvokeMethod, null, mailItem, new object[] { false });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not open Outlook draft:\n\n" + ex.Message, "Bug/Feature",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Close();
        }
    }
}
