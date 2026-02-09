namespace MaterialReqAppV3.Models
{
    public class BackorderLine
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public DateTime DateAdded { get; set; } = DateTime.Now;

        public int Qty { get; set; } = 1;
        public string WorkOrder { get; set; } = "";
        public string SiteReason { get; set; } = "";

        public string Description { get; set; } = "";
        public string Material { get; set; } = "";

        // workflow
        public bool IsOrdered { get; set; } = false;
        public DateTime? DateOrdered { get; set; }
    }
}
