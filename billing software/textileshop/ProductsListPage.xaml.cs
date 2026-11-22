#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Microsoft.Win32;
using Newtonsoft.Json;
using textileshop.Services;
using textileshop.Models;


namespace textileshop
{
    // ══════════════════════════════════════════════════════════════════════════
    //  ProductRow — UI row with stock-status computed property
    // ══════════════════════════════════════════════════════════════════════════
    public class ProductRow : INotifyPropertyChanged
    {
        private int _qty;
        private int _threshold = 10;

        // ✅ Changed to string for auto-generated format
        public string Id { get; set; } = string.Empty;

        public string ProductName { get; set; } = string.Empty;
        public double Rate { get; set; }
        public double PurchasePrice { get; set; }
        public double ProfitAmount { get; set; }
        public double ProfitPct { get; set; }
        public double CostTotal { get; set; }
        public double CGSTPct { get; set; }
        public double CGSTAmt { get; set; }
        public double SGSTPct { get; set; }
        public double SGSTAmt { get; set; }
        public double NetRate { get; set; }
        public string ImportedAt { get; set; } = string.Empty;

        public int Qty
        {
            get => _qty;
            set { _qty = value; OnPropertyChanged(); OnPropertyChanged(nameof(StockStatus)); }
        }

        public int Threshold
        {
            get => _threshold;
            set { _threshold = value; OnPropertyChanged(nameof(StockStatus)); }
        }

        public string StockStatus =>
            _qty <= 0 ? "Out" :
            _qty <= _threshold ? "Low" : "Ok";

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ExcelProductItem ToProductItem() => new ExcelProductItem
        {
            Id = Id,
            ProductName = ProductName,
            Qty = Qty,
            Rate = Rate,
            PurchasePrice = PurchasePrice,
            ProfitAmount = ProfitAmount,
            ProfitPct = ProfitPct,
            CostTotal = CostTotal,
            CGSTPct = CGSTPct,
            CGSTAmt = CGSTAmt,
            SGSTPct = SGSTPct,
            SGSTAmt = SGSTAmt,
            NetRate = NetRate,
            ImportedAt = ImportedAt
        };

        public static ProductRow From(ExcelProductItem p, int threshold = 10) => new ProductRow
        {
            Id = p.Id ?? string.Empty,
            ProductName = p.ProductName,
            Qty = p.Qty,
            Rate = p.Rate,
            PurchasePrice = p.PurchasePrice,   // ← ADD THIS
            ProfitAmount = p.ProfitAmount,    // ← ADD THIS
            ProfitPct = p.ProfitPct,       // ← ADD THIS
            CostTotal = p.CostTotal,
            CGSTPct = p.CGSTPct,
            CGSTAmt = p.CGSTAmt,
            SGSTPct = p.SGSTPct,
            SGSTAmt = p.SGSTAmt,
            NetRate = p.NetRate,
            ImportedAt = p.ImportedAt,
            Threshold = threshold
        };
    } 

    // ══════════════════════════════════════════════════════════════════════════
    //  ProductsListPage
    // ══════════════════════════════════════════════════════════════════════════
    public partial class ProductsListPage : Page
    {
        private readonly List<ProductRow> _allRows = new();
        private readonly ObservableCollection<ProductRow> _viewRows = new();

       
        private int _threshold = 10;
        private string _searchText = string.Empty;
        private string _stockFilter = "All";
        private string _sortMode = "Name A → Z";   // ← fixed: spaces match XAML exactly

        public ProductsListPage()
        {
            InitializeComponent();

            // Subscribe to data change events
            InventoryService.DataChanged += OnInventoryDataChanged;

            // Use Unloaded event instead of OnNavigatedFrom
            this.Unloaded += ProductsListPage_Unloaded;

            LoadAll();
        }

        // ── Cleanup: Unsubscribe when page is unloaded ─────────────────────────
        private void ProductsListPage_Unloaded(object sender, RoutedEventArgs e)
        {
            InventoryService.DataChanged -= OnInventoryDataChanged;
            this.Unloaded -= ProductsListPage_Unloaded;
        }

        private void OnInventoryDataChanged(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() => {
                LoadAll();
                TxtStatus.Text = "📦 Data updated from import.";
            });
        }

        private void LoadAll()
        {
            _allRows.Clear();
            var items = InventoryService.LoadProducts();
            foreach (var p in items)
                _allRows.Add(ProductRow.From(p, _threshold));

            ApplyFiltersAndSort();
            UpdateKPIs();
            TxtLastRefresh.Text = "Last refreshed: " + DateTime.Now.ToString("dd MMM yyyy  HH:mm");
        }

