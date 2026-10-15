using PartsPortal.Contracts.Catalog;
using PartsPortal.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PartsPortal;

public partial class PartsManagementWindow : Window
{
    private readonly PartsAdminService _service = new();
    private readonly ObservableCollection<PartRow> _rows = new();
    private ICollectionView? _view;

    public PartsManagementWindow()
    {
        InitializeComponent();
        Loaded += PartsManagementWindow_Loaded;
    }

    private async void PartsManagementWindow_Loaded(object sender, RoutedEventArgs e)
    {
        await LoadPartsAsync();
    }

    private async Task LoadPartsAsync(int? selectPartId = null)
    {
        try
        {
            SetBusy(true, "Loading parts...");

            var parts = await _service.GetPartsAsync(
                includeInactive: IncludeInactiveCheckBox.IsChecked == true);

            _rows.Clear();

            foreach (var part in parts)
                _rows.Add(new PartRow(part));

            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterPart;
            PartsGrid.ItemsSource = _view;

            StatusText.Text = $"{_rows.Count} part{(_rows.Count == 1 ? "" : "s")} loaded.";

            if (selectPartId.HasValue)
            {
                var row = _rows.FirstOrDefault(x => x.Id == selectPartId.Value);
                if (row is not null)
                {
                    PartsGrid.SelectedItem = row;
                    PartsGrid.ScrollIntoView(row);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not load the parts catalog.\n\n" + ex.Message,
                "Parts Management",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "Catalog load failed.";
        }
        finally
        {
            SetBusy(false);
            UpdateSelectionButtons();
        }
    }

    private bool FilterPart(object item)
    {
        if (item is not PartRow row)
            return false;

        var query = (SearchBox.Text ?? "").Trim();

        if (query.Length == 0)
            return true;

        return Contains(row.MaterialNumber, query) ||
               Contains(row.Description, query) ||
               Contains(row.WarehouseDisplay, query) ||
               Contains(row.StatusDisplay, query);
    }

    private static bool Contains(string? source, string query)
        => !string.IsNullOrWhiteSpace(source) &&
           source.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _view?.Refresh();
    }

    private async void IncludeInactiveCheckBox_Changed(object sender, RoutedEventArgs e)
    {
        if (!IsLoaded)
            return;

        await LoadPartsAsync();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadPartsAsync((PartsGrid.SelectedItem as PartRow)?.Id);
    }

    private async void Add_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var warehouses = await _service.GetWarehousesAsync();

            var editor = new PartEditorWindow(warehouses)
            {
                Owner = this
            };

            if (editor.ShowDialog() != true || editor.Request is null)
                return;

            var created = await _service.CreatePartAsync(editor.Request);
            await LoadPartsAsync(created.Id);
        }
        catch (Exception ex)
        {
            ShowOperationError("add", ex);
        }
    }

    private async void Edit_Click(object sender, RoutedEventArgs e)
    {
        await EditSelectedAsync();
    }

    private async void PartsGrid_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (PartsGrid.SelectedItem is PartRow)
            await EditSelectedAsync();
    }

    private async Task EditSelectedAsync()
    {
        if (PartsGrid.SelectedItem is not PartRow row)
            return;

        try
        {
            var warehouses = await _service.GetWarehousesAsync();

            var editor = new PartEditorWindow(warehouses, row.Part)
            {
                Owner = this
            };

            if (editor.ShowDialog() != true || editor.Request is null)
                return;

            var updated = await _service.UpdatePartAsync(row.Id, editor.Request);
            await LoadPartsAsync(updated.Id);
        }
        catch (Exception ex)
        {
            ShowOperationError("edit", ex);
        }
    }

    private async void ActiveToggle_Click(object sender, RoutedEventArgs e)
    {
        if (PartsGrid.SelectedItem is not PartRow row)
            return;

        try
        {
            if (row.IsActive)
            {
                var answer = MessageBox.Show(
                    $"Deactivate this part?\n\n{row.MaterialNumber} - {row.Description}\n\n" +
                    "It will no longer appear in normal ordering lists, but its record will be retained.",
                    "Deactivate Part",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (answer != MessageBoxResult.Yes)
                    return;

                await _service.DeactivatePartAsync(row.Id);
            }
            else
            {
                await _service.ReactivatePartAsync(row.Id);
            }

            await LoadPartsAsync(row.Id);
        }
        catch (Exception ex)
        {
            ShowOperationError(row.IsActive ? "deactivate" : "reactivate", ex);
        }
    }

    private void PartsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateSelectionButtons();
    }

    private void UpdateSelectionButtons()
    {
        var row = PartsGrid.SelectedItem as PartRow;
        var hasSelection = row is not null;

        EditButton.IsEnabled = hasSelection;
        ActiveToggleButton.IsEnabled = hasSelection;
        ActiveToggleButton.Content = row?.IsActive == false
            ? "Reactivate"
            : "Deactivate";
    }

    private void SetBusy(bool busy, string? message = null)
    {
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;

        SearchBox.IsEnabled = !busy;
        IncludeInactiveCheckBox.IsEnabled = !busy;
        PartsGrid.IsEnabled = !busy;

        if (!string.IsNullOrWhiteSpace(message))
            StatusText.Text = message;
    }

    private void ShowOperationError(string operation, Exception ex)
    {
        MessageBox.Show(
            $"Could not {operation} the part.\n\n{ex.Message}",
            "Parts Management",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private sealed class PartRow
    {
        public PartRow(PartDto part)
        {
            Part = part;
        }

        public PartDto Part { get; }

        public int Id => Part.Id;
        public string MaterialNumber => Part.MaterialNumber;
        public string Description => Part.Description;
        public bool IsActive => Part.IsActive;

        public string WarehouseDisplay =>
            string.Join(", ",
                Part.Warehouses
                    .OrderBy(x => x.SortOrder)
                    .Select(x => x.Name));

        public string StatusDisplay => IsActive ? "Active" : "Inactive";
    }
}
