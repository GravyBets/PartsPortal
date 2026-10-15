using PartsPortal.Contracts.Catalog;
using System.Windows;
using System.Windows.Controls;

namespace PartsPortal;

public partial class PartEditorWindow : Window
{
    private readonly IReadOnlyList<WarehouseDto> _warehouses;
    private readonly PartDto? _existingPart;
    private readonly Dictionary<int, CheckBox> _warehouseChecks = new();

    public PartEditorWindow(
        IReadOnlyList<WarehouseDto> warehouses,
        PartDto? existingPart = null)
    {
        _warehouses = warehouses;
        _existingPart = existingPart;

        InitializeComponent();
        BuildWarehouseChoices();
        LoadExistingPart();
    }

    public UpdatePartRequest? Request { get; private set; }

    private void BuildWarehouseChoices()
    {
        foreach (var warehouse in _warehouses
                     .Where(x => x.IsActive)
                     .OrderBy(x => x.SortOrder)
                     .ThenBy(x => x.Name))
        {
            var label = string.IsNullOrWhiteSpace(warehouse.Subtitle)
                ? warehouse.Name
                : $"{warehouse.Name} - {warehouse.Subtitle}";

            var checkBox = new CheckBox
            {
                Content = label,
                Tag = warehouse.Id,
                Margin = new Thickness(0, 6, 0, 6),
                Foreground = (System.Windows.Media.Brush)FindResource("TextPrimary")
            };

            _warehouseChecks[warehouse.Id] = checkBox;
            WarehousePanel.Children.Add(checkBox);
        }
    }

    private void LoadExistingPart()
    {
        if (_existingPart is null)
            return;

        HeaderText.Text = "Edit Part";
        Title = "Edit Part";

        MaterialTextBox.Text = _existingPart.MaterialNumber;
        DescriptionTextBox.Text = _existingPart.Description;

        foreach (var warehouse in _existingPart.Warehouses)
        {
            if (_warehouseChecks.TryGetValue(warehouse.Id, out var checkBox))
                checkBox.IsChecked = true;
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        var material = (MaterialTextBox.Text ?? "").Trim();
        var description = (DescriptionTextBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(material))
        {
            MessageBox.Show(
                "Material number is required.",
                "Part",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            MaterialTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            MessageBox.Show(
                "Description is required.",
                "Part",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DescriptionTextBox.Focus();
            return;
        }

        var warehouseIds = _warehouseChecks
            .Where(x => x.Value.IsChecked == true)
            .Select(x => x.Key)
            .OrderBy(x => x)
            .ToList();

        if (warehouseIds.Count == 0)
        {
            MessageBox.Show(
                "Select at least one warehouse.",
                "Part",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        Request = new UpdatePartRequest(
            material,
            description,
            warehouseIds,
            Environment.UserName);

        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
