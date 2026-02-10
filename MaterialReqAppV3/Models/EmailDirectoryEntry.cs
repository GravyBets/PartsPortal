using System;

namespace MaterialReqAppV3.Models
{
    public class EmailDirectoryEntry
    {
        public string FirstName { get; set; } = "";
        public string LastName { get; set; } = "";
        public string Email { get; set; } = "";

        // Nice label for the ListBox
        public string DisplayName
        {
            get
            {
                string first = (FirstName ?? "").Trim();
                string last = (LastName ?? "").Trim();

                if (!string.IsNullOrWhiteSpace(first) && !string.IsNullOrWhiteSpace(last))
                    return $"{first} {last}";

                if (!string.IsNullOrWhiteSpace(last))
                    return last;

                if (!string.IsNullOrWhiteSpace(first))
                    return first;

                // fallback
                return (Email ?? "").Trim();
            }
        }
    }
}
