#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    public partial class BillHistoryPage : Page
    {
        private List<Bill> _allBills = new();
        private List<Bill> _filteredBills = new();
        private bool _isInitialized = false;

        public BillHistoryPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;
                LoadBills();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ BillHistoryPage constructor failed: {ex.Message}");
                MessageBox.Show($"Page failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LoadBills()
        {
            if (!_isInitialized) return;

            try
            {
                _allBills.Clear();
                _allBills = DatabaseHelper.LoadBills();
                ApplyFilters();
                System.Diagnostics.Debug.WriteLine($"[INFO] Loaded {_allBills.Count} bill(s) from SQLite");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading bills: {ex.Message}", "Load Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ApplyFilters()
        {
            if (!_isInitialized || TxtSearch == null || CmbDateFilter == null || DgBills == null)
            {
                System.Diagnostics.Debug.WriteLine("⚠️ ApplyFilters skipped: controls not initialized");
                return;
            }

            var search = TxtSearch.Text?.Trim().ToLower() ?? "";
            var dateFilter = (CmbDateFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();

            _filteredBills = _allBills.Where(b =>
            {
                // Resolve display values for search matching
                string displayName = string.IsNullOrWhiteSpace(b.CustomerName) ? "undisclosed customer" : b.CustomerName.ToLower();
                string displayMobile = string.IsNullOrWhiteSpace(b.CustomerMobile) || b.CustomerMobile == "-" ? "-" : b.CustomerMobile;

                bool matchesSearch = string.IsNullOrEmpty(search) ||
                    b.BillNumberFormatted?.ToLower().Contains(search) == true ||
                    displayName.Contains(search) ||
                    displayMobile.Contains(search) ||
                    b.BillDate.ToString("dd MMM yyyy").ToLower().Contains(search);

                bool matchesDate = dateFilter switch
                {
                    "Today" => b.BillDate.Date == DateTime.Today,
                    "Last 7 Days" => b.BillDate >= DateTime.Today.AddDays(-7),
                    "This Month" => b.BillDate.Year == DateTime.Now.Year && b.BillDate.Month == DateTime.Now.Month,
                    "Last 3 Months" => b.BillDate >= DateTime.Today.AddMonths(-3),
                    _ => true
                };

                return matchesSearch && matchesDate;
            }).ToList();

            DgBills.ItemsSource = null;
            DgBills.ItemsSource = _filteredBills;
            UpdateBillCount();
        }

        private void UpdateBillCount()
        {
            if (TxtBillCount == null) return;
            int total = _allBills.Count;
            int showing = _filteredBills.Count;
            TxtBillCount.Text = showing == total
                ? $"Showing {total} bill(s)"
                : $"Showing {showing} of {total} bill(s)";
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (TxtSearch != null) TxtSearch.Text = string.Empty;
            if (CmbDateFilter != null) CmbDateFilter.SelectedIndex = 0;
            LoadBills();
        }

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

        private void DgBills_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || DgBills?.SelectedItem is not Bill selectedBill) return;
            ShowBillPreview(selectedBill);
        }

        private void BtnPreviewBill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is Bill bill)
                ShowBillPreview(bill);
        }

        private void BtnPreviewSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || DgBills?.SelectedItem is not Bill selectedBill)
            {
                MessageBox.Show("Select a bill to preview.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }
            ShowBillPreview(selectedBill);
        }

        private void BtnPrintSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || DgBills?.SelectedItem is not Bill selectedBill)
            {
                MessageBox.Show("Select a bill to export as PDF.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    FileName = $"Bill_{selectedBill.BillNumberFormatted}.pdf",
                    Title = "Save Bill as PDF",
                    InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dlg.ShowDialog() != true) return;

                bool success = PdfGenerator.GenerateBillPdf(selectedBill, dlg.FileName);

                if (success)
                {
                    var result = MessageBox.Show(
                        $"✅ Bill saved as PDF:\n{dlg.FileName}\n\nOpen file now?",
                        "PDF Exported", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        try
                        {
                            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = dlg.FileName,
                                UseShellExecute = true
                            });
                        }
                        catch
                        {
                            MessageBox.Show("PDF saved. Open manually from:\n" + dlg.FileName,
                                "File Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                }
                else
                {
                    MessageBox.Show("Failed to generate PDF.", "Export Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] PDF export failed: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Export Failed",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || DgBills?.SelectedItem is not Bill selectedBill)
            {
                MessageBox.Show("Select a bill to delete.", "No Selection",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Delete bill {selectedBill.BillNumberFormatted}?\nThis cannot be undone.",
                "Confirm Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    bool deleted = DatabaseHelper.DeleteBillByNumber(selectedBill.BillNumber);
                    if (deleted)
                    {
                        LoadBills();
                        MessageBox.Show("Bill deleted successfully.", "Deleted",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show($"Bill {selectedBill.BillNumberFormatted} not found in database.",
                            "Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting bill: {ex.Message}", "Delete Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ShowBillPreview(Bill bill)
        {
            try
            {
                PdfGenerator.Initialize();

                string tempPdfPath = Path.Combine(Path.GetTempPath(), $"Preview_{bill.BillNumberFormatted}.pdf");

                if (File.Exists(tempPdfPath))
                {
                    try { File.Delete(tempPdfPath); } catch { }
                }

                bool success = PdfGenerator.GenerateBillPdf(bill, tempPdfPath);

                if (success && File.Exists(tempPdfPath))
                {
                    try
                    {
                        var previewWindow = new BillPreviewWindow(bill) { Owner = Window.GetWindow(this) };
                        previewWindow.ShowDialog();
                    }
                    catch (Exception modalEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[WARN] BillPreviewWindow failed: {modalEx.Message}");
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = tempPdfPath,
                            UseShellExecute = true
                        });
                    }
                }
                else
                {
                    MessageBox.Show(FormatBillForPreview(bill), $"Bill: {bill.BillNumberFormatted}",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                string errorMsg = ex is TypeInitializationException tie && tie.InnerException != null
                    ? tie.InnerException.Message : ex.Message;

                MessageBox.Show(
                    $"Preview error: {errorMsg}\n\nShowing text preview instead:\n\n{FormatBillForPreview(bill)}",
                    "Preview", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || !_filteredBills.Any())
            {
                MessageBox.Show("No bills to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"bills_export_{DateTime.Now:yyyyMMdd}.csv",
                Title = "Export Bills to CSV",
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("Bill Number,Date,Time,Customer,Mobile,Items,Subtotal,CGST,SGST,Grand Total");

                foreach (var bill in _filteredBills.OrderByDescending(b => b.BillDate))
                {
                    // Use display values in CSV too
                    string customer = string.IsNullOrWhiteSpace(bill.CustomerName)
                        ? "Undisclosed Customer"
                        : bill.CustomerName.Replace("\"", "\"\"");

                    string mobile = string.IsNullOrWhiteSpace(bill.CustomerMobile) || bill.CustomerMobile == "-"
                        ? "-"
                        : bill.CustomerMobile.Replace("\"", "\"\"");

                    sb.AppendLine(
                        $"\"{bill.BillNumberFormatted}\"," +
                        $"\"{bill.BillDate:yyyy-MM-dd}\"," +
                        $"\"{bill.BillDate:HH:mm}\"," +
                        $"\"{customer}\"," +
                        $"\"{mobile}\"," +
                        $"{(bill.Items?.Count ?? 0)}," +
                        $"{bill.SubTotal:N2}," +
                        $"{bill.TotalCGST:N2}," +
                        $"{bill.TotalSGST:N2}," +
                        $"{bill.GrandTotal:N2}"
                    );
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), System.Text.Encoding.UTF8);
                MessageBox.Show($"✅ Exported {_filteredBills.Count} bill(s) to:\n{dlg.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private string FormatBillForPreview(Bill bill)
        {
            var settings = AppSettings.Load();
            var sb = new System.Text.StringBuilder();

            string displayName = string.IsNullOrWhiteSpace(bill.CustomerName) ? "Undisclosed Customer" : bill.CustomerName;
            string displayMobile = string.IsNullOrWhiteSpace(bill.CustomerMobile) || bill.CustomerMobile == "-" ? "-" : bill.CustomerMobile;

            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"         {settings.ShopName.ToUpper()}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine($"Bill No: {bill.BillNumberFormatted}");
            sb.AppendLine($"Date:    {bill.BillDate:dd MMM yyyy, hh:mm tt}");
            sb.AppendLine($"Customer:{displayName}");
            sb.AppendLine($"Mobile:  {displayMobile}");
            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"{"Product",-25} {"Rate",8} {"Qty",5} {"Total",10}");
            sb.AppendLine("───────────────────────────────────────");

            foreach (var item in bill.Items ?? Enumerable.Empty<BillItem>())
                sb.AppendLine($"{item.ProductName,-25} {item.Rate,8:N2} {item.Quantity,5} {item.Total,10:N2}");

            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"Subtotal:    ₹ {bill.SubTotal:N2}");
            sb.AppendLine($"CGST:        ₹ {bill.TotalCGST:N2}");
            sb.AppendLine($"SGST:        ₹ {bill.TotalSGST:N2}");
            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"GRAND TOTAL: ₹ {bill.GrandTotal:N2}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("     Thank you for your purchase!      ");
            sb.AppendLine($"Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}");

            return sb.ToString();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.Key == Key.F5 && _isInitialized)
            {
                LoadBills();
                e.Handled = true;
            }
            if (e.Key == Key.Delete && _isInitialized && DgBills?.SelectedItem != null)
            {
                BtnDeleteSelected_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}