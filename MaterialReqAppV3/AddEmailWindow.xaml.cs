using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;

namespace MaterialReqAppV3
{
    public partial class AddEmailWindow : Window, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private string _firstName = "";
        public string FirstName
        {
            get => _firstName;
            set
            {
                if (_firstName == value) return;
                _firstName = value ?? "";
                OnPropertyChanged();
            }
        }

        private string _lastName = "";
        public string LastName
        {
            get => _lastName;
            set
            {
                if (_lastName == value) return;
                _lastName = value ?? "";
                OnPropertyChanged();
            }
        }

        private string _email = "";
        public string Email
        {
            get => _email;
            set
            {
                if (_email == value) return;
                _email = value ?? "";
                OnPropertyChanged();
            }
        }

        public AddEmailWindow()
        {
            InitializeComponent();
            DataContext = this;

            Loaded += (_, __) =>
            {
                // start in First Name for quick entry
                FirstNameTextBox.Focus();
                Keyboard.Focus(FirstNameTextBox);
                FirstNameTextBox.CaretIndex = FirstNameTextBox.Text.Length;
            };
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            string first = (FirstName ?? "").Trim();
            string last = (LastName ?? "").Trim();
            string email = (Email ?? "").Trim();

            if (string.IsNullOrWhiteSpace(email))
            {
                MessageBox.Show("Enter an email address.", "Add Directory Entry",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                EmailTextBox.Focus();
                return;
            }

            // simple validation
            if (!email.Contains("@") || email.Contains(" "))
            {
                MessageBox.Show("That doesn’t look like a valid email.", "Add Directory Entry",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                EmailTextBox.Focus();
                return;
            }

            // store normalized values back into properties (so caller reads clean values)
            FirstName = first;
            LastName = last;
            Email = email;

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
