#nullable enable
using Microsoft.Win32;
using Newtonsoft.Json;
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
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    public partial class BillingPage : Page
    {
        private readonly ObservableCollection<BillItem> _billItems = new();
        private readonly List<ExcelProductItem> _allProducts = new();
        private string _currentBillNumber = string.Empty;
        private List<CustomerInfo> _customers = new();
        private TextBox? _customerNameEditableBox;
        private TextBox? _customerMobileEditableBox;
        private bool _suppressCustomerSync = false;
        private bool _isInitialized = false;

        public BillingPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;

#if DEBUG
                System.Diagnostics.Debug.WriteLine($"[DEBUG] BillingPage initialized");
#endif
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ InitializeComponent failed: {ex.Message}");
                MessageBox.Show($"Page failed to load:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            LoadProducts();
            GenerateBillNumber();
            UpdateTotals();
        }

        // ── Load Products from Inventory ─────────────────────────────────────
        private void LoadProducts()
        {
            if (!_isInitialized) return;

            _allProducts.Clear();
            var products = InventoryService.LoadProducts();
            _allProducts.AddRange(products);

            CmbProducts.ItemsSource = _allProducts
                .Where(p => p.Qty > 0)
                .OrderBy(p => p.ProductName)
                .Select(p => p.ProductName)
                .ToList();
        }

        // ── Generate Unique Bill Number (String Format) ──────────────────────
        private void GenerateBillNumber()
        {
            if (!_isInitialized) return;

            int randomPart = new Random().Next(1000, 9999);
            _currentBillNumber = $"INV-{DateTime.Now:yyyyMMdd}-{randomPart:D4}";

            TxtBillNumber.Text = _currentBillNumber;
            TxtBillDate.Text = $"Date: {DateTime.Now:dd MMM yyyy, hh:mm tt}";
        }

        // ── ComboBox: Show Only In-Stock Products ────────────────────────────
        private void CmbProducts_DropDownOpened(object sender, EventArgs e)
        {
            if (!_isInitialized) return;

            CmbProducts.ItemsSource = _allProducts
                .Where(p => p.Qty > 0)
                .OrderBy(p => p.ProductName)
                .Select(p => p.ProductName)
                .ToList();
        }

        // ── ComboBox: Product Selected ───────────────────────────────────────
        private void CmbProducts_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized || CmbProducts.SelectedItem is not string productName) return;

            var product = _allProducts.FirstOrDefault(p => p.ProductName == productName);
            if (product != null)
            {
                TxtRate.Text = product.NetRate.ToString("N2");
                TxtQuantity.Text = "1";
                UpdateLineTotal(product.NetRate, 1);

                if (product.Qty <= 10)
                {
                    TxtStockWarning.Text = $"⚠ Only {product.Qty} units in stock!";
                    TxtStockWarning.Visibility = Visibility.Visible;
                }
                else
                {
                    TxtStockWarning.Visibility = Visibility.Collapsed;
                }
            }
        }

        // ── Update Line Total Display ────────────────────────────────────────
        private void UpdateLineTotal(double rate, int qty)
        {
            if (!_isInitialized) return;
            double subtotal = rate * qty;
            TxtLineTotal.Text = $"₹ {subtotal:N2}";
        }

        // ── Input Validation: Numbers Only ───────────────────────────────────
        private void TxtQuantity_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        private void TxtCustomerMobile_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            e.Handled = !Regex.IsMatch(e.Text, "^[0-9]+$");
        }

        // ── Update Line Total on Quantity Change ─────────────────────────────
        private void TxtQuantity_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized || CmbProducts.SelectedItem is not string productName) return;

            if (int.TryParse(TxtQuantity.Text, out int qty) && qty > 0)
            {
                var product = _allProducts.FirstOrDefault(p => p.ProductName == productName);
                if (product != null)
                {
                    UpdateLineTotal(product.NetRate, qty);
                }
            }
        }

        // ── Add Item to Bill ─────────────────────────────────────────────────
        private void BtnAddToBill_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (CmbProducts.SelectedItem is not string productName)
            {
                MessageBox.Show("Please select a product.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                CmbProducts.Focus();
                return;
            }

            if (!int.TryParse(TxtQuantity.Text, out int qty) || qty <= 0)
            {
                MessageBox.Show("Please enter a valid quantity.", "Validation",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                TxtQuantity.Focus();
                return;
            }

            var product = _allProducts.FirstOrDefault(p => p.ProductName == productName);
            if (product == null) return;

            if (qty > product.Qty)
            {
                MessageBox.Show($"Only {product.Qty} units available in stock!", "Out of Stock",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var billItem = new BillItem
            {
                ProductId = product.Id,
                ProductName = product.ProductName,
                Rate = product.NetRate,
                Quantity = qty,
                CGSTPct = product.CGSTPct,
                SGSTPct = product.SGSTPct,
            };

            _billItems.Add(billItem);
            RefreshBillGrid();
            UpdateTotals();

            // Reset form
            CmbProducts.SelectedItem = null;
            CmbProducts.Text = string.Empty;
            TxtQuantity.Text = "1";
            TxtLineTotal.Text = "₹ 0.00";
            TxtStockWarning.Visibility = Visibility.Collapsed;
            TxtRate.Text = string.Empty;

            CmbProducts.Focus();
        }

        // ── Save Bill ────────────────────────────────────────────────────────
        private void BtnSaveBill_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _billItems.Count == 0)
            {
                MessageBox.Show("Add items to the bill first.", "Empty Bill",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                // Step 1: Validate stock availability
                var products = InventoryService.LoadProducts();
                var stockErrors = new List<string>();

                foreach (var item in _billItems)
                {
                    var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                    if (product == null)
                        stockErrors.Add($"Product '{item.ProductName}' not found in inventory.");
                    else if (item.Quantity > product.Qty)
                        stockErrors.Add($"Insufficient stock for '{item.ProductName}' (need {item.Quantity}, have {product.Qty}).");
                }

                if (stockErrors.Any())
                {
                    MessageBox.Show($"Cannot save bill:\n\n{string.Join("\n", stockErrors)}",
                        "Stock Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Step 2: Reduce stock for each item
                foreach (var item in _billItems)
                {
                    var product = products.FirstOrDefault(p => p.Id == item.ProductId);
                    if (product != null)
                    {
                        product.Qty -= item.Quantity;
                        InventoryService.UpdateProduct(product);
                    }
                }

                // Step 3: Create and save bill — customer fields are optional
                var bill = new Bill
                {
                    BillNumber = _currentBillNumber,
                    CustomerName = string.IsNullOrWhiteSpace(TxtCustomerName.Text)
                                   ? "Walk-in Customer" : TxtCustomerName.Text.Trim(),
                    CustomerMobile = string.IsNullOrWhiteSpace(TxtCustomerMobile.Text)
                                     ? "-" : TxtCustomerMobile.Text.Trim(),
                    Items = _billItems.ToList(),
                    BillDate = DateTime.Now
                };

                bool success = DatabaseHelper.SaveBill(bill);

                if (!success)
                {
                    MessageBox.Show("Failed to save bill to database.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                MessageBox.Show(
                    $"✅ Bill {bill.BillNumberFormatted} saved successfully!\n\nStock updated for {_billItems.Count} item(s).",
                    "Bill Saved", MessageBoxButton.OK, MessageBoxImage.Information);

                BtnClearBill_Click(sender, e);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Save bill failed: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Error saving bill: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Refresh DataGrid ─────────────────────────────────────────────────
        private void RefreshBillGrid()
        {
            if (!_isInitialized || DgBillItems == null) return;
            DgBillItems.ItemsSource = null;
            DgBillItems.ItemsSource = _billItems;

            bool hasItems = _billItems.Count > 0;
            EmptyCartPlaceholder.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            DgBillItems.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;

            TxtItemCount.Text = _billItems.Count.ToString();
        }

        // ── Remove Item from Bill ────────────────────────────────────────────
        private void BtnRemoveItem_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (sender is Button btn && btn.Tag is BillItem item)
            {
                _billItems.Remove(item);
                RefreshBillGrid();
                UpdateTotals();
            }
        }

        // ── ✅ EXPORT BILL AS PDF ───────────────────────────────────────────
        private void BtnPrintBill_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_billItems.Count == 0)
                {
                    MessageBox.Show("Add items to the bill first.", "Empty Bill",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var bill = new Bill
                {
                    BillNumber = _currentBillNumber,
                    CustomerName = string.IsNullOrWhiteSpace(TxtCustomerName.Text)
                                     ? "Walk-in Customer" : TxtCustomerName.Text.Trim(),
                    CustomerMobile = string.IsNullOrWhiteSpace(TxtCustomerMobile.Text)
                                     ? "-" : TxtCustomerMobile.Text.Trim(),
                    Items = _billItems.ToList(),
                    BillDate = DateTime.Now
                };

                // ── Open the same BillPreviewWindow used in Bill History ──
                try
                {
                    PdfGenerator.Initialize();

                    string tempPdfPath = Path.Combine(
                        Path.GetTempPath(), $"Preview_{bill.BillNumberFormatted}.pdf");

                    if (File.Exists(tempPdfPath))
                    {
                        try { File.Delete(tempPdfPath); } catch { }
                    }

                    bool pdfOk = PdfGenerator.GenerateBillPdf(bill, tempPdfPath);

                    if (pdfOk && File.Exists(tempPdfPath))
                    {
                        var previewWindow = new BillPreviewWindow(bill)
                        {
                            Owner = Window.GetWindow(this)
                        };
                        previewWindow.ShowDialog();
                    }
                    else
                    {
                        // Fallback: ask user to save PDF manually
                        var dlg = new SaveFileDialog
                        {
                            Filter = "PDF Files (*.pdf)|*.pdf",
                            FileName = $"Bill_{bill.BillNumberFormatted}.pdf",
                            Title = "Save Bill as PDF",
                            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                        };

                        if (dlg.ShowDialog() != true) return;

                        bool saved = PdfGenerator.GenerateBillPdf(bill, dlg.FileName);
                        if (saved)
                        {
                            var result = MessageBox.Show(
                                $"✅ Bill saved as PDF:\n{dlg.FileName}\n\nOpen file now?",
                                "PDF Exported", MessageBoxButton.YesNo, MessageBoxImage.Information);

                            if (result == MessageBoxResult.Yes)
                                System.Diagnostics.Process.Start(
                                    new System.Diagnostics.ProcessStartInfo
                                    {
                                        FileName = dlg.FileName,
                                        UseShellExecute = true
                                    });
                        }
                        else
                        {
                            MessageBox.Show("Failed to generate PDF.", "Export Error",
                                MessageBoxButton.OK, MessageBoxImage.Error);
                        }
                    }
                }
                catch (Exception previewEx)
                {
                    System.Diagnostics.Debug.WriteLine($"[WARN] BillPreviewWindow failed: {previewEx.Message}");

                    // Final fallback: save-file dialog
                    var dlg = new SaveFileDialog
                    {
                        Filter = "PDF Files (*.pdf)|*.pdf",
                        FileName = $"Bill_{bill.BillNumberFormatted}.pdf",
                        Title = "Save Bill as PDF",
                        InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                    };

                    if (dlg.ShowDialog() != true) return;

                    bool saved = PdfGenerator.GenerateBillPdf(bill, dlg.FileName);
                    if (saved)
                    {
                        var open = MessageBox.Show(
                            $"✅ Bill saved:\n{dlg.FileName}\n\nOpen now?",
                            "Saved", MessageBoxButton.YesNo, MessageBoxImage.Information);

                        if (open == MessageBoxResult.Yes)
                            System.Diagnostics.Process.Start(
                                new System.Diagnostics.ProcessStartInfo
                                {
                                    FileName = dlg.FileName,
                                    UseShellExecute = true
                                });
                    }
                    else
                    {
                        MessageBox.Show("Failed to generate PDF.", "Error",
                            MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Print bill failed: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Print Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Update Totals Display ────────────────────────────────────────────
        private void UpdateTotals()
        {
            if (!_isInitialized) return;

            double subtotal = _billItems.Sum(i => i.SubTotal);
            double cgst = _billItems.Sum(i => i.CGSTAmt);
            double sgst = _billItems.Sum(i => i.SGSTAmt);
            double grand = subtotal + cgst + sgst;

            TxtSubTotal.Text = $"₹ {subtotal:N2}";
            TxtCGST.Text = $"₹ {cgst:N2}";
            TxtSGST.Text = $"₹ {sgst:N2}";
            TxtGrandTotal.Text = $"₹ {grand:N2}";

            // Keep badge + placeholder in sync
            bool hasItems = _billItems.Count > 0;
            if (EmptyCartPlaceholder != null)
                EmptyCartPlaceholder.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
            if (DgBillItems != null)
                DgBillItems.Visibility = hasItems ? Visibility.Visible : Visibility.Collapsed;
            if (TxtItemCount != null)
                TxtItemCount.Text = _billItems.Count.ToString();
        }

        // ── Clear Bill Form ──────────────────────────────────────────────────
        private void BtnClearBill_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _billItems.Count == 0) return;

            var result = MessageBox.Show("Clear all items from this bill?", "Confirm Clear",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                _billItems.Clear();
                RefreshBillGrid();
                TxtCustomerName.Text = string.Empty;
                TxtCustomerMobile.Text = string.Empty;
                UpdateTotals();
                GenerateBillNumber();
            }
        }

       

 



        // ── Helper: Format Bill Text for Preview ─────────────────────────────
        private string FormatBillForPreview(Bill bill)
        {
            var settings = AppSettings.Load();
            var sb = new StringBuilder();

            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"         {settings.ShopName.ToUpper()}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Bill No: {bill.BillNumberFormatted}");
            sb.AppendLine($"Date: {bill.BillDate:dd MMM yyyy, hh:mm tt}");
            sb.AppendLine($"Customer: {bill.CustomerName}");
            sb.AppendLine($"Mobile: {bill.CustomerMobile}");
            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"{"Product",-25} {"Rate",8} {"Qty",5} {"Total",10}");
            sb.AppendLine("───────────────────────────────────────");

            foreach (var item in bill.Items)
            {
                sb.AppendLine($"{item.ProductName,-25} {item.Rate,8:N2} {item.Quantity,5} {item.Total,10:N2}");
            }

            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"Subtotal: ₹ {bill.SubTotal:N2}");
            sb.AppendLine($"CGST:     ₹ {bill.TotalCGST:N2}");
            sb.AppendLine($"SGST:     ₹ {bill.TotalSGST:N2}");
            sb.AppendLine($"───────────────────────────────────────");
            sb.AppendLine($"GRAND TOTAL: ₹ {bill.GrandTotal:N2}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("        Thank you for your purchase!   ");

            return sb.ToString();
        }
        // ── Enter on ComboBox → move focus to Quantity ───────────────────────────
        private void CmbProducts_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                // Close dropdown if open
                if (CmbProducts.IsDropDownOpen)
                    CmbProducts.IsDropDownOpen = false;

                // Only move to quantity if a product is actually selected
                if (CmbProducts.SelectedItem != null || !string.IsNullOrWhiteSpace(CmbProducts.Text))
                {
                    TxtQuantity.Focus();
                    TxtQuantity.SelectAll();
                }
                e.Handled = true;
            }
        }

        // ── Enter on Quantity → add to bill ─────────────────────────────────────
        private void TxtQuantity_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnAddToBill_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // ── Keyboard Shortcuts ───────────────────────────────────────────────
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F5 && _isInitialized)
            {
                LoadProducts();
                e.Handled = true;
            }
        }
    }
} 