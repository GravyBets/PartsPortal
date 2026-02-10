using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;


namespace MaterialReqAppV3.Models
{
    public class UserSettings : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            OnPropertyChanged(name);
            return true;
        }

        private string _name = "";
        public string Name
        {
            get => _name;
            set => SetField(ref _name, value ?? "");
        }

        private string _employeeId = "";
        public string EmployeeId
        {
            get => _employeeId;
            set => SetField(ref _employeeId, value ?? "");
        }

        private string _truckNumber = "";
        public string TruckNumber
        {
            get => _truckNumber;
            set => SetField(ref _truckNumber, value ?? "");
        }

        private string _pdfOutputFolder = "";
        public string PdfOutputFolder
        {
            get => _pdfOutputFolder;
            set => SetField(ref _pdfOutputFolder, value ?? "");
        }

        private string _csvPath = "";
        public string CsvPath
        {
            get => _csvPath;
            set => SetField(ref _csvPath, value ?? "");
        }

        private string _emailTo = "";
        public string EmailTo
        {
            get => _emailTo;
            set => SetField(ref _emailTo, value ?? "");
        }

        private string _emailCc = "";
        public string EmailCc
        {
            get => _emailCc;
            set => SetField(ref _emailCc, value ?? "");
        }

        private string _emailSubjectTemplate = "Material Requisition - {Warehouse} - {Date}";
        public string EmailSubjectTemplate
        {
            get => _emailSubjectTemplate;
            set => SetField(ref _emailSubjectTemplate, value ?? "");
        }

        private bool _emailOpenDraftInsteadOfSend = true;
        public bool EmailOpenDraftInsteadOfSend
        {
            get => _emailOpenDraftInsteadOfSend;
            set => SetField(ref _emailOpenDraftInsteadOfSend, value);
        }

        // ✅ Email Directory (saved per-user)
        private ObservableCollection<EmailDirectoryEntry> _emailDirectory
            = new ObservableCollection<EmailDirectoryEntry>();

        public ObservableCollection<EmailDirectoryEntry> EmailDirectory
        {
            get => _emailDirectory;
            set => SetField(ref _emailDirectory, value ?? new ObservableCollection<EmailDirectoryEntry>());
        }

        private bool _emailDirectorySeeded = false;
        public bool EmailDirectorySeeded
        {
            get => _emailDirectorySeeded;
            set => SetField(ref _emailDirectorySeeded, value);
        }



        public bool IsDarkMode { get; set; } = false;
        public string BugReportToEmail { get; set; } = ""; // optional, can leave blank for now        

    }
}
