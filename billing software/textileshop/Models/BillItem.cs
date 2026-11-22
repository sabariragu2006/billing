#nullable enable
using System;

namespace textileshop.Models
{
    public class BillItem
    {
        // ✅ Settable properties (can be assigned)
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public double Rate { get; set; }
        public int Quantity { get; set; }
        public double CGSTPct { get; set; }
        public double SGSTPct { get; set; }
        public string CD { get; set; } = string.Empty;

        // ✅ Computed read-only properties (CANNOT be assigned - calculated from above)
        public double SubTotal => Rate * Quantity;
        public double CGSTAmt => SubTotal * (CGSTPct / 100);
        public double SGSTAmt => SubTotal * (SGSTPct / 100);
        public double Total => SubTotal + CGSTAmt + SGSTAmt;
    }
} 