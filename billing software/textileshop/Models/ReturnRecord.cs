// ── Models/ReturnRecord.cs ─────────────────────────────────────────────────
#nullable enable
using System;

namespace textileshop.Models
{
    public class ReturnRecord
    {
        public string Id { get; set; } = string.Empty;
        public string ProductId { get; set; } = string.Empty;
        public string ProductName { get; set; } = string.Empty;
        public int ReturnQty { get; set; }
        public double SaleRate { get; set; }          // rate at which it was sold
        public double RefundAmount { get; set; }      // ReturnQty × SaleRate
        public string Reason { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public string ReturnedBy { get; set; } = string.Empty;
        public DateTime ReturnDate { get; set; } = DateTime.Now;
        public string Notes { get; set; } = string.Empty;
    }
}