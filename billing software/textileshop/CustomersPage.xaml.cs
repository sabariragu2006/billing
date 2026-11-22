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
    /// Customer analytics page — all data loaded from SQLite via DatabaseHelper
    /// </summary>
    public partial class CustomersPage : Page
    {
        private readonly ObservableCollection<CustomerSummary> _customers = new();
        private List<CustomerSummary> _allCustomers = new();
        private bool _isInitialized = false;

        public CustomersPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;
                LoadCustomers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ CustomersPage constructor failed: {ex.Message}");
                MessageBox.Show($"Page failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Load & Aggregate Customers from SQLite ────────────────────────
        private void LoadCustomers()
        {
            if (!_isInitialized) return;

            try
            {
                _allCustomers.Clear();

                // ✅ Aggregate customers from Bills table in SQLite
                _allCustomers = DatabaseHelper.LoadCustomerSummaries()
                    .OrderByDescending(c => c.LastPurchaseDate)
                    .ToList();

                ApplyFilters();

                System.Diagnostics.Debug.WriteLine($"[INFO] Loaded {_allCustomers.Count} customer(s) from SQLite");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadCustomers failed: {ex.Message}");
                if (TxtStatus != null)
                    TxtStatus.Text = $"Error loading customers: {ex.Message}";
            }
        }

        // ── Apply Search & Sort Filters ───────────────────────────────────
        private void ApplyFilters()
        {
            if (!_isInitialized) return;

            var search = TxtSearch?.Text?.Trim().ToLower() ?? "";
            var sortOption = (CmbSort?.SelectedItem as ComboBoxItem)?.Content?.ToString();

            var filtered = _allCustomers.Where(c =>
            {
                if (string.IsNullOrEmpty(search)) return true;
                return c.Name.ToLower().Contains(search) || c.Mobile.Contains(search);
            }).ToList();

            filtered = sortOption switch
            {
                "Name Z→A" => filtered.OrderByDescending(c => c.Name).ToList(),
                "Last Purchase (Newest)" => filtered.OrderByDescending(c => c.LastPurchaseDate).ToList(),
                "Last Purchase (Oldest)" => filtered.OrderBy(c => c.LastPurchaseDate).ToList(),
                "Most Purchases" => filtered.OrderByDescending(c => c.PurchaseCount).ToList(),
                "Total Spent (High→Low)" => filtered.OrderByDescending(c => c.TotalSpent).ToList(),
                _ => filtered.OrderBy(c => c.Name).ToList() // Default: Name A→Z
            };

            _customers.Clear();
            foreach (var c in filtered)
                _customers.Add(c);

            UpdateGrid();
        }

        private void UpdateGrid()
        {
            if (!_isInitialized || DgCustomers == null) return;

            DgCustomers.ItemsSource = null;
            DgCustomers.ItemsSource = _customers;

            int total = _allCustomers.Count;
            int showing = _customers.Count;

            if (TxtCustomerCount != null)
                TxtCustomerCount.Text = showing == total
                    ? $"Showing {total} customer(s)"
                    : $"Showing {showing} of {total} customer(s)";

            if (TxtStatus != null)
            {
                if (showing == 0)
                {
                    TxtStatus.Text = "No customers found. Create bills to see customer analytics.";
                }
                else
                {
                    // ✅ Bill count from SQLite
                    int billCount = DatabaseHelper.LoadBills().Count;
                    TxtStatus.Text = $"Loaded {showing} customer(s) from {billCount} bill(s)";
                }
            }
        }

        // ── View Individual Customer's Purchase History ───────────────────
        private void ViewCustomerHistory(CustomerSummary customer)
        {
            try
            {
                // ✅ Load all bills from SQLite, then filter by customer
                var allBills = DatabaseHelper.LoadBills();

                var customerBills = allBills
                    .Where(b =>
                        string.Equals(b.CustomerName, customer.Name, StringComparison.OrdinalIgnoreCase) &&
                        b.CustomerMobile == customer.Mobile)
                    .OrderByDescending(b => b.BillDate)
                    .ToList();

                var historyText = new StringBuilder();
                historyText.AppendLine($"📋 Purchase History: {customer.Name} ({customer.Mobile})");
                historyText.AppendLine(new string('─', 60));
                historyText.AppendLine($"Total Purchases : {customer.PurchaseCount}");
                historyText.AppendLine($"Total Spent     : ₹{customer.TotalSpent:N2}");
                historyText.AppendLine($"First Purchase  : {customer.FirstPurchaseDate:dd MMM yyyy}");
                historyText.AppendLine($"Last Purchase   : {customer.LastPurchaseDate:dd MMM yyyy}");
                historyText.AppendLine(new string('─', 60));
                historyText.AppendLine();

                foreach (var bill in customerBills)
                {
                    historyText.AppendLine($"📅 {bill.BillDate:dd MMM yyyy, hh:mm tt}");
                    historyText.AppendLine($"   Bill  : {bill.BillNumberFormatted}");
                    historyText.AppendLine($"   Items : {bill.Items.Count}   |   Total : ₹{bill.GrandTotal:N2}");
                    historyText.AppendLine();
                }

                MessageBox.Show(historyText.ToString(),
                    $"Customer History: {customer.Name}",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading history: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Event Handlers ────────────────────────────────────────────────

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilters();
        }

        private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilters();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            if (TxtSearch != null) TxtSearch.Text = string.Empty;
            if (CmbSort != null) CmbSort.SelectedIndex = 0;
            LoadCustomers();
        }

        private void DgCustomers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || DgCustomers.SelectedItem is not CustomerSummary customer) return;
            ViewCustomerHistory(customer);
        }

        private void BtnViewHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not CustomerSummary customer) return;
            ViewCustomerHistory(customer);
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _customers.Count == 0)
            {
                MessageBox.Show("No customers to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"customers_{DateTime.Now:yyyyMMdd}.csv",
                Title = "Export Customers to CSV"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Customer Name,Mobile,First Purchase,Last Purchase,Purchase Count,Total Spent (₹)");

                foreach (var c in _customers)
                {
                    string escapedName = c.Name.Replace("\"", "\"\"");
                    sb.AppendLine($"\"{escapedName}\",{c.Mobile},{c.FirstPurchaseDate:yyyy-MM-dd},{c.LastPurchaseDate:yyyy-MM-dd},{c.PurchaseCount},{c.TotalSpent:N2}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"✅ Exported {_customers.Count} customer(s) to:\n{dlg.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Keyboard Shortcuts ────────────────────────────────────────────
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

    // ── Customer Summary Model (aggregated from Bills) ─────────────────────
    public class CustomerSummary
    {
        public string Name { get; set; } = string.Empty;
        public string Mobile { get; set; } = string.Empty;
        public DateTime FirstPurchaseDate { get; set; }
        public DateTime LastPurchaseDate { get; set; }
        public int PurchaseCount { get; set; }
        public double TotalSpent { get; set; }
    }
} 