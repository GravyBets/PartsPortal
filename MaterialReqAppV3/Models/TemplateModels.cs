using System.Collections.Generic;

namespace MaterialReqAppV3.Models
{
    public class PartTemplate
    {
        public string Name { get; set; } = "";
        public List<TemplateLine> Lines { get; set; } = new();
    }

    public class TemplateLine
    {
        public string Material { get; set; } = "";
        public string Description { get; set; } = "";
        public int Qty { get; set; } = 1;
    }
}
