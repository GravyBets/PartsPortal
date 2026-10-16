using System.IO;
using System.Text.Json;
using System.Windows;

namespace PartsPortal
{
    public partial class ChangeLogWindow : Window
    {
        public ChangeLogWindow()
        {
            InitializeComponent();

            Loaded += ChangeLogWindow_Loaded;
        }

        private void ChangeLogWindow_Loaded(
            object sender,
            RoutedEventArgs e)
        {
            try
            {
                var entries = LoadChangeLog();
                ChangeLogItemsControl.ItemsSource = entries;
                EmptyStateText.Visibility = entries.Count == 0
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                EmptyStateText.Text = "The change log could not be loaded.";
                EmptyStateText.Visibility = Visibility.Visible;

                MessageBox.Show(
                    $"Unable to load the change log.\n\n{ex.Message}",
                    "Change Log",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
        }

        private static IReadOnlyList<ChangeLogEntry> LoadChangeLog()
        {
            var resourceUri =
                new Uri(
                    "pack://application:,,,/Assets/Data/ChangeLog.json",
                    UriKind.Absolute);

            var resource =
                Application.GetResourceStream(resourceUri);

            if (resource == null)
            {
                throw new FileNotFoundException(
                    "The embedded change-log file was not found.");
            }

            using var reader =
                new StreamReader(resource.Stream);

            var json =
                reader.ReadToEnd();

            return JsonSerializer.Deserialize<List<ChangeLogEntry>>(
                       json,
                       new JsonSerializerOptions
                       {
                           PropertyNameCaseInsensitive = true
                       })
                   ?? [];
        }

        private void Close_Click(
            object sender,
            RoutedEventArgs e)
        {
            Close();
        }

        private sealed class ChangeLogEntry
        {
            public string Version { get; set; } = "";

            public string Date { get; set; } = "";

            public string Title { get; set; } = "";

            public List<string> Changes { get; set; } = [];
        }
    }
}