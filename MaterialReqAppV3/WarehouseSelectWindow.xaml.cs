using PartsPortal.Models;
using PartsPortal.Services;
using Microsoft.VisualBasic.FileIO;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;

namespace PartsPortal
    {
    public partial class WarehouseSelectWindow : Window
    {
        private readonly SettingsService _settingsService = new();
        private UserSettings _settings = new();
        private bool _isShuttingDown;

        private bool _suppressThemeToggle;

        public WarehouseSelectWindow()
        {
            // Load settings once
            _settings = _settingsService.Load();

            // Apply theme immediately (so the window renders correctly)
            ThemeService.Apply(_settings.IsDarkMode);

            InitializeComponent();

            // Sync the toggle to settings without firing events
            _suppressThemeToggle = true;
            if (ThemeToggle != null)
                ThemeToggle.IsChecked = _settings.IsDarkMode;
            _suppressThemeToggle = false;

            Closing += WarehouseSelectWindow_Closing;
        }

        private void WarehouseSelectWindow_Closing(object? sender, CancelEventArgs e)
        {
            // Prevent re-entrancy (Shutdown can trigger closing again)
            if (_isShuttingDown) return;

            _isShuttingDown = true;

            // Close the entire app when the user hits X on the Warehouse window
            Application.Current.Shutdown();
        }

        private void Warehouse_Click(object sender, RoutedEventArgs e)
        {
            string selected = "";

            if (sender is FrameworkElement fe)
                selected = fe.Tag?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(selected) && sender is System.Windows.Controls.Button b)
                selected = b.Content?.ToString() ?? "";

            selected = (selected ?? "").Trim();
            if (selected.Length == 0) return;

            if (string.Equals(selected, "Backorders", StringComparison.OrdinalIgnoreCase))
            {
                OpenBackorders();
                return;
            }

            var main = new MainWindow(selected);
            main.Show();
            Hide();
        }


        private void OpenBackorders()
        {
            string csvPath = GetCsvPathFromSettings();
            if (string.IsNullOrWhiteSpace(csvPath) || !File.Exists(csvPath))
            {
                MessageBox.Show(
                    "CSV path is not set (or file not found). Go to Settings and set the parts CSV path.",
                    "Missing CSV",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var allParts = LoadPartsFromCsv(csvPath);

            // Deduplicate by Material so it’s a clean “full list”
            var unique = allParts
                .Where(p => !string.IsNullOrWhiteSpace(p.Material))
                .GroupBy(p => p.Material.Trim(), StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(p => p.Description)
                .ToList();

            var dlg = new BackordersWindow(allParts, _settings)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            this.Hide();
            try
            {
                dlg.ShowDialog();   // still: NO dlg.Show()
            }
            finally
            {
                this.Show();
                this.Activate();
            }
        }

        private string GetCsvPathFromSettings()
        {
            if (_settings == null) return "";

            var t = _settings.GetType();

            // Works no matter what you named the property
            return (t.GetProperty("PartsCsvPath")?.GetValue(_settings) as string)
                ?? (t.GetProperty("LastCsvPath")?.GetValue(_settings) as string)
                ?? (t.GetProperty("CsvPath")?.GetValue(_settings) as string)
                ?? "";
        }

        private List<Part> LoadPartsFromCsv(string path)
        {
            var list = new List<Part>();

            using var parser = new TextFieldParser(path);
            parser.SetDelimiters(",");
            parser.HasFieldsEnclosedInQuotes = true;

            if (parser.EndOfData) return list;

            var headers = parser.ReadFields() ?? Array.Empty<string>();

            int idxDesc = FindCol(headers, "Description", "Desc");
            int idxMat = FindCol(headers, "Material", "Mat");
            int idxWh = FindCol(headers, "Warehouse", "WH", "Building");

            while (!parser.EndOfData)
            {
                var fields = parser.ReadFields();
                if (fields == null || fields.Length == 0) continue;

                string desc = GetField(fields, idxDesc);
                string mat = GetField(fields, idxMat);
                string wh = GetField(fields, idxWh);

                if (string.IsNullOrWhiteSpace(desc) && string.IsNullOrWhiteSpace(mat))
                    continue;

                list.Add(new Part
                {
                    Description = desc,
                    Material = mat,
                    Warehouse = wh
                });
            }

            return list;

            static int FindCol(string[] headers, params string[] names)
            {
                for (int i = 0; i < headers.Length; i++)
                {
                    var h = (headers[i] ?? "").Trim();
                    if (names.Any(n => string.Equals(h, n, StringComparison.OrdinalIgnoreCase)))
                        return i;
                }
                return -1;
            }

            static string GetField(string[] fields, int idx)
                => (idx >= 0 && idx < fields.Length) ? (fields[idx] ?? "").Trim() : "";
        }



        private void ThemeToggle_Checked(object sender, RoutedEventArgs e)
        {
            if (_suppressThemeToggle) return;

            _settings.IsDarkMode = true;
            _settingsService.Save(_settings);
            ThemeService.Apply(true);
        }

        private void ThemeToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_suppressThemeToggle) return;

            _settings.IsDarkMode = false;
            _settingsService.Save(_settings);
            ThemeService.Apply(false);
        }

        private void About_Click(object sender, RoutedEventArgs e)
        {
            var w = new AboutWindow { Owner = this };
            w.ShowDialog();
        }

        private void BugFeature_Click(object sender, RoutedEventArgs e)
        {
            var w = new BugFeatureWindow(_settings) { Owner = this };
            w.ShowDialog();
        }        
    }
}
