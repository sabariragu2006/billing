#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;

namespace room_booking
{
    public partial class DashboardPage : Page
    {
        public DashboardPage()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadAll();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e) => LoadAll();

        // ── Master load ───────────────────────────────────────────────────
        private void LoadAll()
        {
            LoadRoomStats();
            LoadSalesReport();
            LoadRecentCheckIns();
        }

        // ── Room stats ────────────────────────────────────────────────────
        private void LoadRoomStats()
        {
            var stats = DbContext.Read(conn =>
            {
                int total = 0, occupied = 0;
                decimal todayRev = 0;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT COUNT(*) FROM rooms";
                    total = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(DISTINCT room_id)
                        FROM room_allocations
                        WHERE status = 'active'";
                    occupied = Convert.ToInt32(cmd.ExecuteScalar());
                }
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
        SELECT COALESCE(SUM(total_amount + COALESCE(room_service_charges,0) + COALESCE(extra_person_charges,0)), 0)
        FROM bills
        WHERE date(created_at) = date('now')";
                    todayRev = Convert.ToDecimal(cmd.ExecuteScalar());
                }
                return (total, occupied, todayRev);
            });

            int free = stats.total - stats.occupied;
            double pct = stats.total > 0 ? (double)stats.occupied / stats.total : 0;

            TxtTotalRooms.Text = stats.total.ToString();
            TxtFreeRooms.Text = free.ToString();
            TxtOccupiedRooms.Text = stats.occupied.ToString();
            TxtTodayRevenue.Text = $"₹{stats.todayRev:F0}";
            TxtOccupancyPct.Text = $"{pct * 100:F0}%";

            // Occupancy progress bar — needs actual track width
            // Use Dispatcher to read ActualWidth after layout
            Dispatcher.InvokeAsync(() =>
            {
                double trackW = ((Border)OccupancyBar.Parent).ActualWidth;
                OccupancyBar.Width = trackW * pct;
            }, System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // ── Day-wise sales ────────────────────────────────────────────────
        private void LoadSalesReport()
        {
            var rows = DbContext.Read(conn =>
            {
                var list = new List<(string Date, int CheckOuts, decimal Revenue)>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
    SELECT
        date(check_out)          AS day,
        COUNT(*)                 AS checkouts,
        SUM(total_amount + COALESCE(room_service_charges,0) + COALESCE(extra_person_charges,0)) AS revenue
    FROM bills
    WHERE date(check_out) >= date('now', '-6 days')
    GROUP BY day
    ORDER BY day ASC";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add((r.GetString(0), r.GetInt32(1), r.GetDecimal(2)));

                return list;
            });

            // Fill missing days with zero
            var filled = new List<(string Label, string Date, int CheckOuts, decimal Revenue)>();
            for (int i = 6; i >= 0; i--)
            {
                var d = DateTime.Today.AddDays(-i);
                var key = d.ToString("yyyy-MM-dd");
                var row = rows.FirstOrDefault(r => r.Date == key);
                filled.Add((d.ToString("ddd dd"), key, row.CheckOuts, row.Revenue));
            }

            // Draw bar chart
            ChartGrid.Children.Clear();
            decimal maxRev = filled.Max(r => r.Revenue);
            if (maxRev == 0) maxRev = 1;

            double chartH = 140;

            // We'll use a UniformGrid of columns
            var chartCols = new UniformGrid { Columns = 7, VerticalAlignment = VerticalAlignment.Bottom };
            ChartGrid.Children.Add(chartCols);

            foreach (var day in filled)
            {
                double barPct = (double)(day.Revenue / maxRev);
                double barH = Math.Max(4, barPct * chartH);
                bool isToday = day.Date == DateTime.Today.ToString("yyyy-MM-dd");

                var col = new StackPanel
                {
                    VerticalAlignment = VerticalAlignment.Bottom,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(3, 0, 3, 0)
                };

                // Amount label on top of bar
                col.Children.Add(new TextBlock
                {
                    Text = day.Revenue > 0 ? $"₹{day.Revenue:F0}" : "",
                    FontSize = 9,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 0, 0, 3)
                });

                // Bar
                col.Children.Add(new Border
                {
                    Height = barH,
                    Background = new SolidColorBrush(isToday
                        ? Color.FromRgb(234, 88, 12)
                        : Color.FromRgb(253, 186, 116)),
                    CornerRadius = new CornerRadius(4, 4, 0, 0)
                });

                // Day label
                col.Children.Add(new TextBlock
                {
                    Text = day.Label,
                    FontSize = 9,
                    FontWeight = isToday ? FontWeights.Bold : FontWeights.Normal,
                    Foreground = new SolidColorBrush(isToday
                        ? Color.FromRgb(234, 88, 12)
                        : Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 4, 0, 0)
                });

                chartCols.Children.Add(col);
            }

            // Table below chart
            SalesPanel.Children.Clear();

            // Table header
            SalesPanel.Children.Add(BuildSalesRow("Date", "Check-Outs", "Revenue", isHeader: true));

            foreach (var day in filled)
            {
                bool isToday = day.Date == DateTime.Today.ToString("yyyy-MM-dd");
                SalesPanel.Children.Add(BuildSalesRow(
                    isToday ? $"{day.Label} (Today)" : day.Label,
                    day.CheckOuts.ToString(),
                    day.Revenue > 0 ? $"₹{day.Revenue:F0}" : "—",
                    isHeader: false,
                    highlight: isToday
                ));
            }

            // 7-day totals row
            SalesPanel.Children.Add(BuildSalesRow(
                "7-Day Total",
                filled.Sum(r => r.CheckOuts).ToString(),
                $"₹{filled.Sum(r => r.Revenue):F0}",
                isHeader: false,
                isTotal: true
            ));
        }

        private static Border BuildSalesRow(
            string date, string checkouts, string revenue,
            bool isHeader = false, bool highlight = false, bool isTotal = false)
        {
            Color bg = isTotal ? Color.FromRgb(15, 23, 42)
                     : isHeader ? Color.FromRgb(248, 250, 252)
                     : highlight ? Color.FromRgb(255, 247, 237)
                     : Colors.White;

            Color fg = isTotal ? Colors.White
                     : isHeader ? Color.FromRgb(100, 116, 139)
                                : Color.FromRgb(15, 23, 42);

            var row = new Border
            {
                Background = new SolidColorBrush(bg),
                BorderBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 10, 0, 10)
            };

            var g = new Grid { Margin = new Thickness(4, 0, 4, 0) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });

            TextBlock MakeTb(string text, int col, bool right = false) => new TextBlock
            {
                Text = text,
                FontSize = isHeader ? 10 : 12,
                FontWeight = (isHeader || isTotal) ? FontWeights.Bold : FontWeights.Normal,
                Foreground = new SolidColorBrush(col == 2
                    ? (isTotal ? Colors.White : Color.FromRgb(234, 88, 12))
                    : fg),
                HorizontalAlignment = right ? HorizontalAlignment.Right : HorizontalAlignment.Left,
                Margin = new Thickness(right ? 0 : 0, 0, right ? 4 : 0, 0)
            };

            var t0 = MakeTb(date, 0);
            var t1 = MakeTb(checkouts, 1, right: true);
            var t2 = MakeTb(revenue, 2, right: true);

            Grid.SetColumn(t0, 0); Grid.SetColumn(t1, 1); Grid.SetColumn(t2, 2);
            g.Children.Add(t0); g.Children.Add(t1); g.Children.Add(t2);
            row.Child = g;
            return row;
        }

        // ── Recent check-ins ──────────────────────────────────────────────
        private void LoadRecentCheckIns()
        {
            var recent = DbContext.Read(conn =>
            {
                var list = new List<(string Name, string Room, string CheckIn, decimal Rate)>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT
                        g.full_name,
                        r.room_number,
                        ra.check_in,
                        g.price_per_day
                    FROM room_allocations ra
                    JOIN guests g ON g.id = ra.guest_id
                    JOIN rooms  r ON r.id = ra.room_id
                    WHERE ra.status = 'active'
                    ORDER BY ra.id DESC
                    LIMIT 8";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                    list.Add((
                        reader.GetString(0),
                        reader.GetString(1),
                        reader.IsDBNull(2) ? "" : reader.GetString(2),
                        reader.IsDBNull(3) ? 0 : reader.GetDecimal(3)
                    ));
                return list;
            });

            RecentPanel.Children.Clear();

            if (recent.Count == 0)
            {
                RecentPanel.Children.Add(new TextBlock
                {
                    Text = "No active guests right now.",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 24, 0, 24)
                });
                return;
            }

            foreach (var (name, room, checkIn, rate) in recent)
            {
                int days = 0;
                if (DateTime.TryParse(checkIn, out var ci))
                    days = Math.Max(1, (DateTime.Today - ci).Days);

                var row = new Border
                {
                    BorderBrush = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                    BorderThickness = new Thickness(0, 0, 0, 1),
                    Padding = new Thickness(20, 12, 20, 12)
                };
                row.MouseEnter += (s, e) => row.Background = new SolidColorBrush(Color.FromRgb(255, 247, 237));
                row.MouseLeave += (s, e) => row.Background = Brushes.White;

                var g = new Grid();
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var av = new Border
                {
                    Width = 36,
                    Height = 36,
                    CornerRadius = new CornerRadius(18),
                    Background = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
                    Margin = new Thickness(0, 0, 12, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                av.Child = new TextBlock
                {
                    Text = name.Length > 0 ? name[0].ToString().ToUpper() : "?",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                Grid.SetColumn(av, 0);

                var info = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                info.Children.Add(new TextBlock
                {
                    Text = name,
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                });
                info.Children.Add(new TextBlock
                {
                    Text = $"Room {room}  ·  {days} day(s) staying",
                    FontSize = 11,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    Margin = new Thickness(0, 2, 0, 0)
                });
                Grid.SetColumn(info, 1);

                var amt = new StackPanel { VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
                amt.Children.Add(new TextBlock
                {
                    Text = $"₹{days * rate:F0}",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12)),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                amt.Children.Add(new TextBlock
                {
                    Text = $"₹{rate:F0}/day",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
                Grid.SetColumn(amt, 2);

                g.Children.Add(av); g.Children.Add(info); g.Children.Add(amt);
                row.Child = g;
                RecentPanel.Children.Add(row);
            }
        }
    }
}