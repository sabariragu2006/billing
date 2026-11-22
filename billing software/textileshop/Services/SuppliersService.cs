#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using textileshop.Models;

namespace textileshop.Services
{
    /// <summary>
    /// Service for managing manually added suppliers using SQLite
    /// </summary>
    public static class SuppliersService
    {
        // ✅ Event for UI auto-refresh
        public static event EventHandler? SuppliersChanged;

        /// <summary>
        /// Load all manually added suppliers from SQLite
        /// </summary>
        public static List<Supplier> LoadSuppliers()
        {
            return DatabaseHelper.LoadSuppliers();
        }

        /// <summary>
        /// Save all suppliers to SQLite (overwrite)
        /// </summary>
        public static bool SaveSuppliers(List<Supplier> suppliers)
        {
            bool saveSuccess = DatabaseHelper.SaveSuppliers(suppliers);
            if (saveSuccess) SuppliersChanged?.Invoke(null, EventArgs.Empty);
            return saveSuccess;
        }

        /// <summary>
        /// Add or update a single supplier in SQLite
        /// </summary>
        public static bool AddSupplier(Supplier supplier)
        {
            var all = LoadSuppliers();

            // Auto-generate ID if new
            if (string.IsNullOrEmpty(supplier.Id))
            {
                // ✅ Call without parameter (if you updated GenerateNextId)
                supplier.Id = Supplier.GenerateNextId();
                supplier.CreatedAt = DateTime.Now;
            }
            else
            {
                // Update existing
                var existing = all.FirstOrDefault(s => s.Id == supplier.Id);
                if (existing != null)
                {
                    existing.Name = supplier.Name;
                    existing.Contact = supplier.Contact;
                    existing.Address = supplier.Address;
                    existing.Email = supplier.Email;
                    existing.Phone = supplier.Phone;
                    existing.Notes = supplier.Notes;
                    existing.UpdatedAt = DateTime.Now;

                    // ✅ Renamed variable to avoid conflict
                    bool updateSuccess = SaveSuppliers(all);
                    if (updateSuccess) SuppliersChanged?.Invoke(null, EventArgs.Empty);
                    return updateSuccess;
                }
            }

            // Check for duplicate
            if (all.Any(s =>
                string.Equals(s.Name, supplier.Name, StringComparison.OrdinalIgnoreCase) &&
                s.Contact == supplier.Contact))
            {
                return false;
            }

            all.Add(supplier);
            bool addSuccess = SaveSuppliers(all);
            if (addSuccess) SuppliersChanged?.Invoke(null, EventArgs.Empty);
            return addSuccess;
        }

        /// <summary>
        /// Delete a supplier from SQLite
        /// </summary>
        public static bool DeleteSupplier(string supplierId)
        {
            var all = LoadSuppliers();
            var removed = all.RemoveAll(s => s.Id == supplierId);

            if (removed > 0)
            {
                bool deleteSuccess = SaveSuppliers(all);
                if (deleteSuccess) SuppliersChanged?.Invoke(null, EventArgs.Empty);
                return deleteSuccess;
            }
            return false;
        }

        /// <summary>
        /// Find supplier by name and contact
        /// </summary>
        public static Supplier? FindByNameAndContact(string name, string contact)
        {
            return LoadSuppliers().FirstOrDefault(s =>
                string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase) &&
                s.Contact == contact);
        }
    }
} 