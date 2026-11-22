#nullable enable
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    public partial class Page2 : Page
    {
        private readonly ObservableCollection<ExcelProductItem> _products = new();
        private readonly ObservableCollection<ExcelProductItem> _pendingEntries = new();
        private List<ImportHistory> _cachedSuppliers = new();

        public Page2()
        {
            try
            {
                InitializeComponent();
                LoadFromFile();
                _cachedSuppliers = LoadSuppliersForDropdown();
                MigrateImportHistory();

                LbPendingItems.ItemsSource = _pendingEntries;
                UpdatePendingCount();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Page2 constructor: {ex.Message}");
            }
        }


        // ── Migrate Old JSON → SQLite ──────────────────────────────────────
        private void MigrateImportHistory()
        {
            try
            {
                string path = AppDataHelper.GetDataFilePath("import_history.json");
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(json)) return;

                var all = JsonConvert.DeserializeObject<List<ImportHistory>>(json);
                if (all == null || !all.Any()) return;

                foreach (var r in all)
                {
                    if (string.IsNullOrWhiteSpace(r.SupplierName))
                        r.SupplierName = "Manual Entry";
                    DatabaseHelper.SaveImportHistory(r);
                }

                File.Move(path, path + ".backup_" + DateTime.Now.ToString("yyyyMMdd"));
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] MigrateImportHistory: {ex.Message}");
            }
        }

        // ── Save Import History ────────────────────────────────────────────
        private void SaveImportHistory(string fileName, int itemCount,
            string supplierName, string supplierContact,
            List<ImportedProductSnapshot>? products = null)
        {
            try
            {
                var history = new ImportHistory
                {
                    ImportDate = DateTime.Now,
                    FileName = fileName,
                    ItemCount = itemCount,
                    SupplierName = supplierName,
                    SupplierContact = supplierContact,
                    Products = products
                };

                if (DatabaseHelper.SaveImportHistory(history))
                {
                    _cachedSuppliers = LoadSuppliersForDropdown();
                    if (StatusBar != null) StatusBar.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SaveImportHistory: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  SUPPLIERS DROPDOWN
        // ══════════════════════════════════════════════════════════════════════

        private List<ImportHistory> LoadSuppliersForDropdown()
        {
            var result = new List<ImportHistory>();
            try
            {
                var history = DatabaseHelper.LoadImportHistory();
                if (history?.Any() == true)
                {
                    result.AddRange(history
                        .Where(h => !string.IsNullOrWhiteSpace(h.SupplierName))
                        .GroupBy(h => $"{h.SupplierName}|{h.SupplierContact}")
                        .Select(g => g.First()));
                }

                foreach (var s in SuppliersService.LoadSuppliers())
                {
                    result.Add(new ImportHistory
                    {
                        SupplierName = s.Name,
                        SupplierContact = s.Contact,
                        ImportDate = s.CreatedAt,
                        FileName = "Manual Entry",
                        ItemCount = 0
                    });
                }

                return result
                    .GroupBy(s => $"{s.SupplierName}|{s.SupplierContact}")
                    .Select(g => g.First())
                    .OrderBy(s => s.SupplierName)
                    .ToList();
            }
            catch { return result; }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  MANUAL ENTRY (directly on page)
        // ══════════════════════════════════════════════════════════════════════

       

        

        private void TxtModal_Calculate_Changed(object sender, TextChangedEventArgs e)
        {
            if (TxtModal_Rate == null || TxtModal_CGST == null ||
                TxtModal_SGST == null || TxtModal_NetRate == null) return;

            double.TryParse(TxtModal_Rate.Text, out double rate);
            double.TryParse(TxtModal_CGST.Text, out double cgst);
            double.TryParse(TxtModal_SGST.Text, out double sgst);
            double.TryParse(TxtModal_PurchasePrice?.Text, out double purchase);

            double netRate = rate > 0 ? rate * (1 + (cgst + sgst) / 100.0) : 0;
            TxtModal_NetRate.Text = netRate.ToString("N2");

            if (purchase > 0 && rate > 0)
            {
                double profitAmt = rate - purchase;
                double profitPct = (profitAmt / purchase) * 100.0;
                if (TxtModal_ProfitAmount != null) TxtModal_ProfitAmount.Text = profitAmt.ToString("N2");
                if (TxtModal_ProfitPct != null) TxtModal_ProfitPct.Text = profitPct.ToString("N2") + " %";
            }
            else
            {
                if (TxtModal_ProfitAmount != null) TxtModal_ProfitAmount.Text = "0.00";
                if (TxtModal_ProfitPct != null) TxtModal_ProfitPct.Text = "0.00 %";
            }
        }

        // ── Enter key moves focus to the next field ────────────────────────
        private void TxtModal_Field_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                (sender as UIElement)?.MoveFocus(
                    new TraversalRequest(FocusNavigationDirection.Next));
                e.Handled = true;
            }
        }

        // ── Enter key on the last field triggers Add to List ───────────────
        private void TxtModal_LastField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnModalAddToList_Click(sender, e);
                e.Handled = true;
            }
        }
        private void BtnModalAddToList_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtModal_ProductName.Text))
            { MessageBox.Show("Product Name is required.", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); TxtModal_ProductName.Focus(); return; }

            if (!int.TryParse(TxtModal_Qty.Text, out int qty) || qty <= 0)
            { MessageBox.Show("Enter a valid quantity (> 0).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); TxtModal_Qty.Focus(); return; }

            if (!double.TryParse(TxtModal_Rate.Text, out double rate) || rate <= 0)
            { MessageBox.Show("Enter a valid sales rate (> 0).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); TxtModal_Rate.Focus(); return; }

            if (!double.TryParse(TxtModal_PurchasePrice.Text, out double purchase) || purchase <= 0)
            { MessageBox.Show("Enter a valid purchase price (> 0).", "Validation", MessageBoxButton.OK, MessageBoxImage.Warning); TxtModal_PurchasePrice.Focus(); return; }

            double.TryParse(TxtModal_CGST.Text, out double cgst);
            double.TryParse(TxtModal_SGST.Text, out double sgst);
            double.TryParse(TxtModal_NetRate.Text, out double netRate);
            double.TryParse(TxtModal_ProfitAmount.Text, out double profitAmt);
            double profitPct = purchase > 0 ? (profitAmt / purchase) * 100.0 : 0;

            
            _pendingEntries.Add(new ExcelProductItem
            {
                ProductName = TxtModal_ProductName.Text.Trim(),
                Qty = qty,
                Rate = rate,
                PurchasePrice = purchase,
                ProfitAmount = profitAmt,
                ProfitPct = profitPct,
                CGSTPct = cgst,
                SGSTPct = sgst,
                NetRate = netRate,
                ImportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            });

            ClearModalForm();
            UpdatePendingCount();
            TxtModal_ProductName.Focus();
        }

        private void BtnModalSaveAll_Click(object sender, RoutedEventArgs e)
        {
            if (_pendingEntries.Count == 0)
            {
                MessageBox.Show("Add at least one product before saving.", "Empty",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                if (InventoryService.AddProducts(_pendingEntries.ToList()))
                {
                    string supplierName = _pendingEntries.First().SupplierName ?? "Manual Entry";
                    string supplierContact = _pendingEntries.First().SupplierContact ?? "";

                    var snapshots = _pendingEntries.Select(p => new ImportedProductSnapshot
                    {
                        ProductName = p.ProductName,
                        Qty = p.Qty,
                        Rate = p.Rate,
                        PurchasePrice = p.PurchasePrice,
                        ProfitAmount = p.ProfitAmount,
                        ProfitPct = p.ProfitPct,
                        CGSTPct = p.CGSTPct,
                        CGSTAmt = p.CGSTAmt,
                        SGSTPct = p.SGSTPct,
                        SGSTAmt = p.SGSTAmt,
                        NetRate = p.NetRate,
                        SupplierName = p.SupplierName,
                        SupplierContact = p.SupplierContact,
                        ImportedAt = p.ImportedAt
                    }).ToList();

                    SaveImportHistory("Manual Entry", _pendingEntries.Count,
                        supplierName, supplierContact, snapshots);

                    TxtStatus.Text = $"✅ {_pendingEntries.Count} product(s) saved.";
                    StatusBar.Visibility = Visibility.Visible;

                    MessageBox.Show($"✅ {_pendingEntries.Count} product(s) saved!",
                        "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                    LoadFromFile();
                    _pendingEntries.Clear();
                    UpdatePendingCount();
                    ClearModalForm();
                }
                else
                {
                    MessageBox.Show("Failed to save. Please try again.", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error: {ex.Message}", "Save Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ClearModalForm()
        {
            TxtModal_ProductName.Text = "";
            TxtModal_Qty.Text = "1";
            TxtModal_Rate.Text = "";
            TxtModal_PurchasePrice.Text = "";
            TxtModal_CGST.Text = "0";
            TxtModal_SGST.Text = "0";
            TxtModal_NetRate.Text = "0.00";
            TxtModal_ProfitAmount.Text = "0.00";
            TxtModal_ProfitPct.Text = "0.00 %";
        }

        private void UpdatePendingCount()
        {
            if (TxtPendingCount != null)
                TxtPendingCount.Text = _pendingEntries.Count.ToString();
        }

        private void LoadFromFile()
        {
            _products.Clear();
            foreach (var p in InventoryService.LoadProducts())
                _products.Add(p);
        }
    }
} 