using PartsPortal.Contracts.Access;
using PartsPortal.Models;
using PartsPortal.Services;
using System;
using System.ComponentModel;
using System.Linq;
using System.Windows;

namespace PartsPortal
    {
    public partial class WarehouseSelectWindow : Window
    {
        private readonly SettingsService _settingsService = new();
        private readonly AccessApiService _accessService = new();
        private UserSettings _settings = new();
        private CurrentUserAccessDto? _currentAccess;
        private bool _isShuttingDown;

        private bool _suppressThemeSelection = true;

        public WarehouseSelectWindow()
        {
            // Load settings once
            _settings = _settingsService.Load();

            // App startup applies the saved theme before any window renders.
            InitializeComponent();

            ThemeSelector.ItemsSource = ThemeService.ThemeOptions;
            ThemeSelector.SelectedItem = ThemeService.ThemeOptions
                .FirstOrDefault(option => option.Theme == ThemeService.Current);
            _suppressThemeSelection = false;

            Closing += WarehouseSelectWindow_Closing;
            Loaded += WarehouseSelectWindow_Loaded;
        }

        private async void WarehouseSelectWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            await RefreshAccessAsync();
        }

        private async Task RefreshAccessAsync()
        {
            try
            {
                _currentAccess = await _accessService.GetCurrentAsync();

                ManagePartsButton.Visibility =
                    _currentAccess?.CanManageParts == true
                        ? Visibility.Visible
                        : Visibility.Collapsed;

                UserAccessButton.Visibility =
                    _currentAccess?.CanManageUsers == true
                        ? Visibility.Visible
                        : Visibility.Collapsed;
            }
            catch
            {
                _currentAccess = null;
                ManagePartsButton.Visibility = Visibility.Collapsed;
                UserAccessButton.Visibility = Visibility.Collapsed;
            }
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


        private async void OpenBackorders()
        {
            _settings = _settingsService.Load();

            var partsService = new PartsCatalogService();
            var result = await partsService.LoadBestAvailableAsync();
            var allParts = result.Parts.ToList();

            if (allParts.Count == 0)
            {
                MessageBox.Show(
                    result.Warning ??
                    "The parts catalog is unavailable. Check network connectivity and try again.",
                    "Parts Catalog",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var dlg = new BackordersWindow(allParts, _settings)
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            Hide();

            try
            {
                dlg.ShowDialog();
            }
            finally
            {
                Show();
                Activate();
            }
        }

        private void ThemeSelector_SelectionChanged(
            object sender,
            System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (_suppressThemeSelection ||
                ThemeSelector.SelectedItem is not AppThemeOption selected)
                return;

            ThemeService.Apply(selected.Theme);
            _settings.IsDarkMode = ThemeService.IsDark;
        }

        private void ManageParts_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAccess?.CanManageParts != true)
            {
                MessageBox.Show(
                    "You do not have permission to manage parts.",
                    "Parts Management",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var window = new PartsManagementWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            window.ShowDialog();
        }

        private async void UserAccess_Click(object sender, RoutedEventArgs e)
        {
            if (_currentAccess?.CanManageUsers != true)
            {
                MessageBox.Show(
                    "Administrator access is required.",
                    "User Access",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var window = new UserAccessWindow
            {
                Owner = this,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };

            window.ShowDialog();
            await RefreshAccessAsync();
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
