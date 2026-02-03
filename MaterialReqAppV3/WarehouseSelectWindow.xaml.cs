using MaterialReqAppV3.Models;
using MaterialReqAppV3.Services;
using System;
using System.Windows;

namespace MaterialReqAppV3
{
    public partial class WarehouseSelectWindow : Window
    {
        private readonly SettingsService _settingsService = new();
        private UserSettings _settings = new();

        private bool _suppressThemeToggle;

        public WarehouseSelectWindow()
        {
            InitializeComponent();
            Loaded += (_, __) =>
            {
                ThemeToggle.IsChecked = _settings.IsDarkMode;
            };


            // Load settings once
            _settings = _settingsService.Load();

            // Apply theme immediately (so the window renders correctly)
            ThemeService.Apply(_settings.IsDarkMode);

            // Sync the toggle to settings without firing events
            _suppressThemeToggle = true;
            if (ThemeToggle != null)
                ThemeToggle.IsChecked = _settings.IsDarkMode;
            _suppressThemeToggle = false;
        }

        private void Warehouse_Click(object sender, RoutedEventArgs e)
        {
            // Prefer Tag (works even when Content is a StackPanel)
            string selected = "";

            if (sender is FrameworkElement fe)
                selected = fe.Tag?.ToString() ?? "";

            // Fallback: if someone uses a plain button with string Content
            if (string.IsNullOrWhiteSpace(selected) && sender is System.Windows.Controls.Button b)
                selected = b.Content?.ToString() ?? "";

            selected = (selected ?? "").Trim();
            if (selected.Length == 0) return;

            var main = new MainWindow(selected);
            main.Show();
            Close();
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
