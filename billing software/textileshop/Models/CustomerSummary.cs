#nullable enable
using System;

namespace textileshop.Models
{
    /// <summary>
    /// Aggregated customer data for analytics (loaded from bills)
    /// </summary>
    public class CustomerSummary
    {
        public string Name { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public DateTime FirstPurchaseDate { get; set; }
        public DateTime LastPurchaseDate { get; set; }
        public int PurchaseCount { get; set; }
        public double TotalSpent { get; set; }
    }
}