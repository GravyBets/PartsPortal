using System.Windows;


namespace PartsPortal
{
    public partial class TemplateNameWindow : Window
    {
        public string TemplateName => (NameBox.Text ?? "").Trim();

        public TemplateNameWindow(string suggestedName = "")
        {
            InitializeComponent();
            NameBox.Text = suggestedName ?? "";
            Loaded += (_, __) =>
            {
                NameBox.Focus();
                NameBox.SelectAll();
            };
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TemplateName))
                return;

            DialogResult = true;
            
        }
    }
}
