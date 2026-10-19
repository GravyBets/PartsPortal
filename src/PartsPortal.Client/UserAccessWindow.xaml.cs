using PartsPortal.Contracts.Access;
using PartsPortal.Services;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;

namespace PartsPortal;

public partial class UserAccessWindow : Window
{
    private readonly AccessApiService _service = new();
    private readonly ObservableCollection<UserRow> _rows = new();
    private ICollectionView? _view;
    private bool _isNewUser;
    private bool _isBusy;

    public UserAccessWindow()
    {
        InitializeComponent();
        Loaded += UserAccessWindow_Loaded;
    }

    private async void UserAccessWindow_Loaded(
        object sender,
        RoutedEventArgs e)
    {
        await LoadUsersAsync();
    }

    private async Task LoadUsersAsync(string? selectEmployeeId = null)
    {
        try
        {
            SetBusy(true, "Loading users...");

            var users = await _service.GetUsersAsync();

            _rows.Clear();

            foreach (var user in users)
                _rows.Add(new UserRow(user));

            _view = CollectionViewSource.GetDefaultView(_rows);
            _view.Filter = FilterUser;
            UsersGrid.ItemsSource = _view;

            StatusText.Text =
                $"{_rows.Count} user{(_rows.Count == 1 ? "" : "s")} configured.";

            if (!string.IsNullOrWhiteSpace(selectEmployeeId))
            {
                var row = _rows.FirstOrDefault(x =>
                    string.Equals(
                        x.EmployeeId,
                        selectEmployeeId,
                        StringComparison.OrdinalIgnoreCase));

                if (row is not null)
                {
                    UsersGrid.SelectedItem = row;
                    UsersGrid.ScrollIntoView(row);
                }
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not load PartsPortal users.\n\n" + ex.Message,
                "User Access",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            StatusText.Text = "User access load failed.";
        }
        finally
        {
            SetBusy(false);
        }
    }

    private bool FilterUser(object item)
    {
        if (item is not UserRow row)
            return false;

        var query = (SearchBox.Text ?? "").Trim();

        if (query.Length == 0)
            return true;

        return Contains(row.EmployeeId, query) ||
               Contains(row.DisplayName, query) ||
               Contains(row.RolesDisplay, query) ||
               Contains(row.StatusDisplay, query);
    }

    private static bool Contains(string? value, string query)
        => !string.IsNullOrWhiteSpace(value) &&
           value.IndexOf(
               query,
               StringComparison.OrdinalIgnoreCase) >= 0;

    private void SearchBox_TextChanged(
        object sender,
        TextChangedEventArgs e)
    {
        _view?.Refresh();
    }

    private void UsersGrid_SelectionChanged(
        object sender,
        SelectionChangedEventArgs e)
    {
        if (UsersGrid.SelectedItem is not UserRow row)
        {
            if (!_isNewUser)
            {
                EditorTitle.Text = "Select a user";
                EmployeeIdBox.Text = "";
                EmployeeIdBox.IsReadOnly = false;
                DisplayNameBox.Text = "";
                ActiveCheckBox.IsChecked = false;
                AdminCheckBox.IsChecked = false;
                PartsManagerCheckBox.IsChecked = false;
            }

            UpdateEditorState();
            return;
        }

        _isNewUser = false;

        EditorTitle.Text = "Edit User";
        EmployeeIdBox.Text = row.EmployeeId;
        EmployeeIdBox.IsReadOnly = true;
        DisplayNameBox.Text = row.DisplayName ?? "";
        ActiveCheckBox.IsChecked = row.IsActive;

        AdminCheckBox.IsChecked =
            row.Roles.Contains(
                "ADMIN",
                StringComparer.OrdinalIgnoreCase);

        PartsManagerCheckBox.IsChecked =
            row.Roles.Contains(
                "PARTS_MANAGER",
                StringComparer.OrdinalIgnoreCase);

        UpdateEditorState();
    }

    private void NewUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        UsersGrid.SelectedItem = null;
        _isNewUser = true;

        EditorTitle.Text = "New User";
        EmployeeIdBox.IsReadOnly = false;
        EmployeeIdBox.Text = "";
        DisplayNameBox.Text = "";
        ActiveCheckBox.IsChecked = true;
        AdminCheckBox.IsChecked = false;
        PartsManagerCheckBox.IsChecked = false;
        UpdateEditorState();

        EmployeeIdBox.Focus();
    }

