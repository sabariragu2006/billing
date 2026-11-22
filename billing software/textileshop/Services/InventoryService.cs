#nullable enable
using System;
using System.Collections.Generic;
using textileshop.Models;

namespace textileshop.Services
{
    public static class InventoryService
    {
        // Fired when data changes so ProductsListPage can refresh automatically
        public static event EventHandler? DataChanged;

        public static List<ExcelProductItem> LoadProducts()
            => DatabaseHelper.LoadProducts();

        public static bool SaveProducts(List<ExcelProductItem> products)
        {
            bool ok = DatabaseHelper.SaveProducts(products);
            if (ok) DataChanged?.Invoke(null, EventArgs.Empty);
            return ok;
        }

        public static bool AddProducts(List<ExcelProductItem> products)
        {
            foreach (var p in products)
            {
                if (string.IsNullOrWhiteSpace(p.Id))
                    p.Id = $"PRD-{DateTime.Now:yyyyMMddHHmmssfff}";
            }

            bool result = DatabaseHelper.AddProducts(products);

            if (result)
                DataChanged?.Invoke(null, EventArgs.Empty); // ← this triggers ProductsListPage to reload

            return result;
        }

        public static bool UpdateProduct(ExcelProductItem updated)
        {
            bool ok = DatabaseHelper.UpdateProduct(updated);
            if (ok) DataChanged?.Invoke(null, EventArgs.Empty);
            return ok;
        }

        public static bool DeleteProduct(string productId)
        {
            bool ok = DatabaseHelper.DeleteProduct(productId);
            if (ok) DataChanged?.Invoke(null, EventArgs.Empty);
            return ok;
        }
        // Add this inside the InventoryService class, alongside the other methods
        public static void NotifyDataChanged()
        {
            DataChanged?.Invoke(null, EventArgs.Empty);
        }
        // ── ID generation ────────────────────────────────────────────────────
        private static void AssignIds(List<ExcelProductItem> items)
        {
            // Find the highest existing PRD-##### number in the DB
            var existing = DatabaseHelper.LoadProducts();
            int maxNum = 0;
            foreach (var p in existing)
            {
                if (!string.IsNullOrEmpty(p.Id) && p.Id.StartsWith("PRD-") &&
                    int.TryParse(p.Id.AsSpan(4), out int n) && n > maxNum)
                    maxNum = n;
            }

            int next = maxNum + 1;
            foreach (var item in items)
            {
                if (string.IsNullOrWhiteSpace(item.Id))
                {
                    item.Id = $"PRD-{next:D5}";
                    next++;
                }
            }
        }
    }
} 