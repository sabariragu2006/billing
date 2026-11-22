#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    /// <summary>
    /// Page to view and manage import history records
    /// </summary>
    public partial class ImportHistoryPage : Page
    {
        private readonly ObservableCollection<ImportHistory> _imports = new();
        private List<ImportHistory> _allImports = new();
        private bool _isInitialized = false;

        public ImportHistoryPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;
                LoadImportHistory();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ ImportHistoryPage constructor failed: {ex.Message}");
                MessageBox.Show($"Page failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Load Data ──────────────────────────────────────────────────────
        private void LoadImportHistory()
        {
            if (!_isInitialized) return;

            try
            {
                _allImports.Clear();

                var imports = DatabaseHelper.LoadImportHistory();

                if (imports.Any())
                {
                    _allImports = imports; // already ordered DESC in DatabaseHelper
                    ApplyFilters();
                }
                else
                {
                    UpdateGrid();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadImportHistory failed: {ex.Message}");
                TxtStatus.Text = $"Error loading import history: {ex.Message}";
            }
        }

        // ── Apply Search & Date Filters ────────────────────────────────────
        private void ApplyFilters()
        {
            if (!_isInitialized) return;

            var search = TxtSearch.Text?.Trim().ToLower() ?? "";
            var dateFilter = (CmbDateFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

            var filtered = _allImports.Where(i =>
            {
                if (string.IsNullOrEmpty(search)) return true;

                return i.FileName?.ToLower().Contains(search) == true ||
                       i.SupplierName?.ToLower().Contains(search) == true ||
                       i.SupplierContact?.Contains(search) == true ||
                       i.ImportDate.ToString("dd MMM yyyy").ToLower().Contains(search);
            }).ToList();

            var now = DateTime.Now;
            filtered = dateFilter switch
            {
                "Today"          => filtered.Where(i => i.ImportDate.Date == now.Date).ToList(),
                "Last 7 Days"    => filtered.Where(i => i.ImportDate >= now.AddDays(-7)).ToList(),
                "This Month"     => filtered.Where(i => i.ImportDate.Year == now.Year && i.ImportDate.Month == now.Month).ToList(),
                "Last 3 Months"  => filtered.Where(i => i.ImportDate >= now.AddMonths(-3)).ToList(),
                _                => filtered
            };

            _imports.Clear();
            foreach (var import in filtered)
                _imports.Add(import);

            UpdateGrid();
        }

        // ── Update DataGrid ────────────────────────────────────────────────
        private void UpdateGrid()
        {
            if (!_isInitialized || DgImportHistory == null) return;

            DgImportHistory.ItemsSource = null;
            DgImportHistory.ItemsSource = _imports;

            int total   = _allImports.Count;
            int showing = _imports.Count;

            TxtRecordCount.Text = showing == total
                ? $"Showing {total} record(s)"
                : $"Showing {showing} of {total} record(s)";

            TxtStatus.Text = showing == 0
                ? "No import records found. Import products via Excel to see history here."
                : $"Loaded {showing} import record(s)";
        }

        // ── Event Handlers ─────────────────────────────────────────────────

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilters();
        }

        private void CmbDateFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilters();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            TxtSearch.Text = string.Empty;
            CmbDateFilter.SelectedIndex = 0;
            LoadImportHistory();
        }

        private void DgImportHistory_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || DgImportHistory.SelectedItem is not ImportHistory import) return;
            ViewImportDetails(import);
        }

        private void BtnViewDetails_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not ImportHistory import) return;
            ViewImportDetails(import);
        }

        // ── Open Preview Window ────────────────────────────────────────────
        private void ViewImportDetails(ImportHistory import)
        {
            try
            {
                var previewWindow = new ImportPreviewWindow(import)
                {
                    Owner = Window.GetWindow(this)
                };
                previewWindow.ShowDialog();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Import preview failed: {ex.Message}");
                ShowTextPreview(import);
            }
        }

        // ── Fallback text preview ──────────────────────────────────────────
        private void ShowTextPreview(ImportHistory import)
        {
            var settings = AppSettings.Load();
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine($"            {(settings.ShopName ?? "SHOP NAME").ToUpper()}");
            if (!string.IsNullOrWhiteSpace(settings.ShopAddress))
                sb.AppendLine($"         {settings.ShopAddress}");
            if (!string.IsNullOrWhiteSpace(settings.ShopPhone))
                sb.AppendLine($"         📞 {settings.ShopPhone}");
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine("               IMPORT RECORD");
            sb.AppendLine("───────────────────────────────────────────────────");
            sb.AppendLine($"Date     : {import.ImportDate:dd MMM yyyy, hh:mm tt}");
            sb.AppendLine($"File     : {import.FileName}");
            sb.AppendLine($"Supplier : {import.SupplierName ?? "—"}");
            sb.AppendLine($"Contact  : {import.SupplierContact ?? "—"}");
            sb.AppendLine($"Items    : {import.ItemCount:N0}");
            sb.AppendLine("───────────────────────────────────────────────────");

            if (import.Products != null && import.Products.Any())
            {
                sb.AppendLine($"{"Product",-28} {"Qty",5} {"Rate",10} {"Total",10}");
                sb.AppendLine("───────────────────────────────────────────────────");

                foreach (var product in import.Products)
                {
                    string name = product.ProductName?.Length > 28
                        ? product.ProductName[..25] + "..."
                        : product.ProductName ?? "";
                    sb.AppendLine($"{name,-28} {product.Qty,5} {product.Rate,10:N2} {product.NetRate,10:N2}");
                }

                double importTotal = import.Products.Sum(p => p.NetRate);
                int totalQty       = import.Products.Sum(p => p.Qty);

                sb.AppendLine("───────────────────────────────────────────────────");
                sb.AppendLine($"{"IMPORT TOTAL (" + totalQty + " items):",-33} {"₹" + importTotal.ToString("N2"),16}");
            }

            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine("  💡 Products have been added to your inventory.");
            sb.AppendLine("     View them in the Products List page.");
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine($"Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}");

            MessageBox.Show(sb.ToString(),
                $"Import Record — {import.FileName}",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Export CSV ─────────────────────────────────────────────────────
        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _imports.Count == 0)
            {
                MessageBox.Show("No import records to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter   = "CSV Files (*.csv)|*.csv",
                FileName = $"import_history_{DateTime.Now:yyyyMMdd}.csv",
                Title    = "Export Import History to CSV"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Import Date,File Name,Supplier Name,Supplier Contact,Items Imported");

                foreach (var import in _imports)
                {
                    string fileName       = import.FileName?.Replace("\"", "\"\"")        ?? "";
                    string supplierName   = import.SupplierName?.Replace("\"", "\"\"")    ?? "";
                    string supplierContact = import.SupplierContact?.Replace("\"", "\"\"") ?? "";

                    sb.AppendLine($"\"{import.ImportDate:yyyy-MM-dd HH:mm:ss}\",\"{fileName}\",\"{supplierName}\",\"{supplierContact}\",{import.ItemCount}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"✅ Exported {_imports.Count} record(s) to:\n{dlg.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Keyboard Shortcuts ─────────────────────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.F5 && _isInitialized)
            {
                BtnRefresh_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}