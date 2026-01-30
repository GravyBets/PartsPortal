using MaterialReqAppV3.Models;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Syncfusion.Pdf.Parsing;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf;





namespace MaterialReqAppV3
{
    public partial class MainWindow : Window
    {
        public string SelectedWarehouse { get; }
        private TextBox? SiteNameBox;
        private readonly SettingsService _settingsService = new();
        private UserSettings _settings = new();
        private readonly PartsCatalogService _partsService = new();
        private List<Part> _allParts = new();
        private string _currentWarehouse = "Building D"; // temporary default
        private readonly Dictionary<TabItem, ObservableCollection<SelectedPartLine>> _selectedPartsByTab = new();
        private readonly Dictionary<TabItem, StackPanel> _movementPanelByTab = new();
        private readonly Dictionary<TabItem, string> _movementTypeByTab = new();
        private readonly Dictionary<TabItem, string> _costCenterByTab = new();
        private readonly Dictionary<TabItem, string> _wbsByTab = new();
        private readonly Dictionary<TabItem, string> _workOrderByTab = new();

        private readonly Dictionary<TabItem, TextBox> _detailsBoxByTab = new();

        private bool _suppressDetailsTextChanged = false;


        private bool GetIsReturnForTab(TabItem tab)
        {
            if (tab.Tag is bool b) return b;
            if (bool.TryParse(tab.Tag?.ToString(), out var parsed)) return parsed;
            return false; // default Issue
        }

        private string GetMovementType(TabItem tab)
            => _movementTypeByTab.TryGetValue(tab, out var v) ? v : "";

        private void SetMovementType(TabItem tab, string value)
            => _movementTypeByTab[tab] = value;

