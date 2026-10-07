using System.Collections.Generic;

namespace PartsPortal.Models
{
    public class PrintTabSummary
    {
        public string Tab { get; set; } = "";
        public string Type { get; set; } = "";      // "Issue" / "Return"
        public string RefValue { get; set; } = "";  // just the number
        public List<PrintPartLine> Parts { get; set; } = new();
        public bool MissingReason { get; set; }
        public bool MissingRef { get; set; }
        public string RefLabel { get; set; } = "";   // "CostCenter", "WBS", or "WorkOrder"

    }

    public class PrintPartLine
    {
        public int Qty { get; set; }
        public string Description { get; set; } = "";
        public string Material { get; set; } = "";
    }
}
