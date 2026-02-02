using System.Collections.Generic;

namespace MaterialReqAppV3.Models
{
    public class PrintTabSummary
    {
        public string Tab { get; set; } = "";
        public string Type { get; set; } = "";      // "Issue" / "Return"
        public string RefValue { get; set; } = "";  // just the number
        public List<PrintPartLine> Parts { get; set; } = new();
        public bool MissingReason { get; set; }
        public bool MissingRef { get; set; }
    }

    public class PrintPartLine
    {
        public int Qty { get; set; }
        public string Description { get; set; } = "";
        public string Material { get; set; } = "";
    }
}
