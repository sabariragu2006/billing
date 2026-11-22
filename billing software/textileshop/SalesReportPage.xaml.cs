#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Win32;
using Newtonsoft.Json;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    /// <summary>
    /// Sales report page with graphical and table views
    /// </summary>
    public partial class SalesReportPage : Page
    {
        private readonly ObservableCollection<SalesReportItem> _reportItems = new();
        private bool _isInitialized = false;
        private DateTime _startDate;
        private DateTime _endDate;
        private string _currentView = "graph";

        public SalesReportPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;

                // Set default date range (last 30 days)
                _endDate = DateTime.Today;
                _startDate = _endDate.AddDays(-30);
                UpdateDateRangeText();

                // Load initial data
                LoadReportData();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SalesReportPage constructor failed: {ex.Message}");
                MessageBox.Show($"Page failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Update Date Range Display ──────────────────────────────────────
        private void UpdateDateRangeText()
        {
            TxtDateRange.Text = $"Period: {_startDate:dd MMM yyyy} to {_endDate:dd MMM yyyy}";
        }

        // ── Load Report Data from Bills ────────────────────────────────────
        private void LoadReportData()
        {
            if (!_isInitialized) return;

            try
            {
                // Load bills for date range
                var bills = LoadBillsForDateRange();

                // Calculate KPIs
                UpdateKpiMetrics(bills);

                // Prepare report items for table view
                _reportItems.Clear();
                foreach (var bill in bills.OrderBy(b => b.BillDate))
                {
                    _reportItems.Add(new SalesReportItem
                    {
                        Date = bill.BillDate,
                        BillNumber = bill.BillNumberFormatted,
                        CustomerName = bill.CustomerName,
                        ItemsCount = bill.Items?.Sum(i => i.Quantity) ?? 0,
                        SubTotal = bill.SubTotal,
                        CGST = bill.TotalCGST,
                        SGST = bill.TotalSGST,
                        Total = bill.GrandTotal
                    });
                }

                // Load current view
                if (_currentView == "graph")
                    LoadGraphView(bills);
                else
                    LoadTableView();

                System.Diagnostics.Debug.WriteLine($"[INFO] Loaded {bills.Count} bill(s) for report");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadReportData failed: {ex.Message}");
            }
        }

        // ── Load Bills for Date Range ──────────────────────────────────────
        private List<Bill> LoadBillsForDateRange()
        {
            return DatabaseHelper.LoadBillsByDateRange(_startDate, _endDate);
        }

        // ── Update KPI Metrics ─────────────────────────────────────────────
        private void UpdateKpiMetrics(List<Bill> bills)
        {
            double totalSales = bills.Sum(b => b.GrandTotal);
            int totalBills = bills.Count;
            int itemsSold = bills.Sum(b => b.Items?.Sum(i => i.Quantity) ?? 0);
            int uniqueCustomers = bills.Select(b => b.CustomerName).Distinct().Count();

            KpiTotalSales.Text = $"₹ {totalSales:N2}";
            KpiTotalBills.Text = totalBills.ToString("N0");
            KpiItemsSold.Text = itemsSold.ToString("N0");
            KpiCustomers.Text = uniqueCustomers.ToString("N0");
        }

        // ── Load Graphical View with Bars + Dates Below ────────────────────
        private void LoadGraphView(List<Bill> bills)
        {
            ChartCanvas.Children.Clear();

            // Aggregate sales by date
            var dailySales = bills
                .GroupBy(b => b.BillDate.Date)
                .Select(g => new { Date = g.Key, Value = g.Sum(b => b.GrandTotal) })
                .OrderBy(d => d.Date)
                .ToList();

            if (!dailySales.Any())
            {
                var noDataText = new TextBlock
                {
                    Text = "No sales data for selected period",
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ChartCanvas.Children.Add(noDataText);
                Canvas.SetLeft(noDataText, 100);
                Canvas.SetTop(noDataText, 100);
                return;
            }

            // Chart dimensions
            double chartHeight = 200;
            double barWidth = 30;
            double barSpacing = 15;
            double maxValue = dailySales.Max(d => d.Value);
            if (maxValue == 0) maxValue = 1;

            for (int i = 0; i < dailySales.Count; i++)
            {
                var sale = dailySales[i];
                double barHeight = (sale.Value / maxValue) * chartHeight;
                double x = i * (barWidth + barSpacing) + 20;
                double y = chartHeight - barHeight;

                // ✅ Draw bar
                var bar = new Rectangle
                {
                    Width = barWidth,
                    Height = Math.Max(barHeight, 2), // Minimum height for visibility
                    Fill = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                    RadiusX = 3,
                    RadiusY = 3,
                    ToolTip = $"{sale.Date:dd MMM yyyy}\n₹{sale.Value:N2}"
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                ChartCanvas.Children.Add(bar);

                // ✅ Add date label BELOW the bar
                var dateLabel = new TextBlock
                {
                    Text = sale.Date.ToString("dd MMM", CultureInfo.InvariantCulture),
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                // Center label under bar
                Canvas.SetLeft(dateLabel, x + barWidth / 2 - 25);
                // Position below chart area (with small gap)
                Canvas.SetTop(dateLabel, chartHeight + 5);
                ChartCanvas.Children.Add(dateLabel);

                // ✅ Optional: Add value label ABOVE bar (only if bar is tall enough)
                if (barHeight > 30)
                {
                    var valueLabel = new TextBlock
                    {
                        Text = $"₹{sale.Value:N0}",
                        FontSize = 9,
                        Foreground = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    };
                    Canvas.SetLeft(valueLabel, x + barWidth / 2 - 20);
                    Canvas.SetTop(valueLabel, y - 18);
                    ChartCanvas.Children.Add(valueLabel);
                }
            }

            // ✅ Draw X-axis line
            var axisLine = new Line
            {
                X1 = 0,
                Y1 = chartHeight,
                X2 = dailySales.Count * (barWidth + barSpacing) + 40,
                Y2 = chartHeight,
                Stroke = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                StrokeThickness = 1
            };
            ChartCanvas.Children.Add(axisLine);

            // ✅ Draw Y-axis labels (optional)
            AddYAxisLabels(maxValue, chartHeight);
        }

        // ── Add Y-Axis Labels ──────────────────────────────────────────────
        private void AddYAxisLabels(double maxValue, double chartHeight)
        {
            // Add 3 labels: 0, 50%, 100%
            var labels = new[] { 0, maxValue * 0.5, maxValue };
            foreach (var value in labels)
            {
                var label = new TextBlock
                {
                    Text = $"₹{value:N0}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                };
                double y = chartHeight - (value / maxValue) * chartHeight;
                Canvas.SetLeft(label, -35);
                Canvas.SetTop(label, y - 5);
                ChartCanvas.Children.Add(label);

                // Add grid line
                var gridLine = new Line
                {
                    X1 = 0,
                    Y1 = y,
                    X2 = ChartCanvas.ActualWidth,
                    Y2 = y,
                    Stroke = new SolidColorBrush(Color.FromArgb(30, 226, 232, 240)),
                    StrokeThickness = 1,
                    StrokeDashArray = new DoubleCollection { 2, 2 }
                };
                ChartCanvas.Children.Add(gridLine);
            }
        }

        // ── Load Table View ────────────────────────────────────────────────
        private void LoadTableView()
        {
            DgSalesReport.ItemsSource = null;
            DgSalesReport.ItemsSource = _reportItems.OrderByDescending(r => r.Date).ToList();
        }

        // ── Event Handlers ─────────────────────────────────────────────────

        private void BtnDateRange_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not string tag) return;

            _endDate = DateTime.Today;
            _startDate = tag switch
            {
                "7" => _endDate.AddDays(-7),
                "30" => _endDate.AddDays(-30),
                "month" => new DateTime(_endDate.Year, _endDate.Month, 1),
                _ => _endDate.AddDays(-30)
            };

            UpdateDateRangeText();
            LoadReportData();
        }

        private void BtnViewToggle_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not string view) return;

            // Update button styles
            if (view == "graph")
            {
                BtnGraphView.Style = FindResource("ViewToggleButtonActive") as Style;
                BtnTableView.Style = FindResource("ViewToggleButton") as Style;
                PanelGraphView.Visibility = Visibility.Visible;
                PanelTableView.Visibility = Visibility.Collapsed;
                _currentView = "graph";
                LoadGraphView(LoadBillsForDateRange());
            }
            else
            {
                BtnTableView.Style = FindResource("ViewToggleButtonActive") as Style;
                BtnGraphView.Style = FindResource("ViewToggleButton") as Style;
                PanelTableView.Visibility = Visibility.Visible;
                PanelGraphView.Visibility = Visibility.Collapsed;
                _currentView = "table";
                LoadTableView();
            }
        }

        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || !_reportItems.Any())
            {
                MessageBox.Show("No data to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"sales_report_{DateTime.Now:yyyyMMdd}.csv",
                Title = "Export Sales Report to CSV"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Date,Bill No.,Customer,Items,Subtotal,CGST,SGST,Total");

                foreach (var item in _reportItems.OrderByDescending(r => r.Date))
                {
                    string customer = item.CustomerName?.Replace("\"", "\"\"") ?? "";
                    sb.AppendLine($"\"{item.Date:yyyy-MM-dd}\",\"{item.BillNumber}\",\"{customer}\",{item.ItemsCount},{item.SubTotal:N2},{item.CGST:N2},{item.SGST:N2},{item.Total:N2}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"✅ Exported {_reportItems.Count} record(s) to:\n{dlg.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;

            // Simple print: show message with option to export
            MessageBox.Show("🖨 Print Feature\n\nTo print this report:\n\n1. Click '📤 Export CSV' to save data\n2. Open the CSV file in Excel\n3. Use Excel's Print feature (Ctrl+P)\n\nOr use Windows Print Screen (PrtScn) to capture the screen.",
                "Print Instructions", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        // ── Keyboard Shortcuts ─────────────────────────────────────────────
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // F5 to refresh
            if (e.Key == System.Windows.Input.Key.F5 && _isInitialized)
            {
                LoadReportData();
                e.Handled = true;
            }
        }
    }

    // ── Sales Report Item Model for Table View ────────────────────────────
    public class SalesReportItem
    {
        public DateTime Date { get; set; }
        public string BillNumber { get; set; } = string.Empty;
        public string CustomerName { get; set; } = string.Empty;
        public int ItemsCount { get; set; }
        public double SubTotal { get; set; }
        public double CGST { get; set; }
        public double SGST { get; set; }
        public double Total { get; set; }
    }
} 