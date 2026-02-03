using MaterialReqAppV3.Models;
using MaterialReqAppV3.Services;
using Syncfusion.Pdf;
using Syncfusion.Pdf.Interactive;
using Syncfusion.Pdf.Parsing;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Principal;
using System.Runtime.Versioning;




namespace MaterialReqAppV3
{
    [SupportedOSPlatform("windows")]
    public partial class MainWindowModern : Window
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
        private readonly Services.FavoritesService _favoritesService = new();
        private HashSet<string> _favoriteMaterials = new(StringComparer.OrdinalIgnoreCase);
        private readonly TemplatesService _templatesService = new();
        private List<PartTemplate> _templates = new();
        private readonly List<ListBox> _templateLists = new();
        private readonly List<(ListBox Names, DataGrid Lines)> _templateUIs = new();
        private readonly Dictionary<TabItem, TextBox> _detailsBoxByTab = new();
        private bool _suppressDetailsTextChanged = false;
        private const int MaxLineItemsPerTab = 12;
        private readonly Dictionary<TabItem, TextBlock> _selectedStatusByTab = new();


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

        public MainWindowModern(string selectedWarehouse)
        {
            InitializeComponent();

            //Message Window if App is running in Adminstrator Mode
            if (IsAppElevated())
            {
                MessageBox.Show(
                    "This app is running as Administrator.\n\n" +
                    "If Outlook is NOT running as Administrator, emailing may fail.\n\n" +
                    "Fix: close this app and reopen it normally (do not 'Run as administrator').",
                    "Elevation Warning",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }


            // 1) Warehouse + header first
            SelectedWarehouse = selectedWarehouse;
            _currentWarehouse = NormalizeWarehouse(selectedWarehouse);

            Title = $"Material Requisition - {SelectedWarehouse}";
            HeaderText.Text = $"Material Requisition - {SelectedWarehouse}";

            // 2) Load settings + parts
            _settings = _settingsService.Load();
            TryLoadParts();
            RefreshPartsList();

            // Load Favorites
            _favoriteMaterials = _favoritesService.Load();

            //Templates
            if (string.IsNullOrWhiteSpace(_currentWarehouse))
                _currentWarehouse = SelectedWarehouse; // or whatever your fallback should be

            _templates = _templatesService.Load(GetWarehouseKey());

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
                UpdateTabCloseButtonsVisibility();
                

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
                UpdateDetailsBoxForTab(tab);
            }           
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
                VerticalAlignment = VerticalAlignment.Center,
                Tag = "CloseHit"
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
                _selectedStatusByTab.Remove(ownerTab);


                // Remove it
                SiteTabs.Items.Remove(ownerTab);
                UpdateTabCloseButtonsVisibility();


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

            string header = GetNextDefaultBlankTitle();


            var newTab = CreateSiteTab(header);
            SiteTabs.Items.Insert(SiteTabs.Items.Count - 1, newTab);
            SiteTabs.SelectedItem = newTab;
            UpdateTabCloseButtonsVisibility();


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
                var b = new Border
                {
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = margin,
                    Child = child
                };

                // ✅ Dynamic theme tracking
                b.SetResourceReference(Border.BackgroundProperty, "SurfaceBg");
                b.SetResourceReference(Border.BorderBrushProperty, "SurfaceBorder");

                return b;
            }


            // ============================================================
            // LEFT: Parts Browser (search + tabs + Add button) spans rows 0-1
            // ============================================================

