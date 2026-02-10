using Microsoft.Win32;
using System;
using System.IO;
using System.Windows;
using MaterialReqAppV3.Models;

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
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
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
    }
}
