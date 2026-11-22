// ── ReturnsPage.xaml.cs ────────────────────────────────────────────────────
#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    public partial class ReturnsPage : Page
    {
        private readonly ObservableCollection<ReturnRecord> _view = new();
        private List<ReturnRecord> _all = new();
        private List<ExcelProductItem> _products = new();

        public ReturnsPage()
        {
            InitializeComponent();
            ReturnsService.EnsureTable();
            LoadAll();
        }

        // ── Data ───────────────────────────────────────────────────────────
        private void LoadAll()
        {
            _all = ReturnsService.LoadReturns();
            _products = InventoryService.LoadProducts();
            ApplyFilter();
            UpdateKPIs();
        }

        private void ApplyFilter()
        {
            string q = TxtSearch.Text.Trim().ToLower();
            var filtered = string.IsNullOrEmpty(q)
                ? _all
                : _all.Where(r =>
                    r.ProductName.ToLower().Contains(q) ||
                    r.CustomerName.ToLower().Contains(q) ||
                    r.Reason.ToLower().Contains(q)).ToList();

            _view.Clear();
            foreach (var r in filtered) _view.Add(r);
            DgReturns.ItemsSource = _view;
            TxtShowing.Text = $"Showing {_view.Count} of {_all.Count} return(s)";
        }

        private void UpdateKPIs()
        {
            KpiTotalReturns.Text = _all.Count.ToString();
            KpiTotalItems.Text = _all.Sum(r => r.ReturnQty).ToString();
            KpiTotalRefund.Text = "₹ " + _all.Sum(r => r.RefundAmount).ToString("N2");

            var now = DateTime.Now;
            double thisMonth = _all
                .Where(r => r.ReturnDate.Year == now.Year && r.ReturnDate.Month == now.Month)
                .Sum(r => r.RefundAmount);
            KpiThisMonth.Text = "₹ " + thisMonth.ToString("N2");
        }

        // ── Events: list ──────────────────────────────────────────────────
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e) => ApplyFilter();

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            LoadAll();
            TxtStatus.Text = "Refreshed.";
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_view.Count == 0)
            {
                MessageBox.Show("No returns to export.", "Export", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"returns_{DateTime.Now:yyyyMMdd}.csv"
            };
            if (dlg.ShowDialog() != true) return;

            var sb = new StringBuilder();
            sb.AppendLine("Return ID,Date,Product ID,Product Name,Return Qty,Sale Rate,Refund Amount,Customer,Reason,Notes");
            foreach (var r in _view)
            {
                sb.AppendLine($"{r.Id},{r.ReturnDate:yyyy-MM-dd}," +
                              $"{r.ProductId},\"{r.ProductName}\",{r.ReturnQty}," +
                              $"{r.SaleRate:N2},{r.RefundAmount:N2}," +
                              $"\"{r.CustomerName}\",\"{r.Reason}\",\"{r.Notes}\"");
            }
            File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
            TxtStatus.Text = $"Exported {_view.Count} return(s).";
        }

        private void BtnDeleteReturn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReturnRecord ret) return;

            var confirm = MessageBox.Show(
                $"Delete return record {ret.Id}?\n\n" +
                $"Note: This only removes the record — stock is NOT reversed.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (confirm != MessageBoxResult.Yes) return;

            if (ReturnsService.DeleteReturn(ret.Id))
            {
                _all.Remove(ret);
                ApplyFilter();
                UpdateKPIs();
                TxtStatus.Text = $"Return {ret.Id} deleted.";
            }
        }

        // ── Modal: Open ────────────────────────────────────────────────────
        private void BtnNewReturn_Click(object sender, RoutedEventArgs e)
        {
            _products = InventoryService.LoadProducts();

            // Populate product dropdown: "ProductName  (ID: xxx  |  Stock: nnn)"
            CmbProduct.ItemsSource = _products
                .Select(p => $"{p.ProductName}  (ID: {p.Id}  |  Stock: {p.Qty})")
                .ToList();
            CmbProduct.SelectedIndex = -1;
            CmbProduct.Text = "Search and select product…";

            TxtProductInfo.Text = "Select a product to see current stock and rate";
            TxtReturnQty.Text = "1";
            TxtSaleRate.Text = "";
            TxtRefundAmount.Text = "₹ 0.00";
            TxtCustomerName.Text = "";
            TxtNotes.Text = "";
            CmbReason.SelectedIndex = 0;

            ModalOverlay.Visibility = Visibility.Visible;
            CmbProduct.Focus();
        }

        // ── Modal: Product selected ────────────────────────────────────────
        private void CmbProduct_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = CmbProduct.SelectedIndex;
            if (idx < 0 || idx >= _products.Count) return;

            var p = _products[idx];
            TxtProductInfo.Text = $"Current Stock: {p.Qty} units  |  Sales Rate: ₹{p.Rate:N2}  |  Net Rate: ₹{p.NetRate:N2}";
            TxtSaleRate.Text = p.Rate.ToString("N2");
            RecalcRefund();
        }

        // ── Modal: Recalculate refund ─────────────────────────────────────
        private void TxtReturnQty_TextChanged(object sender, TextChangedEventArgs e) => RecalcRefund();
        private void TxtSaleRate_TextChanged(object sender, TextChangedEventArgs e) => RecalcRefund();

        private void RecalcRefund()
        {
            int.TryParse(TxtReturnQty?.Text, out int qty);
            double.TryParse(TxtSaleRate?.Text, out double rate);
            double refund = qty * rate;
            if (TxtRefundAmount != null)
                TxtRefundAmount.Text = $"₹ {refund:N2}";
        }

        // ── Modal: Save ────────────────────────────────────────────────────
        private void BtnSaveReturn_Click(object sender, RoutedEventArgs e)
        {
            // ── Validation ──
            int productIdx = CmbProduct.SelectedIndex;
            if (productIdx < 0 || productIdx >= _products.Count)
            {
                Show("Please select a product.", CmbProduct);
                return;
            }

            if (!int.TryParse(TxtReturnQty.Text, out int qty) || qty <= 0)
            {
                Show("Return quantity must be a positive whole number.", TxtReturnQty);
                return;
            }

            var product = _products[productIdx];
            if (qty > product.Qty + qty) // allow returning even if somehow out-of-stock in system
            {
                // We allow it — shop may have sold before system update
            }

            if (!double.TryParse(TxtSaleRate.Text, out double rate) || rate <= 0)
            {
                Show("Enter the sale rate at which this was originally sold.", TxtSaleRate);
                return;
            }

            string reason = (CmbReason.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Other";

            var record = new ReturnRecord
            {
                ProductId = product.Id ?? "",
                ProductName = product.ProductName,
                ReturnQty = qty,
                SaleRate = rate,
                Reason = reason,
                CustomerName = TxtCustomerName.Text.Trim(),
                Notes = TxtNotes.Text.Trim(),
                ReturnDate = DateTime.Now
            };

            if (ReturnsService.SaveReturn(record))
            {
                MessageBox.Show(
                    $"✅ Return processed successfully!\n\n" +
                    $"Return ID:      {record.Id}\n" +
                    $"Product:        {record.ProductName}\n" +
                    $"Qty Returned:   {record.ReturnQty} units (added back to stock)\n" +
                    $"Refund Amount:  ₹{record.RefundAmount:N2}",
                    "Return Saved", MessageBoxButton.OK, MessageBoxImage.Information);

                CloseModal();
                LoadAll();
                TxtStatus.Text = $"↩ Return {record.Id} saved — stock restored.";
            }
            else
            {
                MessageBox.Show("Failed to save return. Please try again.", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Modal: Cancel / Close ─────────────────────────────────────────
        private void BtnCancelReturn_Click(object sender, RoutedEventArgs e) => CloseModal();

        private void CloseModal()
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void ModalOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape) { CloseModal(); e.Handled = true; }
        }

        // ── Helper ────────────────────────────────────────────────────────
        private static void Show(string msg, Control focus)
        {
            MessageBox.Show(msg, "Validation", MessageBoxButton.OK, MessageBoxImage.Warning);
            focus.Focus();
        }
    }
}