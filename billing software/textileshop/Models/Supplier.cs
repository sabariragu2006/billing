#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using textileshop.Services;

namespace textileshop.Models
{
    /// <summary>
    /// Represents a supplier that can be manually added or imported
    /// </summary>
    public class Supplier
    {
        // ✅ Auto-generated ID: Format SUP-0001, SUP-0002, etc.
        public string Id { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;  // GST Number
        public string? Address { get; set; }
        public string? Email { get; set; }
        public string? Phone { get; set; }
        public string? Notes { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public DateTime? UpdatedAt { get; set; }

        // For display in dropdowns
        public string DisplayName => $"{Name} ({Contact})";

        // ✅ Simplified: No parameter needed - uses DatabaseHelper
        public static string GenerateNextId()
        {
            try
            {
                // Load existing suppliers from SQLite
                var existing = DatabaseHelper.LoadSuppliers();

                // Extract numeric part from existing IDs (SUP-0001 → 1)
                int maxNum = 0;
                foreach (var s in existing)
                {
                    if (!string.IsNullOrEmpty(s.Id) && s.Id.StartsWith("SUP-"))
                    {
                        if (int.TryParse(s.Id.Substring(4), out int num) && num > maxNum)
                            maxNum = num;
                    }
                }

                // Return next ID with zero-padding
                return $"SUP-{(maxNum + 1):D4}";
            }
            catch
            {
                // Fallback if anything fails
                return $"SUP-{DateTime.Now:yyyyMMddHHmmss}";
            }
        }
    }
}