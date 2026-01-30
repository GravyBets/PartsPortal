using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MaterialReqAppV3.Models
{
    public class UserSettings
    {
        public string Name { get; set; } = "";
        public string EmployeeId { get; set; } = "";
        public string TruckNumber { get; set; } = "";
        public string PdfOutputFolder { get; set; } = "";
        public string CsvPath { get; set; } = "";
    }
}