        private void BuildMovementOptions(TabItem tab)
        {
            if (!_movementPanelByTab.TryGetValue(tab, out var panel))
                return;

            bool isReturn = GetIsReturnForTab(tab);

            string[] options = isReturn
                ? new[]
                {
            "202 - Consumption for Cost Center Reversal",
            "222 - Consumption for Project Reversal",            
            "262 - Consumption for Order Reversal",
            "962 - Consumption for Order Reversal - Used"
                }
                : new[]
                {
            "201 - Consumption for Cost Center",
            "221 - Consumption for Project",            
            "261 - Consumption for Order"
                };

            string defaultOption = isReturn
                ? "262 - Consumption for Order Reversal"
                : "261 - Consumption for Order";

            // If current selection isn't valid for this mode, snap to default
            string current = GetMovementType(tab);
            if (string.IsNullOrWhiteSpace(current) || !options.Contains(current))
                SetMovementType(tab, defaultOption);

            panel.Children.Clear();

            panel.Children.Add(new TextBlock
            {
                Text = "Movement Type",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            foreach (var opt in options)
            {
                var rb = new RadioButton
                {
                    Content = opt,
                    GroupName = "MovementType_" + tab.GetHashCode(), // isolates per tab
                    Margin = new Thickness(0, 4, 0, 4),
                    IsChecked = string.Equals(GetMovementType(tab), opt, StringComparison.OrdinalIgnoreCase)
                };

                rb.Checked += (_, __) =>
                {
                    SetMovementType(tab, opt);
                    UpdateDetailsBoxForTab(tab);
                };


                panel.Children.Add(rb);
            }
        }

        private ObservableCollection<SelectedPartLine> GetSelectedParts(TabItem tab)
        {
            if (!_selectedPartsByTab.TryGetValue(tab, out var list))
            {
                list = new ObservableCollection<SelectedPartLine>();
                _selectedPartsByTab[tab] = list;
            }
            return list;
        }

        public MainWindow(string selectedWarehouse)
        {
            InitializeComponent();

            // 1) Warehouse + header first
            SelectedWarehouse = selectedWarehouse;
            _currentWarehouse = NormalizeWarehouse(selectedWarehouse);

            Title = $"Material Requisition - {SelectedWarehouse}";
            HeaderText.Text = $"Material Requisition - {SelectedWarehouse}";

            // 2) Load settings + parts
            _settings = _settingsService.Load();
            TryLoadParts();

            // 3) After UI is ready
            Loaded += (_, __) =>
            {
                // Grab the templated SiteName box
                SiteNameBox = (TextBox?)SiteTabs.Template.FindName("PART_SiteNameBox", SiteTabs);

                // First tab header uses hover-close header + empty reason (watermark)
                if (SiteTabs.Items.Count > 0 && SiteTabs.Items[0] is TabItem blankTab)
                {
                    blankTab.Header = CreateTabHeader("Blank", blankTab);
                    SetTabReason(blankTab, "");
                    blankTab.Content = CreateTabContentLayout(blankTab);
                }
                

                // Show current tab's reason in textbox
                if (SiteNameBox != null && SiteTabs.SelectedItem is TabItem selected && selected != PlusTab)
                    SiteNameBox.Text = GetTabReason(selected);

                // Enter = Apply
                if (SiteNameBox != null)
                {
                    SiteNameBox.KeyDown += (s, e) =>
                    {
                        if (e.Key == System.Windows.Input.Key.Enter)
                        {
                            ApplySiteName_Click(SiteNameBox, new RoutedEventArgs());
                            e.Handled = true;
                        }
                    };
                }

                // Now that PartsListBox exists, populate it
                RefreshPartsList();

                // OPTIONAL: keep only if you still want the template existence check.
                // If it's annoying now, just delete this block.
                var issue = GetTemplatePath(isReturn: false);
                var ret = GetTemplatePath(isReturn: true);

                if (!System.IO.File.Exists(issue))
                    MessageBox.Show("Issue template missing:\n" + issue);

                if (!System.IO.File.Exists(ret))
                    MessageBox.Show("Return template missing:\n" + ret);
            };
        }

        private void ApplySiteName_Click(object sender, RoutedEventArgs e)
        {
            if (SiteNameBox == null) return;
            if (SiteTabs.SelectedItem is not TabItem tab) return;
            if (tab == PlusTab) return;

            string name = (SiteNameBox.Text ?? "").Trim();

            // Save per-tab text (empty allowed; empty will show watermark)
            SetTabReason(tab, name);

            // If empty, don't rename the tab
            if (name.Length == 0) return;

            // Rename visible tab header
            if (tab.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb)
                tb.Text = name;
            else
                tab.Header = name;
        }

        private void SiteTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (SiteTabs.SelectedItem == null) return;

            if (SiteTabs.SelectedItem == PlusTab)
            {
                SiteTabs.SelectedIndex = SiteTabs.Items.Count - 2;
                return;
            }

            if (SiteTabs.SelectedItem is TabItem tab && tab != PlusTab)
            {
                if (SiteNameBox != null)
                {
                    SiteNameBox.Text = GetTabReason(tab); // empty => watermark shows
                    SiteNameBox.SelectAll();
                }

                BuildMovementOptions(tab);
            }

            Title = System.IO.Path.GetFileName(GetSelectedTabTemplatePath());
        }



        private TabItem CreateSiteTab(string header)
        {
            var tab = new TabItem();
            tab.Tag = false; // default: Material Issue
            tab.Header = CreateTabHeader(header, tab);
            tab.Content = CreateTabContentLayout(tab);
            SetTabReason(tab, "");
            return tab;
        }

        private object CreateTabHeader(string headerText, TabItem ownerTab)
        {
            var panel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            var text = new TextBlock
            {
                Text = headerText,
                Margin = new Thickness(0, 0, 6, 0),
                VerticalAlignment = VerticalAlignment.Center
            };

            var closeHit = new Border
            {
                Style = (Style)FindResource("TabCloseHitStyle"),
                VerticalAlignment = VerticalAlignment.Center
            };

            var closeText = new TextBlock
            {
                Text = "×",
                FontSize = 14,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            closeHit.Child = closeText;

            // IMPORTANT: Use mouse down so TabControl doesn't swallow it
            closeHit.MouseLeftButtonDown += (s, e) =>
            {
                e.Handled = true;

                int siteTabs = SiteTabs.Items.Count - 1; // exclude the + tab
                if (siteTabs <= 1)
                {
                    // no message boxes
                    return;
                }

                // Index of the tab being closed (before removal)
                int closingIndex = SiteTabs.Items.IndexOf(ownerTab);

                // cleanup per-tab state before removing the tab
                _selectedPartsByTab.Remove(ownerTab);
                _movementTypeByTab.Remove(ownerTab);
                _movementPanelByTab.Remove(ownerTab);

                _detailsBoxByTab.Remove(ownerTab);
                _costCenterByTab.Remove(ownerTab);
                _wbsByTab.Remove(ownerTab);
                _workOrderByTab.Remove(ownerTab);

                // Remove it
                SiteTabs.Items.Remove(ownerTab);

                // After removal, last real tab index (since + is last)
                int lastRealIndex = SiteTabs.Items.Count - 2;

                // Chrome behavior:
                // - select the tab that shifted into the closed tab's spot (same index)
                // - if we closed the last real tab, select the new last real tab (to the left)
                int newIndex = closingIndex;
                if (newIndex > lastRealIndex) newIndex = lastRealIndex;
                if (newIndex < 0) newIndex = 0;

                SiteTabs.SelectedIndex = newIndex;

                // Keep Reason textbox + movement/details in sync
                if (SiteTabs.SelectedItem is TabItem selected && selected != PlusTab)
                {
                    if (SiteNameBox != null)
                    {
                        SiteNameBox.Text = GetTabReason(selected); // <-- reason text (watermark if empty)
                        SiteNameBox.SelectAll();
                    }

                    BuildMovementOptions(selected);
                    UpdateDetailsBoxForTab(selected);
                }
            };




            panel.Children.Add(text);
            panel.Children.Add(closeHit);

            return panel;  
        }

        //Fixes the weird bug that made me click off 3rd tab before opening 4th
        private void SiteTabs_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Walk up the visual tree to find the TabItem that was clicked
            DependencyObject? current = e.OriginalSource as DependencyObject;

            while (current != null && current is not TabItem)
                current = VisualTreeHelper.GetParent(current);

            if (current == null) return;

            // If the user clicked the + tab, create a new tab immediately
            if (current == PlusTab)
            {
                e.Handled = true;
                AddNewSiteTab();   // <- this should be your method that inserts Blank 2/3/4 before PlusTab
            }
        }

        private const int MaxSiteTabs = 4;

        private void AddNewSiteTab()
        {
            int currentSiteTabCount = SiteTabs.Items.Count - 1; // excluding +

            if (currentSiteTabCount >= MaxSiteTabs)
            {
                MessageBox.Show("Max of 4 site tabs.");
                // go back to last real tab
                SiteTabs.SelectedIndex = SiteTabs.Items.Count - 2;
                return;
            }

            int newNumber = currentSiteTabCount + 1;
            string header = newNumber == 1 ? "Blank" : $"Blank {newNumber}";

            var newTab = CreateSiteTab(header);
            SiteTabs.Items.Insert(SiteTabs.Items.Count - 1, newTab);
            SiteTabs.SelectedItem = newTab;

            SetTabReason(newTab, ""); // start empty so watermark shows

            if (SiteNameBox != null)
            {
                SiteNameBox.Text = "";
                SiteNameBox.Focus();
            }


        }

        private string GetTabTitle(TabItem tab)
        {
            if (tab.Header is StackPanel sp && sp.Children.Count > 0 && sp.Children[0] is TextBlock tb)
                return tb.Text;

            return tab.Header?.ToString() ?? "";
        }

        private UIElement CreateTabContentLayout(TabItem ownerTab)
        {
            // New layout:
            // Left column = Parts Browser (spans full height)
            // Right column = Top: Movement+Inputs, Bottom: Selected Parts

            var grid = new Grid { Margin = new Thickness(10) };

            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                     // top-right card height
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // bottom-right card
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // left
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // right

            Border MakeCard(UIElement child, Thickness margin)
            {
                return new Border
                {
                    Background = (System.Windows.Media.Brush)FindResource("SurfaceBg"),
                    BorderBrush = (System.Windows.Media.Brush)FindResource("SurfaceBorder"),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = margin,
                    Child = child
                };
            }

            // ============================================================
            // LEFT: Parts Browser (search + list + Add button) spans rows 0-1
            // ============================================================

            var partsPanel = new Grid();
            partsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                         // search
            partsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // list
            partsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                         // button

            var partsSearch = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 10),
                Tag = "Search parts..."
            };
            partsSearch.Style = (Style)FindResource("WatermarkTextBoxStyle");