        private void ApplyFiltersAndSort()
        {
            if (DgProducts == null || TxtShowing == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ UI controls not initialized - skipping filter apply");
                return;
            }

            foreach (var r in _allRows)
            {
                if (r != null) r.Threshold = _threshold;
            }

            IEnumerable<ProductRow> filtered = _allRows ?? Enumerable.Empty<ProductRow>();

            // Text search
            if (!string.IsNullOrWhiteSpace(_searchText))
            {
                filtered = filtered.Where(r =>
                    r != null &&
                    !string.IsNullOrEmpty(r.ProductName) &&
                    r.ProductName.IndexOf(_searchText, StringComparison.OrdinalIgnoreCase) >= 0);
            }

            // Stock chip filter
            filtered = _stockFilter switch
            {
                "In" => filtered.Where(r => r != null && r.StockStatus == "Ok"),
                "Low" => filtered.Where(r => r != null && r.StockStatus == "Low"),
                "Order" => filtered.Where(r => r != null && (r.StockStatus == "Low" || r.StockStatus == "Out")),
                _ => filtered.Where(r => r != null)
            };

            // Sort — strings match XAML ComboBoxItem Content exactly
            filtered = _sortMode switch
            {
                "Name Z → A" => filtered.OrderByDescending(r => r?.ProductName ?? string.Empty),
                "Qty: Low → High" => filtered.OrderBy(r => r?.Qty ?? 0),
                "Qty: High → Low" => filtered.OrderByDescending(r => r?.Qty ?? 0),
                "Net Rate: Low → High" => filtered.OrderBy(r => r?.NetRate ?? 0),
                "Net Rate: High → Low" => filtered.OrderByDescending(r => r?.NetRate ?? 0),
                "Imported: Newest" => filtered.OrderByDescending(r => r?.ImportedAt ?? string.Empty),
                "Imported: Oldest" => filtered.OrderBy(r => r?.ImportedAt ?? string.Empty),
                _ => filtered.OrderBy(r => r?.ProductName ?? string.Empty)  // "Name A → Z"
            };

            // Rebuild view collection
            _viewRows.Clear();
            foreach (var r in filtered)
            {
                if (r != null) _viewRows.Add(r);
            }

            DgProducts.ItemsSource = _viewRows;

            int viewCount = _viewRows?.Count ?? 0;
            int allCount = _allRows?.Count ?? 0;
            TxtShowing.Text = $"Showing {viewCount} of {allCount} product(s)";
        }
        private void UpdateKPIs()
        {
            int total = _allRows.Count;
            int low = _allRows.Count(r => r.StockStatus == "Low");
            int outStock = _allRows.Count(r => r.StockStatus == "Out");
            double val = _allRows.Sum(r => r.Qty * r.NetRate);

            KpiTotal.Text = total.ToString();
            KpiLowStock.Text = low.ToString();
            KpiOutStock.Text = outStock.ToString();
            KpiValue.Text = "₹ " + val.ToString("N2");
        }

        // ── PUBLIC METHODS ─────────────────────────────────────────────────────
        public void RefreshFromStorage()
        {
            LoadAll();
            TxtStatus.Text = $"📦 Refreshed: {_allRows.Count} product(s) loaded.";
        }

