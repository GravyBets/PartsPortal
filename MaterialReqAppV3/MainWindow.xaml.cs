using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;


namespace MaterialReqAppV3
{
    public partial class MainWindow : Window
    {
        public string SelectedWarehouse { get; }
        private TextBox? SiteNameBox;

        public MainWindow(string selectedWarehouse)
        {
            InitializeComponent();

            Loaded += (_, __) =>
            {
                SiteNameBox = (TextBox?)SiteTabs.Template.FindName("PART_SiteNameBox", SiteTabs);

                if (SiteTabs.Items.Count > 0 && SiteTabs.Items[0] is TabItem blankTab)
                {
                    // Make first tab header use hover-close header
                    blankTab.Header = CreateTabHeader("Blank", blankTab);

                    // Default: no reason yet
                    SetTabReason(blankTab, "");
                }

                // Show the selected tab's saved reason (or empty => watermark shows)
                if (SiteNameBox != null && SiteTabs.SelectedItem is TabItem selected && selected != PlusTab)
                    SiteNameBox.Text = GetTabReason(selected);

                // Press Enter to apply
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
            };


            SelectedWarehouse = selectedWarehouse;
            Title = $"Material Requisition - {SelectedWarehouse}";

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

            if (SiteNameBox != null && SiteTabs.SelectedItem is TabItem tab && tab != PlusTab)
            {
                SiteNameBox.Text = GetTabReason(tab); // empty => watermark shows
                SiteNameBox.SelectAll();
            }
        }


        private TabItem CreateSiteTab(string header)
        {
            var tab = new TabItem();
            tab.Tag = false; // default: Material Issue
            tab.Header = CreateTabHeader(header, tab);
            tab.Content = CreateTabContentLayout();
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
                    MessageBox.Show("You must have at least one site tab.");
                    return;
                }

                // Index of the tab being closed (before removal)
                int closingIndex = SiteTabs.Items.IndexOf(ownerTab);

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

                // Keep textbox in sync
                if (SiteNameBox != null && SiteTabs.SelectedItem is TabItem selected && selected != PlusTab)
                {
                    SiteNameBox.Text = GetTabTitle(selected);
                    SiteNameBox.SelectAll();
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

        private Grid CreateTabContentLayout()
        {
            // Pull theme brushes from XAML resources (with safe fallbacks)
            var cardBg = TryFindResource("SurfaceBg") as Brush ?? Brushes.White;
            var cardBorder = TryFindResource("SurfaceBorder") as Brush ?? Brushes.LightGray;
            var textPrimary = TryFindResource("TextPrimary") as Brush ?? Brushes.Black;

            var content = new Grid { Margin = new Thickness(10) };

            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(220) });
            content.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            Border MakeCard(string title, int row, int col, Thickness margin)
            {
                var border = new Border
                {
                    Background = cardBg,
                    BorderBrush = cardBorder,
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(12),
                    Margin = margin,
                    Child = new TextBlock
                    {
                        Text = title,
                        Foreground = textPrimary,
                        FontSize = 18,
                        FontWeight = FontWeights.SemiBold
                    }
                };

                Grid.SetRow(border, row);
                Grid.SetColumn(border, col);
                return border;
            }

            content.Children.Add(MakeCard("Movement Type (Top-Left)", 0, 0, new Thickness(0, 0, 8, 8)));
            content.Children.Add(MakeCard("Cost Center / WBS / Work Order (Top-Right)", 0, 1, new Thickness(8, 0, 0, 8)));
            content.Children.Add(MakeCard("Parts Browser (Bottom-Left)", 1, 0, new Thickness(0, 8, 8, 0)));
            content.Children.Add(MakeCard("Selected Parts (Bottom-Right)", 1, 1, new Thickness(8, 8, 0, 0)));

            return content;
        }

        private string GetTabReason(TabItem tab) => tab.ToolTip?.ToString() ?? "";
        private void SetTabReason(TabItem tab, string reason) => tab.ToolTip = reason;







    }
}