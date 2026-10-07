using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace PartsPortal.Models
{
    public class Part
    {
        public string Warehouse { get; set; } = "";
        public string Material { get; set; } = "";
        public string Description { get; set; } = "";

        public string DisplayText => $"{Description} ({Material})";

       
    }
}

