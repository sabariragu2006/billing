#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    /// <summary>
    /// Dashboard page — all data loaded from SQLite via DatabaseHelper
    /// </summary>
    public partial class DashboardPage : Page
    {
        private readonly ObservableCollection<ActivityItem> _activities = new();
        private bool _isInitialized = false;

        public DashboardPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;
                LoadDashboardData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ DashboardPage constructor failed: {ex.Message}");
                MessageBox.Show($"Dashboard failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Load All Dashboard Data ────────────────────────────────────────
        private void LoadDashboardData()
        {
            if (!_isInitialized) return;

            try
            {
                LoadKpiMetrics();
                LoadChartsData();
                LoadRecentActivity();

                if (TxtLastUpdated != null)
                    TxtLastUpdated.Text = $"Last updated: {DateTime.Now:dd MMM yyyy, hh:mm tt}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadDashboardData failed: {ex.Message}");
            }
        }

        // ── KPI Metrics ───────────────────────────────────────────────────
        private void LoadKpiMetrics()
        {
            try
            {
                // ✅ Products from SQLite
                var products = DatabaseHelper.LoadProducts();

                if (KpiTotalProducts != null)
                    KpiTotalProducts.Text = products.Count.ToString("N0");

                int lowStock = products.Count(p => p.Qty <= 10);
                if (KpiLowStock != null)
                {
                    KpiLowStock.Text = lowStock.ToString();
                    KpiLowStock.Foreground = lowStock > 0
                        ? new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(239, 68, 68))   // #EF4444
                        : new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(16, 185, 129)); // #10B981
                }

                // ✅ Customers aggregated from Bills in SQLite
                var customers = DatabaseHelper.LoadCustomerSummaries();
                if (KpiTotalCustomers != null)
                    KpiTotalCustomers.Text = customers.Count.ToString("N0");

                // ✅ Total sales via SQL SUM
                double totalSales = DatabaseHelper.GetTotalSales();
                if (KpiTotalSales != null)
                    KpiTotalSales.Text = $"₹ {totalSales:N2}";

                // Monthly change calculations
                int newProducts = products.Count(p =>
                    !string.IsNullOrEmpty(p.ImportedAt) &&
                    DateTime.TryParse(p.ImportedAt, out DateTime dt) &&
                    dt > DateTime.Now.AddMonths(-1));

                if (KpiProductsChange != null)
                    KpiProductsChange.Text = $"+{newProducts} this month";

                int newCustomers = customers.Count(c => c.FirstPurchaseDate > DateTime.Now.AddMonths(-1));
                if (KpiCustomersChange != null)
                    KpiCustomersChange.Text = $"+{newCustomers} this month";

                if (KpiSalesChange != null)
                    KpiSalesChange.Text = "+0% vs last month"; // Extend later with prior-month query
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadKpiMetrics failed: {ex.Message}");
            }
        }

        // ── Charts & Visualizations ───────────────────────────────────────
        private void LoadChartsData()
        {
            try
            {
                if (ChartSalesTrend == null) return;
                ChartSalesTrend.Children.Clear();

                // ✅ Monthly sales from SQLite
                var monthlySales = DatabaseHelper.GetMonthlySalesData();

                foreach (var month in monthlySales)
                {
                    var barContainer = new StackPanel
                    {
                        Orientation = Orientation.Horizontal,
                        Margin = new Thickness(0, 0, 0, 8)
                    };

                    // Month label
                    var monthLabel = new TextBlock
                    {
                        Text = month.Key,
                        Width = 70,
                        FontSize = 11,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(71, 85, 105)), // #475569
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };

                    // Bar — gradient fill using a border with LinearGradientBrush
                    double barWidth = Math.Max(4, Math.Min(260, month.Value / 100));
                    var bar = new Border
                    {
                        Width = barWidth,
                        Height = 26,
                        CornerRadius = new CornerRadius(4),
                        Margin = new Thickness(8, 0, 8, 0),
                        VerticalAlignment = System.Windows.VerticalAlignment.Center,
                        Background = new System.Windows.Media.LinearGradientBrush(
                            System.Windows.Media.Color.FromRgb(59, 130, 246),   // #3B82F6
                            System.Windows.Media.Color.FromRgb(96, 165, 250),   // #60A5FA
                            new System.Windows.Point(0, 0),
                            new System.Windows.Point(1, 0))
                    };

                    // Value label
                    var valueLabel = new TextBlock
                    {
                        Text = $"₹{month.Value:N0}",
                        FontSize = 11,
                        FontWeight = System.Windows.FontWeights.SemiBold,
                        Foreground = new System.Windows.Media.SolidColorBrush(
                            System.Windows.Media.Color.FromRgb(203, 213, 225)), // #CBD5E1
                        VerticalAlignment = System.Windows.VerticalAlignment.Center
                    };

                    barContainer.Children.Add(monthLabel);
                    barContainer.Children.Add(bar);
                    barContainer.Children.Add(valueLabel);
                    ChartSalesTrend.Children.Add(barContainer);
                }

                // ✅ Top customers from SQLite
                var topCustomers = DatabaseHelper.LoadCustomerSummaries()
                    .OrderByDescending(c => c.TotalSpent)
                    .Take(5)
                    .ToList();

                if (ListTopCustomers != null)
                    ListTopCustomers.ItemsSource = topCustomers;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadChartsData failed: {ex.Message}");
            }
        }

        // ── Recent Activity Feed ──────────────────────────────────────────
        private void LoadRecentActivity()
        {
            try
            {
                _activities.Clear();

                // ✅ Load recent activity (bills + imports) from SQLite
                var activityItems = DatabaseHelper.LoadRecentActivity(limit: 20);

                foreach (var item in activityItems)
                    _activities.Add(item);

                if (DgRecentActivity != null)
                {
                    DgRecentActivity.ItemsSource = null;
                    DgRecentActivity.ItemsSource = _activities;
                }

                System.Diagnostics.Debug.WriteLine($"[INFO] Loaded {_activities.Count} activity item(s) from SQLite");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadRecentActivity failed: {ex.Message}");
            }
        }

        // ── Event Handlers ────────────────────────────────────────────────

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            LoadDashboardData();
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is MainWindow main)
                main.NavigateToPage("settings");
        }

        private void KpiCard_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || sender is not Border card || card.Tag is not string pageKey) return;

            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is MainWindow main)
                main.NavigateToPage(pageKey);
        }

        private void BtnViewCustomers_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is MainWindow main)
                main.NavigateToPage("customers");
        }

        private void BtnViewBilling_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            if (Application.Current.Windows.OfType<MainWindow>().FirstOrDefault() is MainWindow main)
                main.NavigateToPage("billing");
        }

        private void BtnViewActivity_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not ActivityItem activity) return;

            if (activity.Data is Bill bill)
            {
                // Try full bill preview modal; fall back to text preview
                try
                {
                    var previewWindow = new BillPreviewWindow(bill)
                    {
                        Owner = Window.GetWindow(this)
                    };
                    previewWindow.ShowDialog();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[ERROR] Preview failed: {ex.Message}");
                    string billText = FormatBillForPreview(bill);
                    MessageBox.Show(billText, $"Bill: {bill.BillNumberFormatted}",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            else if (activity.Data is ImportHistory import)
            {
                MessageBox.Show(
                    $"📥 Import Details\n\n" +
                    $"File: {import.FileName}\n" +
                    $"Date: {import.ImportDate:dd MMM yyyy, hh:mm tt}\n" +
                    $"Items: {import.ItemCount:N0}\n" +
                    $"Supplier: {import.SupplierName} ({import.SupplierContact})",
                    "Import Details", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                // Activity loaded without full Data object (lightweight DB query path)
                MessageBox.Show(
                    $"{activity.Type}\n\n{activity.Description}\n\nAmount: {activity.Amount}\nTime: {activity.Timestamp:dd MMM yyyy, hh:mm tt}",
                    "Activity Details", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ── Helper: Format Bill for Preview (Fallback) ────────────────────
        private string FormatBillForPreview(Bill bill)
        {
            var settings = AppSettings.Load();
            var sb = new System.Text.StringBuilder();

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

            foreach (var item in bill.Items ?? Enumerable.Empty<BillItem>())
                sb.AppendLine($"{item.ProductName,-25} {item.Rate,8:N2} {item.Quantity,5} {item.Total,10:N2}");

            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"Subtotal: ₹ {bill.SubTotal:N2}");
            sb.AppendLine($"CGST:     ₹ {bill.TotalCGST:N2}");
            sb.AppendLine($"SGST:     ₹ {bill.TotalSGST:N2}");
            sb.AppendLine("───────────────────────────────────────");
            sb.AppendLine($"GRAND TOTAL: ₹ {bill.GrandTotal:N2}");
            sb.AppendLine("═══════════════════════════════════════");
            sb.AppendLine("        Thank you for your purchase!   ");

            return sb.ToString();
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

    // ── Activity Item Model ───────────────────────────────────────────────
    public class ActivityItem
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Amount { get; set; } = string.Empty;
        public string Customer { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public object? Data { get; set; }
    }
} 