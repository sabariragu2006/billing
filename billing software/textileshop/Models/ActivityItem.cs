#nullable enable
using System;

namespace textileshop.Models
{
    /// <summary>
    /// Represents a recent activity item for the dashboard feed
    /// </summary>
    public class ActivityItem
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;  // e.g., "🧾 Bill", "📥 Import"
        public string Description { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;  // e.g., "₹1,250.00", "15 items"
        public object? Data { get; set; }  // Optional: reference to original Bill/ImportHistory object
    }
}