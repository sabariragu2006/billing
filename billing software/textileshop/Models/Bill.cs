#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace textileshop.Models
{
    /// <summary>
    /// Represents a complete customer bill
    /// </summary>
    public class Bill
    {
        // ✅ Changed to string for auto-generated format
        public string BillNumber { get; set; } = string.Empty;

        public DateTime BillDate { get; set; } = DateTime.Now;
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerMobile { get; set; } = string.Empty;

        public List<BillItem> Items { get; set; } = new();

        // Computed totals
        public double SubTotal => Items.Sum(i => i.SubTotal);
        public double TotalCGST => Items.Sum(i => i.CGSTAmt);
        public double TotalSGST => Items.Sum(i => i.SGSTAmt);
        public double GrandTotal => SubTotal + TotalCGST + TotalSGST;

        // ✅ BillNumber is already a string, so just return it
        public string BillNumberFormatted => BillNumber;
    }
} 