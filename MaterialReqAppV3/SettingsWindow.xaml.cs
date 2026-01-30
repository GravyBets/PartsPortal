using MaterialReqAppV3.Models;
using Microsoft.Win32;
using System.Windows;

namespace MaterialReqAppV3
{
    public partial class SettingsWindow : Window
    {
        
        public UserSettings Settings { get; private set; }

        public SettingsWindow(UserSettings settings)
        {
            InitializeComponent();

            Settings = settings;

            NameBox.Text = Settings.Name;
            EmployeeIdBox.Text = Settings.EmployeeId;
            TruckBox.Text = Settings.TruckNumber;
            PdfFolderBox.Text = Settings.PdfOutputFolder;
            CsvPathBox.Text = Settings.CsvPath;
        }

        private void BrowsePdfFolder_Click(object sender, RoutedEventArgs e)
        {
            // WPF-only folder picker workaround (no WinForms)
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Select PDF output folder",
                CheckFileExists = false,
                CheckPathExists = true,
                ValidateNames = false,
                FileName = "Select Folder"
            };

            if (dlg.ShowDialog() == true)
            {
                var folder = System.IO.Path.GetDirectoryName(dlg.FileName);
                if (!string.IsNullOrWhiteSpace(folder))
                    PdfFolderBox.Text = folder;
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
                CsvPathBox.Text = dlg.FileName;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            Settings.Name = (NameBox.Text ?? "").Trim();
            Settings.TruckNumber = (TruckBox.Text ?? "").Trim();
            Settings.PdfOutputFolder = (PdfFolderBox.Text ?? "").Trim();
            Settings.CsvPath = (CsvPathBox.Text ?? "").Trim();

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