    private async void Save_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        var employeeId = (EmployeeIdBox.Text ?? "").Trim();

        if (string.IsNullOrWhiteSpace(employeeId))
        {
            MessageBox.Show(
                "Employee ID is required.",
                "User Access",
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            EmployeeIdBox.Focus();
            return;
        }

        var roles = new List<string>();

        if (AdminCheckBox.IsChecked == true)
            roles.Add("ADMIN");

        if (PartsManagerCheckBox.IsChecked == true)
            roles.Add("PARTS_MANAGER");

        var request = new SaveUserAccessRequest(
            employeeId,
            (DisplayNameBox.Text ?? "").Trim(),
            ActiveCheckBox.IsChecked == true,
            roles);

        try
        {
            SetBusy(true, "Saving access...");

            await _service.SaveUserAsync(request);

            _isNewUser = false;
            await LoadUsersAsync(employeeId);

            StatusText.Text =
                $"Access saved for {employeeId}.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not save user access.\n\n" + ex.Message,
                "User Access",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void DeleteUser_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy || _isNewUser ||
            UsersGrid.SelectedItem is not UserRow row)
            return;

        var display = string.IsNullOrWhiteSpace(row.DisplayName)
            ? row.EmployeeId
            : $"{row.DisplayName} ({row.EmployeeId})";

        var answer = MessageBox.Show(
            $"Permanently delete the PartsPortal access record for {display}?\n\n" +
            "Their PartsPortal roles will be removed. This action cannot " +
            "be undone. Their saved personal templates will be retained.\n\n" +
            "The final active administrator cannot be deleted.",
            "Delete User",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (answer != MessageBoxResult.Yes)
            return;

        try
        {
            SetBusy(true, "Deleting user...");
            await _service.DeleteUserAsync(row.EmployeeId);

            // Remove the old selection and reset the editor before reloading.
            _isNewUser = false;
            UsersGrid.SelectedItem = null;

            await LoadUsersAsync();
            StatusText.Text = $"User {row.EmployeeId} deleted.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                "Could not delete the user.\n\n" + ex.Message,
                "User Access",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async void Refresh_Click(
        object sender,
        RoutedEventArgs e)
    {
        if (_isBusy)
            return;

        await LoadUsersAsync(
            (UsersGrid.SelectedItem as UserRow)?.EmployeeId);
    }

    private void SetBusy(bool busy, string? message = null)
    {
        _isBusy = busy;
        Mouse.OverrideCursor = busy ? Cursors.Wait : null;

        UsersGrid.IsEnabled = !busy;
        SearchBox.IsEnabled = !busy;
        UpdateEditorState();

        if (!string.IsNullOrWhiteSpace(message))
            StatusText.Text = message;
    }

    private void UpdateEditorState()
    {
        bool canEdit = !_isBusy &&
                       (_isNewUser || UsersGrid.SelectedItem is UserRow);

        EditorFields.IsEnabled = canEdit;
        SaveButton.IsEnabled = canEdit;
        DeleteUserButton.IsEnabled = !_isBusy &&
                                     !_isNewUser &&
                                     UsersGrid.SelectedItem is UserRow;
    }

    private void Close_Click(
        object sender,
        RoutedEventArgs e)
    {
        Close();
    }

    private sealed class UserRow
    {
        public UserRow(UserAccessDto user)
        {
            User = user;
        }

        public UserAccessDto User { get; }

        public string EmployeeId => User.EmployeeId;
        public string? DisplayName => User.DisplayName;
        public bool IsActive => User.IsActive;
        public IReadOnlyList<string> Roles => User.Roles;

        public string RolesDisplay =>
            Roles.Count == 0
                ? "User"
                : string.Join(", ", Roles);

        public string StatusDisplay =>
            IsActive ? "Active" : "Inactive";
    }
}