            var partsList = new ListBox
            {
                BorderThickness = new Thickness(0),
                ItemTemplate = (DataTemplate)FindResource("PartItemTemplate")
            };

            var addToOrderBtn = new Button
            {
                Content = "Add to Order",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };

            partsPanel.Children.Add(partsSearch);

            Grid.SetRow(partsList, 1);
            partsPanel.Children.Add(partsList);

            Grid.SetRow(addToOrderBtn, 2);
            partsPanel.Children.Add(addToOrderBtn);

            void RefreshParts()
            {
                string q = (partsSearch.Text ?? "").Trim();

                var filtered = _allParts
                    .Where(p => string.Equals(p.Warehouse, _currentWarehouse, StringComparison.OrdinalIgnoreCase))
                    .Where(p =>
                        string.IsNullOrWhiteSpace(q) ||
                        (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (p.Material?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                    .Take(250)
                    .ToList();

                partsList.ItemsSource = filtered;
            }

            partsSearch.TextChanged += (_, __) => RefreshParts();

            partsList.MouseDoubleClick += (_, __) =>
            {
                if (partsList.SelectedItem is Part part)
                    AddPartToOrder(ownerTab, part);
            };

            addToOrderBtn.Click += (_, __) =>
            {
                if (partsList.SelectedItem is Part part)
                    AddPartToOrder(ownerTab, part);
            };

            RefreshParts();

            var partsCard = MakeCard(partsPanel, new Thickness(0, 0, 8, 0));
            Grid.SetRow(partsCard, 0);
            Grid.SetColumn(partsCard, 0);
            Grid.SetRowSpan(partsCard, 2); // <-- fills the empty top-left space now
            grid.Children.Add(partsCard);

            // ============================================================
            // TOP-RIGHT: Movement Types + Inputs (same card)
            // ============================================================

            var topRightLayout = new Grid();
            topRightLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) }); // movement list
            topRightLayout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }); // text inputs

            // Movement list panel (dynamic per Issue/Return)
            var movementPanel = new StackPanel();
            _movementPanelByTab[ownerTab] = movementPanel;

            var movementHost = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0, 0, 12, 0),
                Child = movementPanel
            };
            Grid.SetColumn(movementHost, 0);
            topRightLayout.Children.Add(movementHost);

            // Inputs panel (single Details box that changes based on movement type)
            var inputPanel = new StackPanel();

            inputPanel.Children.Add(new TextBlock
            {
                Text = "Details",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 10)
            });

            var detailsBox = new TextBox
            {
                Height = 28,
                Tag = "Work Order", // will be overwritten by UpdateDetailsBoxForTab(...)
                Margin = new Thickness(0, 0, 0, 0)
            };
            detailsBox.Style = (Style)FindResource("WatermarkTextBoxStyle");

            // store reference per tab so other code can update it
            _detailsBoxByTab[ownerTab] = detailsBox;

            // save text into the correct bucket for this tab based on current movement type
            detailsBox.TextChanged += (_, __) =>
            {
                if (_suppressDetailsTextChanged) return;

                string val = (detailsBox.Text ?? "").Trim();
                switch (GetRequiredField(ownerTab))
                {
                    case DetailField.CostCenter:
                        SetCostCenter(ownerTab, val);
                        break;

                    case DetailField.Wbs:
                        SetWbs(ownerTab, val);
                        break;

                    default:
                        SetWorkOrder(ownerTab, val);
                        break;
                }
            };

            inputPanel.Children.Add(detailsBox);

            Grid.SetColumn(inputPanel, 1);
            topRightLayout.Children.Add(inputPanel);


            var topRightCard = MakeCard(topRightLayout, new Thickness(8, 0, 0, 8));
            Grid.SetRow(topRightCard, 0);
            Grid.SetColumn(topRightCard, 1);
            grid.Children.Add(topRightCard);

            // Build movement options AFTER panel is registered
            BuildMovementOptions(ownerTab);
            UpdateDetailsBoxForTab(ownerTab);


            // ============================================================
            // BOTTOM-RIGHT: Selected Parts (grid + buttons)
            // ============================================================

            var selectedArea = new Grid();
            selectedArea.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            selectedArea.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var selectedGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                ItemsSource = GetSelectedParts(ownerTab),
                IsReadOnly = true // you set it read-only; keep it stable
            };

            // prevent weird crashes on double click
            selectedGrid.PreviewMouseDoubleClick += (_, e) => e.Handled = true;

            selectedGrid.FontSize = 14;
            selectedGrid.RowHeight = 44;
            selectedGrid.ColumnHeaderHeight = 30;
            selectedGrid.VerticalContentAlignment = VerticalAlignment.Center;

            selectedGrid.CellStyle = new Style(typeof(DataGridCell));
            selectedGrid.CellStyle.Setters.Add(new Setter(Control.PaddingProperty, new Thickness(8, 4, 8, 4)));
            selectedGrid.CellStyle.Setters.Add(new Setter(Control.VerticalContentAlignmentProperty, VerticalAlignment.Top));

            selectedGrid.Columns.Clear();

            // Qty centered (your existing style)
            var qtyTextStyle = new Style(typeof(TextBlock));
            qtyTextStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            qtyTextStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            qtyTextStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));

            selectedGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Qty",
                Width = 50,
                Binding = new Binding("Qty") { UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged },
                ElementStyle = qtyTextStyle,
                IsReadOnly = true
            });

            selectedGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Part",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = (DataTemplate)FindResource("SelectedPartDisplayTemplate"),
                IsReadOnly = true
            });

            // Buttons under Selected Parts (LEFT aligned)
            var btnRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var removeBtn = new Button
            {
                Content = "Remove Part",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(0, 0, 10, 0)
            };

            var clearBtn = new Button
            {
                Content = "Clear List",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0)
            };

            btnRow.Children.Add(removeBtn);
            btnRow.Children.Add(clearBtn);

            removeBtn.Click += (_, __) =>
            {
                if (selectedGrid.SelectedItem is not SelectedPartLine line) return;

                var list = GetSelectedParts(ownerTab);
                if (line.Qty > 1) line.Qty -= 1;
                else list.Remove(line);
            };

            clearBtn.Click += (_, __) => GetSelectedParts(ownerTab).Clear();

            // Delete key matches Remove Part behavior
            selectedGrid.PreviewKeyDown += (_, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Delete && selectedGrid.SelectedItem is SelectedPartLine line)
                {
                    var list = GetSelectedParts(ownerTab);
                    if (line.Qty > 1) line.Qty -= 1;
                    else list.Remove(line);

                    e.Handled = true;
                }
            };

            Grid.SetRow(selectedGrid, 0);
            selectedArea.Children.Add(selectedGrid);

            Grid.SetRow(btnRow, 1);
            selectedArea.Children.Add(btnRow);

            var selectedCard = MakeCard(selectedArea, new Thickness(8, 8, 0, 0));
            Grid.SetRow(selectedCard, 1);
            Grid.SetColumn(selectedCard, 1);
            grid.Children.Add(selectedCard);

            return grid;
        }


        private string GetTabReason(TabItem tab) => tab.ToolTip?.ToString() ?? "";
        private void SetTabReason(TabItem tab, string reason) => tab.ToolTip = reason;

        private string GetTemplatePath(bool isReturn)
        {
            string fileName = isReturn
                ? "Material_Requisition_Return_Fillable.pdf"
                : "Material_Requisition_Issue_Fillable.pdf";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory; // bin\Debug\net8.0-windows\
            return System.IO.Path.Combine(baseDir, "Templates", fileName);
        }        

        private string GetSelectedTabTemplatePath()
        {
            if (SiteTabs.SelectedItem is not TabItem tab || tab == PlusTab)
                return GetTemplatePath(isReturn: false); // safe fallback

            bool isReturn = GetIsReturnForTab(tab);
            return GetTemplatePath(isReturn);
        }

        private void GeneratePdf_Click(object sender, RoutedEventArgs e)
        {
            // Output folder (use settings if provided; otherwise bin\Output)
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            string outDir =
                !string.IsNullOrWhiteSpace(_settings?.PdfOutputFolder)
                    ? _settings.PdfOutputFolder.Trim()
                    : Path.Combine(baseDir, "Output");

            Directory.CreateDirectory(outDir);

            // Output filename uses selected tab reason/title (just like you had)
            string fileLabel = "MaterialReq";
            if (SiteTabs.SelectedItem is TabItem selectedTab && selectedTab != PlusTab)
            {
                fileLabel = (GetTabReason(selectedTab) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(fileLabel))
                    fileLabel = (GetTabTitle(selectedTab) ?? "").Trim();
            }

            foreach (char c in Path.GetInvalidFileNameChars())
                fileLabel = fileLabel.Replace(c, '_');

            string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            string outFile = Path.Combine(outDir, $"{fileLabel}_MERGED_{stamp}.pdf");

            // Create merged output PDF
            using var outDoc = new PdfDocument();

            bool addedAny = false;

            foreach (var item in SiteTabs.Items)
            {
                if (item is not TabItem tab) continue;
                if (tab == PlusTab) continue;

                // Optional: skip totally blank tabs (no parts + no DocHeader)
                var lines = GetSelectedParts(tab);
                string docHeader = (GetTabReason(tab) ?? "").Trim();
                if (lines.Count == 0 && string.IsNullOrWhiteSpace(docHeader))
                    continue;

                bool isReturn = GetIsReturnForTab(tab);
                string templatePath = GetTemplatePath(isReturn);
                if (!File.Exists(templatePath))
                    continue; // silent skip; no message boxes

                // MovementType code
                string mtFull = GetMovementType(tab) ?? "";
                string mtCode = ExtractMovementCode(mtFull);
                if (string.IsNullOrWhiteSpace(mtCode))
                    mtCode = isReturn ? "262" : "261";

                // Only ONE field filled based on mtCode
                string costCenter = "";
                string wbs = "";
                string workOrder = "";
                string workOrderUsed = "";

                if (mtCode == "201" || mtCode == "202")
                    costCenter = (GetCostCenter(tab) ?? "").Trim();
                else if (mtCode == "221" || mtCode == "222")
                    wbs = (GetWbs(tab) ?? "").Trim();
                else
                {
                    if (isReturn && mtCode == "962")
                        workOrderUsed = (GetWorkOrder(tab) ?? "").Trim();
                    else
                        workOrder = (GetWorkOrder(tab) ?? "").Trim();
                }

                using var loaded = new PdfLoadedDocument(templatePath);

                // Header
                //SetPdfTextField(loaded, "CompanyName", _settings?.CompanyName ?? "");
                SetPdfTextField(loaded, "Name", _settings?.Name ?? "");
                SetPdfTextField(loaded, "Date", DateTime.Now.ToString("MM/dd/yyyy"));
                SetPdfTextField(loaded, "DocHeader", docHeader);
                SetPdfTextField(loaded, "MovementType", mtCode);

                // Assignment
                SetPdfTextField(loaded, "CostCenter", costCenter);
                SetPdfTextField(loaded, "Wbs", wbs);
                SetPdfTextField(loaded, "WorkOrder", workOrder);
                SetPdfTextField(loaded, "WorkOrderUsed", workOrderUsed);

                // Lines 1..12
                for (int i = 1; i <= 12; i++)
                {
                    var line = (i - 1 < lines.Count) ? lines[i - 1] : null;

                    SetPdfTextField(loaded, $"DESCRIPTION{i}", line?.Description ?? "");
                    SetPdfTextField(loaded, $"MATERIAL{i}", line?.Material ?? "");
                    SetPdfTextField(loaded, $"QUANTITY{i}", line != null ? line.Qty.ToString() : "");
                }

                // Flatten for reliable printing
                if (loaded.Form != null)
                    loaded.Form.Flatten = true;

                // Import this filled page(s) into merged output
                outDoc.ImportPageRange(loaded, 0, loaded.Pages.Count - 1);

                addedAny = true;
            }

            if (!addedAny)
            {
                Title = "Nothing to generate (all tabs blank).";
                return;
            }

            using var fs = File.Create(outFile);
            outDoc.Save(fs);

            Title = "Created: " + outFile;
        }



        private string GetSettingsPath()
        {
            string user = Environment.UserName; // windows login username
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "MaterialReqAppV3");

            return Path.Combine(dir, $"settings_{user}.json");
        }

        private UserSettings LoadSettings()
        {
            string path = GetSettingsPath();
            string user = Environment.UserName;

            if (File.Exists(path))
            {
                try
                {
                    var json = File.ReadAllText(path);
                    var s = JsonSerializer.Deserialize<UserSettings>(json) ?? new UserSettings();
                    if (string.IsNullOrWhiteSpace(s.EmployeeId))
                        s.EmployeeId = user;
                    return s;
                }
                catch { /* ignore and fall back */ }
            }

            return new UserSettings { EmployeeId = user };
        }

        private void OpenSettings_Click(object sender, RoutedEventArgs e)
        {
            var win = new SettingsWindow(_settings) { Owner = this };
            bool? ok = win.ShowDialog();

            if (ok == true)
            {
                _settings = win.Settings;
                _settingsService.Save(_settings);

                System.Windows.MessageBox.Show("Settings saved!", "Settings",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }       

        private void TestCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var path = _settings?.CsvPath ?? "";
                var parts = _partsService.LoadFromCsv(path);

                var preview = string.Join("\n", parts.Take(5).Select(p => p.ToString()));
                MessageBox.Show($"Loaded {parts.Count} parts.\n\nFirst 5:\n{preview}");
            }
            catch (Exception ex)
            {
                MessageBox.Show("CSV load failed:\n" + ex.Message);
            }
        }

        private void TryLoadParts()
        {
            try
            {
                _allParts = _partsService.LoadFromCsv(_settings.CsvPath);
            }
            catch
            {
                _allParts = new List<Part>();
                // We'll show a friendly message later (or in the UI)
            }
        }

        private void RefreshPartsList()
        {
            if (PartsListBox == null) return;

            string q = (PartsSearchBox?.Text ?? "").Trim();

            var filtered = _allParts
                .Where(p => string.Equals(p.Warehouse, _currentWarehouse, StringComparison.OrdinalIgnoreCase))
                .Where(p =>
                    string.IsNullOrWhiteSpace(q) ||
                    (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                    (p.Material?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false))
                .Take(200) // keep it snappy for now
                .ToList();

            PartsListBox.ItemsSource = filtered;
        }

        private void PartsSearchBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            RefreshPartsList();
        }

        private string NormalizeWarehouse(string selectedWarehouse)
        {
            if (string.IsNullOrWhiteSpace(selectedWarehouse))
                return "Building D";

            // If passed "Warehouse - Building D", reduce to "Building D"
            int idx = selectedWarehouse.IndexOf("Building", StringComparison.OrdinalIgnoreCase);
            if (idx >= 0)
                return selectedWarehouse.Substring(idx).Trim();

            // If already "Building D", keep it
            return selectedWarehouse.Trim();
        }

        private void AddPartToOrder(TabItem ownerTab, Part part)
        {
            var list = GetSelectedParts(ownerTab);
            var existing = list.FirstOrDefault(x => x.Material == part.Material);

            if (existing != null)
                existing.Qty += 1;
            else
                list.Add(new SelectedPartLine(part));
        }

        private void IssueReturnToggle_Changed(object sender, RoutedEventArgs e)
        {
            if (SiteTabs.SelectedItem is not TabItem tab || tab == PlusTab)
                return;

            Dispatcher.BeginInvoke(() =>
            {
                BuildMovementOptions(tab);
                UpdateDetailsBoxForTab(tab);
            });
        }


        private enum DetailField { CostCenter, Wbs, WorkOrder }

        private DetailField GetRequiredField(TabItem tab)
        {
            string mt = GetMovementType(tab);

            if (mt.StartsWith("201") || mt.StartsWith("202"))
                return DetailField.CostCenter;

            if (mt.StartsWith("221") || mt.StartsWith("222"))
                return DetailField.Wbs;

            // 261/262/962 (and anything else) -> Work Order
            return DetailField.WorkOrder;
        }

        private string GetCostCenter(TabItem tab) => _costCenterByTab.TryGetValue(tab, out var v) ? v : "";
        private string GetWbs(TabItem tab) => _wbsByTab.TryGetValue(tab, out var v) ? v : "";
        private string GetWorkOrder(TabItem tab) => _workOrderByTab.TryGetValue(tab, out var v) ? v : "";

        private void SetCostCenter(TabItem tab, string v) => _costCenterByTab[tab] = v;
        private void SetWbs(TabItem tab, string v) => _wbsByTab[tab] = v;
        private void SetWorkOrder(TabItem tab, string v) => _workOrderByTab[tab] = v;

        private void UpdateDetailsBoxForTab(TabItem tab)
        {
            if (!_detailsBoxByTab.TryGetValue(tab, out var box))
                return;

            var field = GetRequiredField(tab);

            _suppressDetailsTextChanged = true;

            switch (field)
            {
                case DetailField.CostCenter:
                    box.Tag = "Cost Center";
                    box.Text = GetCostCenter(tab);
                    break;

                case DetailField.Wbs:
                    box.Tag = "WBS";
                    box.Text = GetWbs(tab);
                    break;

                default:
                    box.Tag = "Work Order";
                    box.Text = GetWorkOrder(tab);
                    break;
            }

            _suppressDetailsTextChanged = false;
        }

        private static string ExtractMovementCode(string movementText)
        {
            if (string.IsNullOrWhiteSpace(movementText)) return "";
            int dash = movementText.IndexOf('-');
            if (dash > 0) return movementText.Substring(0, dash).Trim();
            return movementText.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        }

        private static void SetPdfTextField(PdfLoadedDocument doc, string fieldName, string value)
        {
            if (doc.Form == null) return;

            foreach (PdfField f in doc.Form.Fields)
            {
                if (string.Equals(f.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
                    f is PdfLoadedTextBoxField tb)
                {
                    tb.Text = value ?? "";
                    return;
                }
            }
        }


    }
}