using MaterialReqAppV3.Models;
using MaterialReqAppV3.Services;
using Microsoft.Win32;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Data;



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

                // ✅ copy directory list
                EmailDirectory = new ObservableCollection<EmailDirectoryEntry>(
                    (s.EmailDirectory ?? new ObservableCollection<EmailDirectoryEntry>())
                )
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
                Settings.IsDarkMode = ThemeService.IsDark; // ✅ preserve theme on save

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
            if (EmailDirectoryListBox.SelectedItem is not EmailDirectoryEntry entry)
                return;

            string email = (entry.Email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email))
                return;

            Settings.EmailTo = AppendEmail(Settings.EmailTo, email);
        }
        
        private void CopyCc_Click(object sender, RoutedEventArgs e)
        {
            if (EmailDirectoryListBox.SelectedItem is not EmailDirectoryEntry entry)
                return;

            string email = (entry.Email ?? "").Trim();
            if (string.IsNullOrWhiteSpace(email))
                return;

            Settings.EmailCc = AppendEmail(Settings.EmailCc, email);
        }

        private void AddEmail_Click(object sender, RoutedEventArgs e)
        {
            var win = new AddEmailWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            if (win.ShowDialog() != true)
                return;

            string first = (win.FirstName ?? "").Trim();
            string last = (win.LastName ?? "").Trim();
            string email = (win.Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(email))
                return;

            Settings.EmailDirectory ??= new ObservableCollection<EmailDirectoryEntry>();

            // prevent duplicates by EMAIL (case-insensitive)
            if (Settings.EmailDirectory.Any(x =>
                    string.Equals((x?.Email ?? "").Trim(), email, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That email is already in the directory.",
                    "Add Email", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var entry = new EmailDirectoryEntry
            {
                FirstName = first,
                LastName = last,
                Email = email
            };

            Settings.EmailDirectory.Add(entry);

            EmailDirectoryListBox.Items.Refresh();
            EmailDirectoryListBox.SelectedItem = entry;
            EmailDirectoryListBox.ScrollIntoView(entry);
        }

        private void RemoveEmail_Click(object sender, RoutedEventArgs e)
        {
            if (EmailDirectoryListBox.SelectedItem is not EmailDirectoryEntry entry)
                return;

            string label = (entry.DisplayName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(label))
                label = (entry.Email ?? "").Trim();

            var result = MessageBox.Show(
                $"Remove this entry from your directory?\n\n{label}",
                "Remove Email",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (result != MessageBoxResult.Yes)
                return;

            Settings.EmailDirectory ??= new ObservableCollection<EmailDirectoryEntry>();
            Settings.EmailDirectory.Remove(entry);

            EmailDirectoryListBox.Items.Refresh();
        }

        private static string AppendEmail(string existing, string emailToAdd)
        {
            existing = (existing ?? "").Trim();
            emailToAdd = (emailToAdd ?? "").Trim();

            if (string.IsNullOrWhiteSpace(emailToAdd))
                return existing;

            // Split on ; or , and normalize
            var parts = existing
                .Split(new[] { ';', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Where(x => x.Length > 0)
                .ToList();

            // Prevent duplicates (case-insensitive)
            if (!parts.Any(x => string.Equals(x, emailToAdd, StringComparison.OrdinalIgnoreCase)))
                parts.Add(emailToAdd);

            return string.Join("; ", parts);
        }

        private void EmailDirectoryView_Filter(object sender, FilterEventArgs e)
        {
            if (e.Item is not EmailDirectoryEntry entry)
            {
                e.Accepted = false;
                return;
            }

            var q = (EmailSearchBox?.Text ?? "").Trim();
            if (q.Length == 0)
            {
                e.Accepted = true;
                return;
            }

            e.Accepted =
                ContainsIgnoreCase(entry.FirstName, q) ||
                ContainsIgnoreCase(entry.LastName, q) ||
                ContainsIgnoreCase(entry.Email, q) ||
                ContainsIgnoreCase(entry.DisplayName, q);
        }

        private static bool ContainsIgnoreCase(string? source, string query) =>
            !string.IsNullOrWhiteSpace(source) &&
            source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

        private void EmailSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (Resources["EmailDirectoryView"] is CollectionViewSource cvs)
                cvs.View.Refresh();
        }

    }
}