            var partsPanel = new Grid();
            partsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                         // search
            partsPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });   // tabs
            partsPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });                         // button

            var partsSearch = new TextBox
            {
                Height = 28,
                Margin = new Thickness(0, 0, 0, 10),
                Tag = "Search parts..."
            };
            partsSearch.Style = (Style)FindResource("WatermarkTextBoxStyle");

            // ---- Tabs container
            var partsTabs = new TabControl
            {
                Margin = new Thickness(0),
                Padding = new Thickness(0)
            };
            Grid.SetRow(partsTabs, 1);

            // ---- All parts list
            var allPartsList = new ListBox
            {
                BorderThickness = new Thickness(0),
                ItemTemplate = (DataTemplate)FindResource("PartItemTemplate")
            };

            // ---- Favorites list
            var favPartsList = new ListBox
            {
                BorderThickness = new Thickness(0),
                ItemTemplate = (DataTemplate)FindResource("PartItemTemplate")
            };

            // ---- Templates UI: left = names, right = preview
            var templatesPanel = new Grid();
            templatesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
            templatesPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            templatesPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            templatesPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            // Names list (LEFT)
            var templatesList = new ListBox
            {
                BorderThickness = new Thickness(0),
                DisplayMemberPath = "Name"
            };
            templatesList.ItemContainerStyle = new Style(typeof(ListBoxItem))
            {
                Setters =
                {
                    new Setter(Control.FontSizeProperty, 14.0),
                    new Setter(Control.PaddingProperty, new Thickness(6, 4, 6, 4))
                }
            };
            Grid.SetRow(templatesList, 0);
            Grid.SetColumn(templatesList, 0);

            // Preview grid (RIGHT)
            var templateLinesGrid = new DataGrid
            {
                AutoGenerateColumns = false,
                CanUserAddRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                IsReadOnly = true,
                RowHeight = 44,
                FontSize = 14,
                ColumnHeaderHeight = 30
            };
            Grid.SetRow(templateLinesGrid, 0);
            Grid.SetColumn(templateLinesGrid, 1);

            // Columns: Qty + Part (2-line template)
            templateLinesGrid.Columns.Clear();

            var qtyStyle = new Style(typeof(TextBlock));
            qtyStyle.Setters.Add(new Setter(TextBlock.TextAlignmentProperty, TextAlignment.Center));
            qtyStyle.Setters.Add(new Setter(TextBlock.VerticalAlignmentProperty, VerticalAlignment.Center));
            qtyStyle.Setters.Add(new Setter(TextBlock.HorizontalAlignmentProperty, HorizontalAlignment.Center));

            templateLinesGrid.Columns.Add(new DataGridTextColumn
            {
                Header = "Qty",
                Width = 50,
                Binding = new Binding("Qty"),
                ElementStyle = qtyStyle
            });

            templateLinesGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "Part",
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                CellTemplate = (DataTemplate)FindResource("SelectedPartDisplayTemplate")
            });

            

            void RefreshTemplatesUI(string? selectName = null)
            {
                var sorted = _templates.OrderBy(t => t.Name).ToList();

                templatesList.ItemsSource = null;
                templatesList.ItemsSource = sorted;

                string? pick = selectName ?? (templatesList.SelectedItem as PartTemplate)?.Name;
                if (!string.IsNullOrWhiteSpace(pick))
                {
                    var match = sorted.FirstOrDefault(t =>
                        string.Equals(t.Name, pick, StringComparison.OrdinalIgnoreCase));

                    templatesList.SelectedItem = match;
                    templateLinesGrid.ItemsSource = match?.Lines;
                }
                else
                {
                    templateLinesGrid.ItemsSource = null;
                }
            }


            // When you click a template name, show its lines on the right
            templatesList.SelectionChanged += (_, __) =>
            {
                if (templatesList.SelectedItem is PartTemplate t)
                    templateLinesGrid.ItemsSource = t.Lines;
                else
                    templateLinesGrid.ItemsSource = null;
            };

            // Load into Selected Parts
            void AddTemplateToOrder(PartTemplate t)
            {
                var selected = GetSelectedParts(ownerTab);

                foreach (var line in t.Lines)
                {
                    // match by material number when possible
                    var existing = selected.FirstOrDefault(x =>
                        !string.IsNullOrWhiteSpace(line.Material) &&
                        string.Equals(x.Material, line.Material, StringComparison.OrdinalIgnoreCase));

                    if (existing != null)
                    {
                        existing.Qty += line.Qty;
                        continue;
                    }

                    // add a new line
                    var part = new Part
                    {
                        Material = line.Material,
                        Description = line.Description,
                        Warehouse = SelectedWarehouse
                    };

                    var sel = new SelectedPartLine(part);
                    sel.Qty = line.Qty;
                    selected.Add(sel);
                }
            } 

            // Add to panel
            templatesPanel.Children.Add(templatesList);
            templatesPanel.Children.Add(templateLinesGrid);

            // Add tabs in the CORRECT order (indexes matter)
            partsTabs.Items.Add(new TabItem { Header = "All Parts", Content = allPartsList });
            partsTabs.Items.Add(new TabItem { Header = "Favorites", Content = favPartsList });
            partsTabs.Items.Add(new TabItem { Header = "Templates", Content = templatesPanel });

            // Initial fill
            RefreshTemplatesUI();



            // Right-click should select item under mouse (WPF doesn’t do this by default)
            void SelectOnRightClick(ListBox lb)
            {
                lb.PreviewMouseRightButtonDown += (_, e) =>
                {
                    var dep = (DependencyObject)e.OriginalSource;
                    var container = ItemsControl.ContainerFromElement(lb, dep) as ListBoxItem;
                    if (container != null)
                        lb.SelectedItem = container.DataContext;
                };
            }
            SelectOnRightClick(allPartsList);
            SelectOnRightClick(favPartsList);

            // Context menu for All Parts (add/remove favorite)
            var addFav = new MenuItem { Header = "Add to Favorites" };
            addFav.Click += (_, __) =>
            {
                if (allPartsList.SelectedItem is not Part p) return;
                if (string.IsNullOrWhiteSpace(p.Material)) return;

                _favoriteMaterials.Add(p.Material);
                _favoritesService.Save(_favoriteMaterials);
                RefreshParts();
            };

            var removeFavFromAll = new MenuItem { Header = "Remove from Favorites" };
            removeFavFromAll.Click += (_, __) =>
            {
                if (allPartsList.SelectedItem is not Part p) return;
                if (string.IsNullOrWhiteSpace(p.Material)) return;

                _favoriteMaterials.Remove(p.Material);
                _favoritesService.Save(_favoriteMaterials);
                RefreshParts();
            };

            allPartsList.ContextMenu = new ContextMenu();
            allPartsList.ContextMenu.Items.Add(addFav);
            allPartsList.ContextMenu.Items.Add(removeFavFromAll);

            // Context menu for Favorites list (remove favorite)
            var removeFav = new MenuItem { Header = "Remove from Favorites" };
            removeFav.Click += (_, __) =>
            {
                if (favPartsList.SelectedItem is not Part p) return;
                if (string.IsNullOrWhiteSpace(p.Material)) return;

                _favoriteMaterials.Remove(p.Material);
                _favoritesService.Save(_favoriteMaterials);
                RefreshParts();
            };

            favPartsList.ContextMenu = new ContextMenu();
            favPartsList.ContextMenu.Items.Add(removeFav);

            // Add-to-order behavior (double click works on both lists)
            void WireAddToOrder(ListBox lb)
            {
                lb.MouseDoubleClick += (_, __) =>
                {
                    if (lb.SelectedItem is Part part)
                        AddPartToOrder(ownerTab, part);
                };
            }
            WireAddToOrder(allPartsList);
            WireAddToOrder(favPartsList);

            // --- Button row (left aligned): Add to Order + Add to Favorites + Remove from Favorites (favorites tab only)
            var buttonsRow = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 10, 0, 0)
            };
            Grid.SetRow(buttonsRow, 2);

            // Helper: get selected Part from the active tab list
            Part? GetActiveSelectedPart()
            {
                if (partsTabs.SelectedIndex == 0) return allPartsList.SelectedItem as Part;
                if (partsTabs.SelectedIndex == 1) return favPartsList.SelectedItem as Part;
                return null;
            }

            var addToOrderBtn = new Button
            {
                Content = "Add to Order",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0)
            };

            var addToFavBtn = new Button
            {
                Content = "Add to Favorites",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0)
            };

            var removeFromFavBtn = new Button
            {
                Content = "Remove from Favorites",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };
            var deleteTemplateBtnTop = new Button
            {
                Content = "Delete Template",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0),
                Visibility = Visibility.Collapsed
            };

            UpdateButtonsForTab();


            addToOrderBtn.Click += (_, __) =>
            {
                // All Parts tab
                if (partsTabs.SelectedIndex == 0)
                {
                    if (allPartsList.SelectedItem is Part part)
                        AddPartToOrder(ownerTab, part);
                    return;
                }

                // Favorites tab
                if (partsTabs.SelectedIndex == 1)
                {
                    if (favPartsList.SelectedItem is Part part)
                        AddPartToOrder(ownerTab, part);
                    return;
                }

                // Templates tab
                if (partsTabs.SelectedIndex == 2)
                {
                    if (templatesList.SelectedItem is PartTemplate t)
                        AddTemplateToOrder(t);
                    return;
                }
            };



            addToFavBtn.Click += (_, __) =>
            {
                var part = GetActiveSelectedPart();
                if (part == null || string.IsNullOrWhiteSpace(part.Material)) return;

                _favoriteMaterials.Add(part.Material);
                _favoritesService.Save(_favoriteMaterials);
                RefreshParts();
            };

            removeFromFavBtn.Click += (_, __) =>
            {
                var part = GetActiveSelectedPart();
                if (part == null || string.IsNullOrWhiteSpace(part.Material)) return;

                _favoriteMaterials.Remove(part.Material);
                _favoritesService.Save(_favoriteMaterials);
                RefreshParts();
            };
            deleteTemplateBtnTop.Click += (_, __) =>
            {
                if (templatesList.SelectedItem is not PartTemplate t) return;

                var result = MessageBox.Show(
                    $"Delete template '{t.Name}'?\n\nThis cannot be undone.",
                    "Delete Template",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;

                _templatesService.Delete(_templates, t.Name);
                _templatesService.Save(GetWarehouseKey(), _templates);

                RefreshTemplatesUI();
                Title = $"Template deleted: {t.Name}";
            };


            void UpdateButtonsForTab()
            {
                addToFavBtn.Visibility = (partsTabs.SelectedIndex == 0)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                removeFromFavBtn.Visibility = (partsTabs.SelectedIndex == 1)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                deleteTemplateBtnTop.Visibility = (partsTabs.SelectedIndex == 2)
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }


            buttonsRow.Children.Add(addToOrderBtn);
            buttonsRow.Children.Add(deleteTemplateBtnTop);
            buttonsRow.Children.Add(addToFavBtn);
            buttonsRow.Children.Add(removeFromFavBtn);

            partsPanel.Children.Add(buttonsRow);

            // IMPORTANT: update this in your existing SelectionChanged
            // (keep your RefreshParts call too)
            partsTabs.SelectionChanged += (_, __) =>
            {
                UpdateButtonsForTab();
                RefreshParts();
            };


            // Add controls to panel
            partsPanel.Children.Add(partsSearch);
            partsPanel.Children.Add(partsTabs);
            

            // Refresh method now fills BOTH lists
            void RefreshParts()
            {
                string q = (partsSearch.Text ?? "").Trim();

                var baseSet = _allParts
                    .Where(p => string.Equals(p.Warehouse, _currentWarehouse, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(q))
                {
                    baseSet = baseSet.Where(p =>
                        (p.Description?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false) ||
                        (p.Material?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false));
                }

                var all = baseSet.Take(250).ToList();

                var fav = baseSet
                    .Where(p => !string.IsNullOrWhiteSpace(p.Material) && _favoriteMaterials.Contains(p.Material))
                    .Take(250)
                    .ToList();

                allPartsList.ItemsSource = all;
                favPartsList.ItemsSource = fav;
            }

            partsSearch.TextChanged += (_, __) => RefreshParts();
            

            RefreshParts();

            // Put card into 2x2 grid (spanning rows 0-1 like you already do)
            var partsCard = MakeCard(partsPanel, new Thickness(0, 0, 8, 0));
            Grid.SetRow(partsCard, 0);
            Grid.SetColumn(partsCard, 0);
            Grid.SetRowSpan(partsCard, 2); // fills the empty top-left space
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

            // Header row: [Pill] [Movement Type]
            var movementHeader = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            movementHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            movementHeader.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Per-tab pill (binds to THIS tab's Tag)
            var issueReturnPill = new ToggleButton
            {
                Style = (Style)FindResource("PillToggleStyle"),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center,
                IsChecked = (ownerTab.Tag as bool?) ?? false
            };

            issueReturnPill.Checked += (_, __) =>
            {
                ownerTab.Tag = true; // Return
                BuildMovementOptions(ownerTab);
                UpdateDetailsBoxForTab(ownerTab);
            };

            issueReturnPill.Unchecked += (_, __) =>
            {
                ownerTab.Tag = false; // Issue
                BuildMovementOptions(ownerTab);
                UpdateDetailsBoxForTab(ownerTab);
            };

            Grid.SetColumn(issueReturnPill, 0);
            movementHeader.Children.Add(issueReturnPill);

            var movementLabel = new TextBlock
            {
                Text = "Movement Type",
                FontSize = 16,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };

            Grid.SetColumn(movementLabel, 1);
            movementHeader.Children.Add(movementLabel);

            // Stack that holds header + radio buttons
            var movementStack = new StackPanel();
            movementStack.Children.Add(movementHeader);
            movementStack.Children.Add(movementPanel);

            // Host border added ONCE
            var movementHost = new Border
            {
                Background = System.Windows.Media.Brushes.Transparent,
                Padding = new Thickness(0, 0, 12, 0),
                Child = movementStack
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

            //Status for adding more than 12 line items.
            var statusText = new TextBlock
            {
                Foreground = Brushes.IndianRed,
                FontSize = 13,
                Margin = new Thickness(0, 8, 0, 0),
                Visibility = Visibility.Collapsed
            };

            _selectedStatusByTab[ownerTab] = statusText;

            Grid.SetRow(statusText, 2);
            selectedArea.Children.Add(statusText);


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

            var saveToTemplatesBtn = new Button
            {
                Content = "Save to Templates",
                Height = 34,
                Padding = new Thickness(14, 0, 14, 0),
                Margin = new Thickness(8, 0, 0, 0)
            };

            btnRow.Children.Add(removeBtn);
            btnRow.Children.Add(clearBtn);

            removeBtn.Click += (_, __) =>
            {
                if (selectedGrid.SelectedItem is not SelectedPartLine line) return;

                var list = GetSelectedParts(ownerTab);
                if (line.Qty > 1) line.Qty -= 1;
                else list.Remove(line);
                ClearTabStatus(ownerTab);
            };

            saveToTemplatesBtn.Click += (_, __) =>
            {
                var lines = GetSelectedParts(ownerTab);
                if (lines.Count == 0)
                {
                    // Small message box is ok since you asked for it
                    MessageBox.Show("No selected parts to save.", "Templates", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                // Suggest a name based on current reason or tab title
                string suggested = GetTabReason(ownerTab).Trim();
                if (string.IsNullOrWhiteSpace(suggested))
                    suggested = GetTabTitle(ownerTab).Trim();

                // if the default tab title is "Blank" / "Blank (2)" etc, don't suggest it
                if (Regex.IsMatch(suggested, @"^Blank(\s*\(\d+\))?$", RegexOptions.IgnoreCase))
                    suggested = "";

                var dlg = new TemplateNameWindow(suggested) { Owner = this };
                if (dlg.ShowDialog() != true) return;

                string name = dlg.TemplateName;

                var template = new PartTemplate
                {
                    Name = name,
                    Lines = lines.Select(x => new TemplateLine
                    {
                        Material = x.Material,
                        Description = x.Description,
                        Qty = x.Qty
                    }).ToList()
                };

                _templatesService.Upsert(_templates, template);
                _templatesService.Save(GetWarehouseKey(), _templates);

                RefreshTemplatesUI(name);
                Title = $"Template saved: {name}";
            };


            clearBtn.Click += (_, __) =>
            {
                GetSelectedParts(ownerTab).Clear();
                ClearTabStatus(ownerTab);
            };

            btnRow.Children.Add(saveToTemplatesBtn);



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
                _settings = _settingsService.Load();
                TryLoadParts();
                RefreshPartsList();

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

        private void AddPartToOrder(TabItem tab, Part part, int qtyToAdd = 1)
        {
            if (part == null) return;

            var list = GetSelectedParts(tab);

            // If already there, just increase qty (doesn't count as a new line item)
            var existing = list.FirstOrDefault(x => x.Material == part.Material);
            if (existing != null)
            {
                existing.Qty += qtyToAdd;
                ClearTabStatus(tab);
                return;
            }

            // New line item would exceed max
            if (list.Count >= MaxLineItemsPerTab)
            {
                SetTabStatus(tab, $"Max {MaxLineItemsPerTab} line items per site/tab.");
                return;
            }

            var newLine = new SelectedPartLine(part) { Qty = qtyToAdd };
            list.Add(newLine);

            ClearTabStatus(tab);
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

        private static void SetPdfTextField(PdfLoadedDocument doc, string fieldName, string? value)
        {
            if (doc.Form == null) return;

            try
            {
                // IMPORTANT: this indexer throws if the name is wrong
                var field = doc.Form.Fields[fieldName];

                if (field is Syncfusion.Pdf.Parsing.PdfLoadedTextBoxField tb)
                    tb.Text = value ?? "";
                else if (field is Syncfusion.Pdf.Parsing.PdfLoadedCheckBoxField cb)
                    cb.Checked = string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
                else
                {
                    // field exists but isn't a textbox/checkbox we handle
                    System.Diagnostics.Debug.WriteLine($"Field '{fieldName}' exists but is type {field.GetType().Name}");
                }
            }
            catch (ArgumentException)
            {
                // Dump all field names to the Output window (Debug)
                var names = new List<string>();
                for (int i = 0; i < doc.Form.Fields.Count; i++)
                    names.Add(doc.Form.Fields[i].Name);

                System.Diagnostics.Debug.WriteLine($"❌ PDF field not found: '{fieldName}'");
                System.Diagnostics.Debug.WriteLine("✅ Available fields:");
                foreach (var n in names.OrderBy(x => x))
                    System.Diagnostics.Debug.WriteLine("  - " + n);

                // Don’t crash the app
                return;
            }
        }

        private string GetDocHeaderText(TabItem tab)
        {
            // DocHeader should ONLY be the user-entered Site#/Reason.
            // If empty -> write nothing.
            return (GetTabReason(tab) ?? "").Trim();
        }        

        private static void SetMovementTypeRadio(PdfLoadedDocument loaded, string movementCode)
    {
        if (loaded?.Form?.Fields == null) return;
        if (string.IsNullOrWhiteSpace(movementCode)) return;

        // Your templates might have either name (you had a space issue earlier)
        string[] possibleNames = { "MovementType", "Movement Type" };

        foreach (PdfField f in loaded.Form.Fields)
        {
            if (!possibleNames.Any(n => string.Equals(f.Name, n, StringComparison.OrdinalIgnoreCase)))
                continue;

            // MovementType is a RADIO button list in your template
            if (f is PdfLoadedRadioButtonListField rb)
            {
                // Most reliable: select by matching OptionValue (e.g., "261", "202", "962")
                foreach (PdfLoadedRadioButtonItem item in rb.Items)
                {
                    if (string.Equals(item.OptionValue, movementCode, StringComparison.OrdinalIgnoreCase))
                    {
                        item.Selected = true;
                        return;
                    }
                }

                // Fallback: try SelectedValue (still valid in Syncfusion)
                rb.SelectedValue = movementCode;
                return;
            }
        }
    }

        private string GetWarehouseKey()
        {
            // ex: "Building D" -> "building_d"
            var s = (_currentWarehouse ?? "").Trim().ToLowerInvariant();
            s = s.Replace(" ", "_");
            return string.IsNullOrWhiteSpace(s) ? "unknown" : s;
        }

        private void Home_Click(object sender, RoutedEventArgs e)
        {
            if (HasWorkInProgress())
            {
                var result = MessageBox.Show(
                    "Are you sure you want to go Home?\n\n" +
                    "You will lose all selected parts and entered details for your current tabs.",
                    "Go Home",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                    return;
            }

            var w = new WarehouseSelectWindow();
            w.Show();
            Close();
        }

        private bool HasWorkInProgress()
        {
            foreach (var item in SiteTabs.Items)
            {
                if (item is not TabItem tab || tab == PlusTab)
                    continue;

                // Selected parts count
                if (GetSelectedParts(tab).Count > 0)
                    return true;

                // Reason/Site # text (stored in ToolTip)
                if (!string.IsNullOrWhiteSpace(GetTabReason(tab)))
                    return true;

                // If you want, you can also include movement/details here later.
            }

            return false;
        }
        
        private string GetNextDefaultBlankTitle()
    {
        // We treat:
        // "Blank"      => number 1
        // "Blank (2)"  => number 2
        // etc.
        var used = new HashSet<int>();

        foreach (var item in SiteTabs.Items)
        {
            if (item is not TabItem t) continue;
            if (t == PlusTab) continue;

            string title = GetTabTitle(t).Trim();

            // Match: Blank or Blank (N)
            var m = Regex.Match(title, @"^Blank(?:\s*\((\d+)\))?$", RegexOptions.IgnoreCase);
            if (!m.Success) continue;

            if (!m.Groups[1].Success)
                used.Add(1);
            else if (int.TryParse(m.Groups[1].Value, out int n))
                used.Add(n);
        }

        // Find the smallest missing positive number
        int next = 1;
        while (used.Contains(next)) next++;

        return next == 1 ? "Blank" : $"Blank ({next})";
    }

        private void UpdateTabCloseButtonsVisibility()
        {
            int siteTabs = SiteTabs.Items.Count - 1; // exclude PlusTab
            bool showClose = siteTabs > 1;

            foreach (var item in SiteTabs.Items)
            {
                if (item is not TabItem tab || tab == PlusTab)
                    continue;

                if (tab.Header is StackPanel sp)
                {
                    // Find the Border we tagged as CloseHit
                    foreach (var child in sp.Children)
                    {
                        if (child is Border b && (b.Tag as string) == "CloseHit")
                        {
                            b.Visibility = showClose ? Visibility.Visible : Visibility.Collapsed;
                            break;
                        }
                    }
                }
            }
        }

        private string GetPdfName()
        {
            string employeeId = (_settings?.EmployeeId ?? Environment.UserName).Trim();
            string name = (_settings?.Name ?? "").Trim();

            if (string.IsNullOrWhiteSpace(name))
                return employeeId;

            return $"{name} ({employeeId})";
        }

        private string GetEmailDisplayName()
        {
            // Use Settings Name only (no EmployeeID)
            string name = (_settings?.Name ?? "").Trim();

            // Fallback if they never filled it out
            if (string.IsNullOrWhiteSpace(name))
                name = Environment.UserName;

            return name;
        }

        private static bool IsBlankTabName(string? title)
    {
        var t = (title ?? "").Trim();
        return t.Length == 0 || t.StartsWith("Blank", StringComparison.OrdinalIgnoreCase);
    }

        private string GetDocHeaderForTab(TabItem tab)
    {
        // Prefer the per-tab reason (ToolTip)
        string reason = (GetTabReason(tab) ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(reason))
            return reason;

        // If reason is empty and tab is any "Blank...", then DocHeader should be empty
        string title = (GetTabTitle(tab) ?? "").Trim();
        if (IsBlankTabName(title))
            return "";

        // Otherwise fall back to the tab title (if user renamed it)
        return title;
    }

        private string BuildPdfFileName(List<TabItem> tabsIncluded, int pageCount)
    {
        // Use DocHeader if present; otherwise fall back to tab title
        var pieces = tabsIncluded
            .Select(t =>
            {
                var s = GetDocHeaderForTab(t).Trim();
                if (string.IsNullOrWhiteSpace(s))
                    s = GetTabTitle(t).Trim();

                // sanitize invalid filename chars
                foreach (char c in Path.GetInvalidFileNameChars())
                    s = s.Replace(c, '_');

                return s;
            })
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .ToList();

        if (pieces.Count == 0)
            pieces.Add("Output");

        string dateToken = DateTime.Now
            .ToString("ddMMMyyyy", CultureInfo.InvariantCulture)
            .ToUpperInvariant(); // 01FEB2026

        string joined = string.Join("_", pieces);

        return $"{joined}_{dateToken}_{pageCount}page.pdf";
    }

        private void SetTabStatus(TabItem tab, string message)
        {
            if (_selectedStatusByTab.TryGetValue(tab, out var tb))
            {
                tb.Text = message;
                tb.Visibility = string.IsNullOrWhiteSpace(message) ? Visibility.Collapsed : Visibility.Visible;
            }
        }

        private void ClearTabStatus(TabItem tab) => SetTabStatus(tab, "");        

        private bool IsBlankTabTitle(string title)
    {
        title = (title ?? "").Trim();
        if (title.Equals("Blank", StringComparison.OrdinalIgnoreCase)) return true;
        if (title.StartsWith("Blank (", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

        // IMPORTANT: DocHeader should ONLY be the user's Site#/Reason (never fall back to tab title)
        private string GetDocHeaderForPdf(TabItem tab)
    {
        string title = GetTabTitle(tab);
        if (IsBlankTabTitle(title))
            return "";

        string reason = (GetTabReason(tab) ?? "").Trim();
        if (string.IsNullOrWhiteSpace(reason))
            return "";

        return reason;
    }

        private List<TabItem> GetCandidateTabs()
    {
        var candidateTabs = new List<TabItem>();

        foreach (var item in SiteTabs.Items)
        {
            if (item is not TabItem tab) continue;
            if (tab == PlusTab) continue;

            var lines = GetSelectedParts(tab);
            string docHeader = GetDocHeaderForPdf(tab);

            // Skip totally empty
            if (lines.Count == 0 && string.IsNullOrWhiteSpace(docHeader))
                continue;

            bool isReturn = GetIsReturnForTab(tab);
            string templatePath = GetTemplatePath(isReturn);
            if (!File.Exists(templatePath))
                continue;

            candidateTabs.Add(tab);
        }

        return candidateTabs;
    }

        private (string OutFile, int Pages) GenerateMergedPdf(List<TabItem> candidateTabs, string outDir)
        {
            using var outDoc = new PdfDocument();
            var printedTabs = new List<TabItem>();

            foreach (var tab in candidateTabs)
            {
                var lines = GetSelectedParts(tab);

                bool isReturn = GetIsReturnForTab(tab);
                string templatePath = GetTemplatePath(isReturn);
                if (!File.Exists(templatePath))
                    continue;

                // MovementType code
                string mtFull = GetMovementType(tab) ?? "";
                string mtCode = ExtractMovementCode(mtFull);
                if (string.IsNullOrWhiteSpace(mtCode))
                    mtCode = isReturn ? "262" : "261";

                // Single ref field
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
                SetPdfTextField(loaded, "Name", GetPdfName());
                SetPdfTextField(loaded, "Date", DateTime.Now.ToString("MM/dd/yyyy"));
                SetPdfTextField(loaded, "DocHeader", GetDocHeaderForPdf(tab)); // <- important

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

                SetMovementTypeRadio(loaded, mtCode);

                // Flatten
                if (loaded.Form != null)
                {
                    loaded.Form.SetDefaultAppearance(false);
                    loaded.Form.FlattenFields();
                }

                // Merge
                outDoc.ImportPageRange(loaded, 0, loaded.Pages.Count - 1);
                printedTabs.Add(tab);
            }

            if (printedTabs.Count == 0 || outDoc.Pages.Count == 0)
                throw new InvalidOperationException("No pages were added to the PDF.");

            string outFile = Path.Combine(outDir, BuildPdfFileName(printedTabs, outDoc.Pages.Count));
            using var fs = File.Create(outFile);
            outDoc.Save(fs);

            return (outFile, outDoc.Pages.Count); ;
        }

        private bool SendPdfEmailOutlook(string pdfPath, List<PrintTabSummary>? rows, int pages, bool openDraft)
        {
            if (string.IsNullOrWhiteSpace(pdfPath) || !File.Exists(pdfPath))
                throw new FileNotFoundException("PDF not found.", pdfPath);

            rows ??= new List<PrintTabSummary>();

            // Recipients
            string to = (_settings?.EmailTo ?? "").Trim();
            string cc = (_settings?.EmailCc ?? "").Trim();

            const string requiredCc = "smartgridradio@centerpointenergy.com";

            if (string.IsNullOrWhiteSpace(cc))
            {
                cc = requiredCc;
            }
            else if (!cc.Contains(requiredCc, StringComparison.OrdinalIgnoreCase))
            {
                cc = cc + "; " + requiredCc; // Outlook supports ; separated
            }

            if (string.IsNullOrWhiteSpace(to) && string.IsNullOrWhiteSpace(cc))
            {
                MessageBox.Show("Email To/CC is blank. Add recipients in Settings → Email.",
                    "Email", MessageBoxButton.OK, MessageBoxImage.Warning);
                return false;
            }

            //Email Subject
            string dateToken = DateTime.Now.ToString("MM/dd/yyyy");
            string typeToken = GetSubjectIssueReturnToken(rows);

            string subject = $"Material {typeToken} - {GetEmailDisplayName()} - {dateToken} - {pages} page(s)";


            // Body
            string body = BuildEmailBody(rows, pages);

            object? outlookApp = null;
            object? mailItem = null;
            object? attachments = null;

            bool didAction = false;

            try
            {
                outlookApp = OutlookCom.GetOrStartOutlook();

                // Create MailItem (0 = olMailItem)
                mailItem = outlookApp.GetType().InvokeMember(
                    "CreateItem",
                    System.Reflection.BindingFlags.InvokeMethod,
                    null,
                    outlookApp,
                    new object[] { 0 });

                if (mailItem == null)
                    throw new InvalidOperationException("Outlook CreateItem returned null.");

                // Set fields
                SetComProperty(mailItem, "To", to);
                SetComProperty(mailItem, "CC", cc);
                SetComProperty(mailItem, "Subject", subject);
                SetComProperty(mailItem, "Body", body);

                // Attach PDF
                attachments = GetComProperty(mailItem, "Attachments");
                if (attachments == null)
                    throw new InvalidOperationException("Outlook Attachments collection was null.");

                attachments.GetType().InvokeMember("Add",
                    System.Reflection.BindingFlags.InvokeMethod, null, attachments,
                    new object[] { pdfPath });

                // Draft vs Send
                if (openDraft)
                {
                    // false = don't modal-block the app; Outlook will open inspector
                    mailItem.GetType().InvokeMember("Display",
                        System.Reflection.BindingFlags.InvokeMethod, null, mailItem, new object[] { false });

                    didAction = true; // draft opened
                }
                else
                {
                    mailItem.GetType().InvokeMember("Send",
                        System.Reflection.BindingFlags.InvokeMethod, null, mailItem, null);

                    didAction = true; // send invoked
                }
            }
            catch (COMException ex)
            {
                MessageBox.Show(
                    "Outlook email failed.\n\n" +
                    $"HRESULT: 0x{ex.HResult:X8}\n" +
                    ex.Message + "\n\n" +
                    "Fixes:\n" +
                    "• Open Outlook (classic) manually once, finish any prompts, then retry.\n" +
                    "• If Outlook is hung, end OUTLOOK.EXE in Task Manager.\n" +
                    "• Ensure your app is not running as Administrator.\n" +
                    "• If you only have New Outlook, COM automation may fail.",
                    "Email", MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }
            catch (Exception ex)
            {
                MessageBox.Show("Outlook email failed:\n\n" + ex.Message,
                    "Email", MessageBoxButton.OK, MessageBoxImage.Error);

                return false;
            }
            finally
            {
                SafeReleaseComObject(attachments);
                SafeReleaseComObject(mailItem);
                SafeReleaseComObject(outlookApp);
            }

            return didAction;
        }

        private static void SetComProperty(object? target, string name, object? value)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.SetProperty, null, target, new object?[] { value });
        }

        private static object? GetComProperty(object? target, string name)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));
            return target.GetType().InvokeMember(name,
                System.Reflection.BindingFlags.GetProperty, null, target, null);
        }

        private static void SafeReleaseComObject(object? obj)
        {
            try
            {
                if (obj != null && Marshal.IsComObject(obj))
                    Marshal.FinalReleaseComObject(obj);
            }
            catch { /* ignore */ }
        }

        private string BuildEmailBody(List<PrintTabSummary> rows, int pages)
        {
            rows ??= new List<PrintTabSummary>();

            var lines = new List<string>
            {
                $"Attached is the Material Requisition PDF {pages} page(s).",
                "",
                "Summary:"
            };

            foreach (var r in rows)
            {
                string site = (r.Tab ?? "").Trim();
                if (string.IsNullOrWhiteSpace(site))
                    site = "(blank)";

                string type = (r.Type ?? "").Trim();
                if (string.IsNullOrWhiteSpace(type))
                    type = "(unknown)";

                string label = (r.RefLabel ?? "").Trim();
                if (string.IsNullOrWhiteSpace(label))
                    label = "WorkOrder";

                string val = (r.RefValue ?? "").Trim();
                if (string.IsNullOrWhiteSpace(val))
                    val = "(missing)";

                // ✅ "# of parts, not line items" = sum of quantities
                int partCount = 0;
                if (r.Parts != null)
                    partCount = r.Parts.Sum(p => Math.Max(0, p.Qty));

                lines.Add($"- {site} | {type} | {label}: {val} | {partCount} parts");
            }

            lines.Add("");
            lines.Add("Thank you!");
            lines.Add("");
            lines.Add(GetEmailDisplayName());

            return string.Join(Environment.NewLine, lines);
        }

        private async void OpenSummary_Click(object sender, RoutedEventArgs e)
        {
            // Save current textbox value into selected tab
            if (SiteTabs.SelectedItem is TabItem current && current != PlusTab && SiteNameBox != null)
                SetTabReason(current, SiteNameBox.Text ?? "");

            // Output folder
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string outDir =
                !string.IsNullOrWhiteSpace(_settings?.PdfOutputFolder)
                    ? _settings.PdfOutputFolder.Trim()
                    : Path.Combine(baseDir, "Output");

            Directory.CreateDirectory(outDir);

            var candidateTabs = GetCandidateTabs();
            if (candidateTabs.Count == 0)
            {
                Title = "Nothing to generate (all tabs blank).";
                return;
            }

            // Build summary rows
            var rows = new List<PrintTabSummary>();

            foreach (var tab in candidateTabs)
            {
                bool isReturn = GetIsReturnForTab(tab);

                // Tab label: prefer DocHeader (site/reason). fallback to tab title.
                string tabLabel = (GetDocHeaderText(tab) ?? "").Trim();
                if (string.IsNullOrWhiteSpace(tabLabel))
                    tabLabel = GetTabTitle(tab);

                // movement code and the single reference value
                string mtFull = GetMovementType(tab) ?? "";
                string mtCode = ExtractMovementCode(mtFull);
                if (string.IsNullOrWhiteSpace(mtCode))
                    mtCode = isReturn ? "262" : "261";

                string refLabel;
                string refValue;

                if (mtCode == "201" || mtCode == "202")
                {
                    refLabel = "CostCenter";
                    refValue = (GetCostCenter(tab) ?? "").Trim();
                }
                else if (mtCode == "221" || mtCode == "222")
                {
                    refLabel = "WBS";
                    refValue = (GetWbs(tab) ?? "").Trim();
                }
                else
                {
                    refLabel = "WorkOrder";
                    refValue = (GetWorkOrder(tab) ?? "").Trim(); // includes 962 case (still WO value)
                }


                var parts = GetSelectedParts(tab)
                    .Select(line => new PrintPartLine
                    {
                        Qty = line.Qty,
                        Description = line.Description,
                        Material = line.Material
                    })
                    .ToList();

                string reason = (GetDocHeaderText(tab) ?? "").Trim();
                bool missingReason = string.IsNullOrWhiteSpace(reason);

                // only "missing ref" if they actually have parts selected
                bool missingRef = parts.Count > 0 && string.IsNullOrWhiteSpace(refValue);

                rows.Add(new PrintTabSummary
                {
                    Tab = tabLabel,
                    Type = isReturn ? "Return" : "Issue",
                    RefLabel = refLabel, // CC/WBS/WO
                    RefValue = refValue, //Movement Type (261, 262, etc)
                    Parts = parts,
                    MissingReason = missingReason,
                    MissingRef = missingRef
                });
            }

            if (rows.Any(r => r.MissingRef))
            {
                MessageBox.Show(
                    "One or more tabs are missing the required Cost Center / WBS / Work Order.\n\n" +
                    "You can still review the Summary window to see which tab is missing it.",
                    "Missing Required Reference",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }


            // SHOW summary first (this is what you were missing)
            var summary = new SummaryWindow(
                rows,
                "Yellow = missing Site#/Reason   •   Red = missing required reference #"
            )
            { Owner = this };

            bool? ok = summary.ShowDialog();
            if (ok != true)
                return; // user canceled

            // Now generate the PDF (after the user confirmed)
            string outFile;
            int pages;
            try
            {
                (outFile, pages) = GenerateMergedPdf(candidateTabs, outDir);
            }
            catch (Exception ex)
            {
                MessageBox.Show("PDF generation failed:\n\n" + ex.Message, "Generate PDF",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Do the chosen action
            switch (summary.Action)
            {
                case SummaryWindow.SummaryAction.EmailDraft:
                    await SendPdfEmailOutlookStaAsync(outFile, rows, pages, openDraft: true);
                    break;

                case SummaryWindow.SummaryAction.Email:
                    {
                        bool sent = await SendPdfEmailOutlookStaAsync(outFile, rows, pages, openDraft: false);
                        if (sent)
                        {
                            MessageBox.Show(
                                "Email sent successfully.",
                                "Email",
                                MessageBoxButton.OK,
                                MessageBoxImage.Information);
                        }
                        break;
                    }

                case SummaryWindow.SummaryAction.GenerateOnly:
                default:
                    try
                    {
                        Process.Start(new ProcessStartInfo
                        {
                            FileName = outFile,
                            UseShellExecute = true // required to open with default PDF viewer
                        });
                    }
                    catch { }

                    break;
            }
            Title = "Created: " + outFile;
        }

        private Task<bool> SendPdfEmailOutlookStaAsync(string pdfPath, List<PrintTabSummary>? rows, int pages, bool openDraft)
        {
            var tcs = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            var thread = new Thread(() =>
            {
                try
                {
                    bool ok = SendPdfEmailOutlook(pdfPath, rows, pages, openDraft);
                    tcs.SetResult(ok);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();

            return tcs.Task;
        }

        private static string GetSubjectIssueReturnToken(List<PrintTabSummary>? rows)
        {
            rows ??= new List<PrintTabSummary>();

            var types = rows
                .Select(r => (r.Type ?? "").Trim())
                .Where(t => t.Length > 0)
                .Select(t => t.Equals("Return", StringComparison.OrdinalIgnoreCase) ? "Return"
                            : t.Equals("Issue", StringComparison.OrdinalIgnoreCase) ? "Issue"
                            : t)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (types.Count == 1) return types[0];
            if (types.Count > 1) return "Issue AND Return";   // mixed tabs
            return "Material"; // fallback
        }

        private static bool IsAppElevated()
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }

    }
}