        public string GetProductDetailsList()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"📦 Product Inventory Report");
            sb.AppendLine($"Generated: {DateTime.Now:dd MMM yyyy HH:mm}");
            sb.AppendLine(new string('─', 80));
            sb.AppendLine();

            foreach (var row in _allRows.OrderBy(r => r.ProductName))
            {
                sb.AppendLine($"#{row.Id} | {row.ProductName}");
                sb.AppendLine($"   Qty: {row.Qty} | Status: {row.StockStatus}");
                sb.AppendLine($"   Rate: ₹{row.Rate:N2} | Net: ₹{row.NetRate:N2}");
                sb.AppendLine($"   Total Value: ₹{row.Qty * row.NetRate:N2}");
                sb.AppendLine($"   Imported: {row.ImportedAt}");
                sb.AppendLine();
            }

            sb.AppendLine(new string('─', 80));
            sb.AppendLine($"TOTAL PRODUCTS: {_allRows.Count}");
            sb.AppendLine($"TOTAL VALUE: ₹{_allRows.Sum(r => r.Qty * r.NetRate):N2}");

            return sb.ToString();
        }

        public void ShowProductDetails()
        {
            string details = GetProductDetailsList();
            MessageBox.Show(details, "Product Inventory Details",
                           MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── UI Events ─────────────────────────────────────────────────────────
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchText = TxtSearch.Text;
            ApplyFiltersAndSort();
        }

        private void TxtThreshold_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Allow empty string while user is mid-edit — don't reset threshold
            if (string.IsNullOrWhiteSpace(TxtThreshold.Text)) return;

            if (int.TryParse(TxtThreshold.Text, out int t) && t >= 0)
            {
                _threshold = t;
                ApplyFiltersAndSort();
                UpdateKPIs();
            }
        }

        private void StockChip_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton clickedChip) return;

            var chips = new[] { ChipAll, ChipInStock, ChipLow, ChipOrder };
            foreach (var chip in chips)
            {
                if (chip != null && chip != clickedChip)
                    chip.IsChecked = false;
            }

            _stockFilter = clickedChip switch
            {
                var c when c == ChipInStock => "In",
                var c when c == ChipLow => "Low",
                var c when c == ChipOrder => "Order",
                _ => "All"
            };

            ApplyFiltersAndSort();
        }

        private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbSort.SelectedItem is ComboBoxItem item)
            {
                _sortMode = item.Content?.ToString() ?? "Name A → Z";
                ApplyFiltersAndSort();
            }
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            LoadAll();
            TxtStatus.Text = "Refreshed.";
        }
        private void BtnAddProduct_Click(object sender, RoutedEventArgs e)
        {

            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is MainWindow main)
                main.NavigateToPage("import");
        }
        // ── Add these fields at the top of the class ──────────────────────────
        private ProductRow? _editingRow = null;

        // ── Replace BtnEdit_Click ──────────────────────────────────────────────
        private void BtnEdit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProductRow row)
            {
                _editingRow = row;

                // Populate modal fields
                EditTxtProductName.Text = row.ProductName;
                EditTxtQty.Text = row.Qty.ToString();
                EditTxtRate.Text = row.Rate.ToString("N2");
                EditTxtPurchasePrice.Text = row.PurchasePrice.ToString("N2");
                EditTxtCGST.Text = row.CGSTPct.ToString("N1");
                EditTxtSGST.Text = row.SGSTPct.ToString("N1");

                // Trigger auto-calc
                RecalcEditFields();

                // Show overlay and focus first field
                EditModalOverlay.Visibility = Visibility.Visible;
                EditTxtProductName.Focus();
            }
        }

        // ── Auto-calculate net rate + profit in edit modal ─────────────────────
        private void EditTxt_Calculate_Changed(object sender, TextChangedEventArgs e)
            => RecalcEditFields();

        private void RecalcEditFields()
        {
            if (EditTxtRate == null) return;

            double.TryParse(EditTxtRate.Text, out double rate);
            double.TryParse(EditTxtCGST.Text, out double cgst);
            double.TryParse(EditTxtSGST.Text, out double sgst);
            double.TryParse(EditTxtPurchasePrice.Text, out double purchase);

            double netRate = rate > 0 ? rate * (1 + (cgst + sgst) / 100.0) : 0;
            double profitAmt = (rate > 0 && purchase > 0) ? rate - purchase : 0;
            double profitPct = purchase > 0 ? (profitAmt / purchase) * 100.0 : 0;

            if (EditTxtNetRate != null) EditTxtNetRate.Text = netRate.ToString("N2");
            if (EditTxtProfitAmount != null) EditTxtProfitAmount.Text = profitAmt.ToString("N2");
            if (EditTxtProfitPct != null) EditTxtProfitPct.Text = profitPct.ToString("N2") + " %";
        }

        // ── Save edit ──────────────────────────────────────────────────────────
        private void BtnEditSave_Click(object sender, RoutedEventArgs e)
        {
            if (_editingRow == null) return;

            if (string.IsNullOrWhiteSpace(EditTxtProductName.Text))
            {
                MessageBox.Show("Product name is required.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                EditTxtProductName.Focus();
                return;
            }
            if (!int.TryParse(EditTxtQty.Text, out int qty) || qty < 0)
            {
                MessageBox.Show("Enter a valid quantity.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                EditTxtQty.Focus();
                return;
            }
            if (!double.TryParse(EditTxtRate.Text, out double rate) || rate <= 0)
            {
                MessageBox.Show("Enter a valid sales rate.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                EditTxtRate.Focus();
                return;
            }
            if (!double.TryParse(EditTxtPurchasePrice.Text, out double purchase) || purchase <= 0)
            {
                MessageBox.Show("Enter a valid purchase price.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                EditTxtPurchasePrice.Focus();
                return;
            }

            double.TryParse(EditTxtCGST.Text, out double cgst);
            double.TryParse(EditTxtSGST.Text, out double sgst);
            double.TryParse(EditTxtNetRate.Text, out double netRate);
            double profitAmt = rate - purchase;
            double profitPct = purchase > 0 ? (profitAmt / purchase) * 100.0 : 0;

            _editingRow.ProductName = EditTxtProductName.Text.Trim();
            _editingRow.Qty = qty;
            _editingRow.Rate = rate;
            _editingRow.PurchasePrice = purchase;
            _editingRow.ProfitAmount = profitAmt;
            _editingRow.ProfitPct = profitPct;
            _editingRow.CGSTPct = cgst;
            _editingRow.SGSTPct = sgst;
            _editingRow.NetRate = netRate;

            InventoryService.UpdateProduct(_editingRow.ToProductItem());
            UpdateKPIs();
            ApplyFiltersAndSort();
            TxtStatus.Text = $"✅ \"{_editingRow.ProductName}\" updated.";

            CloseEditModal();
        }

        // ── Cancel edit ────────────────────────────────────────────────────────
        private void BtnEditCancel_Click(object sender, RoutedEventArgs e)
            => CloseEditModal();

        // ── Esc / Enter keyboard handling ─────────────────────────────────────
        private void EditModal_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                CloseEditModal();
                e.Handled = true;
                return;
            }

            // Enter on SGST (TabIndex=6, last editable field) → Save
            if (e.Key == System.Windows.Input.Key.Enter &&
                System.Windows.Input.Keyboard.FocusedElement is TextBox tb &&
                tb.TabIndex == 6)
            {
                BtnEditSave_Click(sender, e);
                e.Handled = true;
            }
        }

        // ── Close modal ────────────────────────────────────────────────────────
        private void CloseEditModal()
        {
            EditModalOverlay.Visibility = Visibility.Collapsed;
            _editingRow = null;
        }

        // In ProductsListPage.xaml.cs, update BtnDelete_Click:

        private void BtnDelete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is ProductRow row)
            {
                var confirm = MessageBox.Show(
                    $"Delete \"{row.ProductName}\"?\nThis cannot be undone.",
                    "Confirm Delete",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (confirm != MessageBoxResult.Yes) return;

                // ✅ Pass string ID directly (no conversion)
                InventoryService.DeleteProduct(row.Id);

                _allRows.Remove(row);
                UpdateKPIs();
                ApplyFiltersAndSort();
                TxtStatus.Text = $"🗑 \"{row.ProductName}\" deleted.";
            }
        }

        private void StockBadge_Loaded(object sender, RoutedEventArgs e)
        {
            if (sender is not Border badge) return;
            if (badge.DataContext is not ProductRow row) return;
            if (badge.Child is not TextBlock txt) return;

            switch (row.StockStatus)
            {
                case "Ok":
                    badge.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#DCFCE7"));
                    badge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#86EFAC"));
                    badge.BorderThickness = new Thickness(1);
                    txt.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#166534"));
                    txt.Text = $"✔ {row.Qty}";
                    break;

                case "Low":
                    badge.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEF3C7"));
                    badge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FCD34D"));
                    badge.BorderThickness = new Thickness(1);
                    txt.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#92400E"));
                    txt.Text = $"⚠ {row.Qty}";
                    break;

                case "Out":
                    badge.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FEE2E2"));
                    badge.BorderBrush = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#FCA5A5"));
                    badge.BorderThickness = new Thickness(1);
                    txt.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#991B1B"));
                    txt.Text = "✖ Out";
                    break;

                default:
                    badge.Background = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#F3F4F6"));
                    badge.BorderThickness = new Thickness(0);
                    txt.Foreground = new System.Windows.Media.SolidColorBrush(
                        (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#6B7280"));
                    txt.Text = row.Qty.ToString();
                    break;
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = "products_export_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".csv"
            };

            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Id,Product Name,Qty,Rate,CD,Cost Total,CGST %,CGST Amt,SGST %,SGST Amt,Net Rate,Imported At,Stock Status");

            foreach (var r in _viewRows)
                sb.AppendLine($"{r.Id},\"{r.ProductName}\",{r.Qty},{r.Rate},{r.CostTotal},{r.CGSTPct},{r.CGSTAmt},{r.SGSTPct},{r.SGSTAmt},{r.NetRate},{r.ImportedAt},{r.StockStatus}");

            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            TxtStatus.Text = $"⬇ Exported {_viewRows.Count} rows to {Path.GetFileName(dlg.FileName)}";
        }

        private void SaveAll()
        {
            var list = _allRows.Select(r => r.ToProductItem()).ToList();
            InventoryService.SaveProducts(list);
        }
    }
}