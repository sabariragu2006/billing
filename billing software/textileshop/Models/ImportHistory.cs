#nullable enable
using System;
using System.Collections.Generic;

namespace textileshop.Models
{
    public class ImportHistory
    {
        public DateTime ImportDate { get; set; }
        public string FileName { get; set; } = string.Empty;
        public int ItemCount { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierContact { get; set; }
        public List<ImportedProductSnapshot>? Products { get; set; }
    }

    public class ImportedProductSnapshot
    {
        public string ProductName { get; set; } = string.Empty;
        public int Qty { get; set; }
        public double Rate { get; set; }          // selling/net rate
        public double PurchasePrice { get; set; }          // NEW: cost per unit
        public double ProfitAmount { get; set; }          // NEW: Rate - PurchasePrice
        public double ProfitPct { get; set; }          // NEW: (Profit/PurchasePrice)*100
        public double CostTotal { get; set; }
        public double CGSTPct { get; set; }
        public double CGSTAmt { get; set; }
        public double SGSTPct { get; set; }
        public double SGSTAmt { get; set; }
        public double NetRate { get; set; }
        public string? SupplierName { get; set; }
        public string? SupplierContact { get; set; }
        public string? ImportedAt { get; set; }
    }
}