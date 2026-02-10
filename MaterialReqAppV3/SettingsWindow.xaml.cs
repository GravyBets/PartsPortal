using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using MaterialReqAppV3.Models;
using System.Linq;
using Microsoft.VisualBasic;

namespace MaterialReqAppV3
{
    public partial class SettingsWindow : Window

    {
        private readonly SettingsService _settingsService = new();
        // This is the edited copy the caller will read when DialogResult == true
        public UserSettings Settings { get; private set; }

        public SettingsWindow(UserSettings currentSettings)
        {
            InitializeComponent();

            // Work on a clone so Cancel doesn't mutate the app settings
            Settings = Clone(currentSettings);

            // Ensure EmployeeId is always set
            if (string.IsNullOrWhiteSpace(Settings.EmployeeId))
                Settings.EmployeeId = Environment.UserName;

            DataContext = Settings;
        }

        private static UserSettings Clone(UserSettings s)
        {
            return new UserSettings
            {
                Name = s.Name ?? "",
                EmployeeId = s.EmployeeId ?? "",
                TruckNumber = s.TruckNumber ?? "",
                PdfOutputFolder = s.PdfOutputFolder ?? "",
                CsvPath = s.CsvPath ?? "",

                EmailTo = s.EmailTo ?? "",
                EmailCc = s.EmailCc ?? "",
                EmailSubjectTemplate = string.IsNullOrWhiteSpace(s.EmailSubjectTemplate)
                    ? "Material Requisition - {Warehouse} - {Date}"
                    : s.EmailSubjectTemplate,
                EmailOpenDraftInsteadOfSend = s.EmailOpenDraftInsteadOfSend,
                EmailDirectory = s.EmailDirectory != null ? new List<string>(s.EmailDirectory) : new List<string>()


                IsDarkMode = s.IsDarkMode,
                BugReportToEmail = s.BugReportToEmail ?? ""

            };
        }

        private void BrowsePdfFolder_Click(object sender, RoutedEventArgs e)
        {
            // "Pick folder" trick using OpenFileDialog
            var dlg = new OpenFileDialog
            {
                Title = "Select PDF output folder",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Select Folder"
            };

            if (dlg.ShowDialog() == true)
            {
                var folder = Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrWhiteSpace(folder))
                    Settings.PdfOutputFolder = folder;   // ✅ updates bound TextBox automatically
            }
        }

        private void BrowseCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV files (*.csv)|*.csv",
                Title = "Select parts CSV file"
            };

            if (dlg.ShowDialog() == true)
                Settings.CsvPath = dlg.FileName;         // ✅ updates bound TextBox automatically
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _settingsService.Save(Settings); // ✅ persist here no matter who opened the window
                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Settings save failed:\n\n" + ex.Message,
                    "Settings", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CopyTo_Click(object sender, RoutedEventArgs e)
        {
            if (EmailDirectoryListBox.SelectedItem is not string email) return;
            email = (email ?? "").Trim();
            if (email.Length == 0) return;

            Settings.EmailTo = email;
        }

        private void CopyCc_Click(object sender, RoutedEventArgs e)
        {
            if (EmailDirectoryListBox.SelectedItem is not string email) return;
            email = (email ?? "").Trim();
            if (email.Length == 0) return;

            Settings.EmailCc = email;
        }

        private void AddEmail_Click(object sender, RoutedEventArgs e)
        {
            // simple prompt (no new window needed)
            string input = Microsoft.VisualBasic.Interaction.InputBox(
                "Enter an email address to add:",
                "Add Email",
                "");

            string email = (input ?? "").Trim();

            if (email.Length == 0) return;

            // very basic validation
            if (!email.Contains("@") || email.Contains(" "))
            {
                MessageBox.Show("That doesn’t look like a valid email.",
                    "Add Email", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            Settings.EmailDirectory ??= new List<string>();

            if (Settings.EmailDirectory.Any(x => string.Equals(x.Trim(), email, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That email is already in the directory.",
                    "Add Email", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            Settings.EmailDirectory.Add(email);

            // refresh listbox if needed
            EmailDirectoryListBox.Items.Refresh();

            // optionally auto-select new item
            EmailDirectoryListBox.SelectedItem = email;
            EmailDirectoryListBox.ScrollIntoView(email);
        }

        private void RemoveEmail_Click(object sender, RoutedEventArgs e)
        {
            if (EmailDirectoryListBox.SelectedItem is not string email) return;

            var result = MessageBox.Show(
                $"Remove this email from your directory?\n\n{email}",
                "Remove Email",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            Settings.EmailDirectory ??= new List<string>();
            Settings.EmailDirectory.Remove(email);

            EmailDirectoryListBox.Items.Refresh();
        }

    }
}
