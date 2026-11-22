#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Microsoft.Data.Sqlite;

namespace room_booking
{
    public class BillRow
    {
        public int BillId { get; set; }
        public string GuestName { get; set; } = "";
        public string RoomNumber { get; set; } = "";
        public string CheckIn { get; set; } = "";
        public string CheckOut { get; set; } = "";
        public int Days { get; set; }
        public double PricePerDay { get; set; }
        public double TotalAmount { get; set; }
    }

    public class LegendItem
    {
        public string Color { get; set; } = "";
        public string Label { get; set; } = "";
        public string Percent { get; set; } = "";
    }

    public class RevenuePoint
    {
        public string Label { get; set; } = "";
        public double Revenue { get; set; }
    }

    public class PhaseSummary
    {
        public int ReservationCount { get; set; }
        public double ReservationRevenue { get; set; }
        public int ActiveCount { get; set; }
        public double ActiveRevenue { get; set; }
        public int CheckedOutCount { get; set; }
        public double CheckedOutRevenue { get; set; }
        public int BilledCount { get; set; }
        public double BilledRevenue { get; set; }
    }

    public class RoomTypeSlice
    {
        public string RoomType { get; set; } = "";
        public double Revenue { get; set; }
    }

    public partial class SalesReportPage : Page
    {
        private static readonly string[] PieColors =
            { "#2563EB", "#10B981", "#F59E0B", "#A78BFA", "#F43F5E", "#06B6D4" };

        // Reusable grand-total expression — keeps all queries consistent
        private const string GrandTotalExpr =
            "(total_amount + COALESCE(room_service_charges,0) + COALESCE(extra_person_charges,0))";

        private DateTime _from;
        private DateTime _to;
        private ObservableCollection<BillRow> _billRows = new();

        public SalesReportPage()
        {
            InitializeComponent();

            Loaded += (_, _) =>
            {
                try
                {
                    RbToday.Checked += Period_Changed;
                    RbWeek.Checked += Period_Changed;
                    RbMonth.Checked += Period_Changed;
                    RbQuarter.Checked += Period_Changed;
                    RbYear.Checked += Period_Changed;
                    RbCustom.Checked += Period_Changed;

                    DpFrom.SelectedDateChanged += DateRange_Changed;
                    DpTo.SelectedDateChanged += DateRange_Changed;

                    SetPeriodMonth();
                    Refresh();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Loaded error: {ex.Message}\n\n{ex.StackTrace}",
                        "Sales Report Error");
                }
            };
        }

        // ── Period helpers ───────────────────────────────────────────────────────

        private void SetPeriodMonth()
        {
            var today = DateTime.Today;
            _from = new DateTime(today.Year, today.Month, 1);
            _to = today;
            DpFrom.SelectedDate = _from;
            DpTo.SelectedDate = _to;
        }

        private void Period_Changed(object sender, RoutedEventArgs e)
        {
            var today = DateTime.Today;

            if (RbToday?.IsChecked == true)
            {
                _from = today; _to = today;
            }
            else if (RbWeek?.IsChecked == true)
            {
                int diff = (int)today.DayOfWeek;
                _from = today.AddDays(-diff); _to = today;
            }
            else if (RbMonth?.IsChecked == true)
            {
                _from = new DateTime(today.Year, today.Month, 1); _to = today;
            }
            else if (RbQuarter?.IsChecked == true)
            {
                int qStart = (today.Month - 1) / 3 * 3 + 1;
                _from = new DateTime(today.Year, qStart, 1); _to = today;
            }
            else if (RbYear?.IsChecked == true)
            {
                _from = new DateTime(today.Year, 1, 1); _to = today;
            }
            else return;

            DpFrom.SelectedDate = _from;
            DpTo.SelectedDate = _to;
            Refresh();
        }

        private void DateRange_Changed(object sender, SelectionChangedEventArgs e)
        {
            if (RbCustom?.IsChecked != true) return;
            if (DpFrom.SelectedDate.HasValue) _from = DpFrom.SelectedDate.Value;
            if (DpTo.SelectedDate.HasValue) _to = DpTo.SelectedDate.Value;
            if (_from <= _to) Refresh();
        }

        // ── Main refresh ─────────────────────────────────────────────────────────

        private void Refresh()
        {
            try
            {
                TxtSubtitle.Text =
                    $"Revenue and occupancy analytics  ·  {_from:dd MMM yyyy} – {_to:dd MMM yyyy}";
                TxtChartPeriodLabel.Text = $"{_from:MMM d} – {_to:MMM d, yyyy}";

                var bills = LoadBills();
                var phase = LoadPhase();
                var revPoints = LoadRevenueTrend();
                var roomSlices = LoadRoomTypeRevenue();

                // ── KPI cards ────────────────────────────────────────────────────
                double totalRev = bills.Sum(b => b.TotalAmount);
                TxtTotalRevenue.Text = $"₹{totalRev:N0}";
                TxtTotalBookings.Text = bills.Count.ToString();

                double avgStay = bills.Count > 0 ? bills.Average(b => b.Days) : 0;
                TxtAvgStay.Text = avgStay.ToString("F1");

                int totalRooms = GetTotalRooms();
                int days = Math.Max(1, (_to - _from).Days + 1);
                double occupancy = totalRooms > 0
                    ? bills.Sum(b => b.Days) / (double)(totalRooms * days) * 100
                    : 0;
                TxtOccupancy.Text = $"{Math.Min(occupancy, 100):F1}%";

                TxtRevenueDelta.Text = "";
                TxtBookingsDelta.Text = "";
                TxtAvgStayDelta.Text = "";
                TxtOccupancyDelta.Text = "";

                _billRows = new ObservableCollection<BillRow>(bills);

                DrawBarChart(revPoints);
                DrawPieChart(roomSlices);
                DrawPhase(phase);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Refresh error: {ex.Message}\n\n{ex.StackTrace}",
                    "Sales Report Error");
            }
        }

        // ── Data loaders ─────────────────────────────────────────────────────────

        private List<BillRow> LoadBills()
        {
            return DbContext.Read(conn =>
            {
                var list = new List<BillRow>();
                using var cmd = conn.CreateCommand();

                // Grand total = base + room service + extra person charges
                cmd.CommandText = $@"
                    SELECT b.id,
                           COALESCE(r.room_number, '—'),
                           b.check_in,
                           b.check_out,
                           b.days,
                           b.price_per_day,
                           (b.total_amount
                            + COALESCE(b.room_service_charges, 0)
                            + COALESCE(b.extra_person_charges,  0)) AS grand_total
                    FROM bills b
                    LEFT JOIN rooms r ON r.id = b.room_id
                    WHERE DATE(b.check_out) >= $from
                      AND DATE(b.check_out) <= $to
                      AND b.check_out IS NOT NULL
                      AND b.check_out != ''
                    ORDER BY b.check_out DESC";

                cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    list.Add(new BillRow
                    {
                        BillId = rdr.GetInt32(0),
                        GuestName = "—",
                        RoomNumber = rdr.GetString(1),
                        CheckIn = FormatDate(rdr.IsDBNull(2) ? "" : rdr.GetString(2)),
                        CheckOut = FormatDate(rdr.IsDBNull(3) ? "" : rdr.GetString(3)),
                        Days = rdr.IsDBNull(4) ? 0 : rdr.GetInt32(4),
                        PricePerDay = rdr.IsDBNull(5) ? 0 : rdr.GetDouble(5),
                        TotalAmount = rdr.IsDBNull(6) ? 0 : rdr.GetDouble(6), // grand total
                    });
                }
                return list;
            });
        }

        private PhaseSummary LoadPhase()
        {
            return DbContext.Read(conn =>
            {
                var ps = new PhaseSummary();

                // 1. Reservations — advance collected at reservation time
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*), COALESCE(SUM(advance_paid), 0.0)
                        FROM reservations
                        WHERE status = 'reserved'
                          AND DATE(created_at) >= $from
                          AND DATE(created_at) <= $to";
                    cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        ps.ReservationCount = r.GetInt32(0);
                        ps.ReservationRevenue = r.GetDouble(1);
                    }
                }

                // 2. Active stays — advance actually collected so far
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*), COALESCE(SUM(ra.advance_paid), 0.0)
                        FROM room_allocations ra
                        WHERE ra.status = 'active'
                          AND DATE(ra.check_in) >= $from
                          AND DATE(ra.check_in) <= $to";
                    cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        ps.ActiveCount = r.GetInt32(0);
                        ps.ActiveRevenue = r.GetDouble(1);
                    }
                }

                // 3. Checked out — full grand total (base + service + extra persons)
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*),
                               COALESCE(SUM(
                                   b.total_amount
                                   + COALESCE(b.room_service_charges, 0)
                                   + COALESCE(b.extra_person_charges,  0)
                               ), 0.0)
                        FROM bills b
                        WHERE b.check_out IS NOT NULL
                          AND b.check_out != ''
                          AND DATE(b.check_out) >= $from
                          AND DATE(b.check_out) <= $to";
                    cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        ps.CheckedOutCount = r.GetInt32(0);
                        ps.CheckedOutRevenue = r.GetDouble(1);
                    }
                }

                // 4. Billed — same grand total formula, scoped by bill creation date
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        SELECT COUNT(*),
                               COALESCE(SUM(
                                   total_amount
                                   + COALESCE(room_service_charges, 0)
                                   + COALESCE(extra_person_charges,  0)
                               ), 0.0)
                        FROM bills
                        WHERE DATE(created_at) >= $from
                          AND DATE(created_at) <= $to";
                    cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));
                    using var r = cmd.ExecuteReader();
                    if (r.Read())
                    {
                        ps.BilledCount = r.GetInt32(0);
                        ps.BilledRevenue = r.GetDouble(1);
                    }
                }

                return ps;
            });
        }

        private List<RevenuePoint> LoadRevenueTrend()
        {
            return DbContext.Read(conn =>
            {
                var list = new List<RevenuePoint>();
                int span = (_to - _from).Days;

                string groupFormat;
                if (span <= 14) groupFormat = "%Y-%m-%d";
                else if (span <= 90) groupFormat = "%Y-%W";
                else groupFormat = "%Y-%m";

                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
                    SELECT STRFTIME('{groupFormat}', created_at) AS period,
                           COALESCE(SUM(
                               total_amount
                               + COALESCE(room_service_charges, 0)
                               + COALESCE(extra_person_charges,  0)
                           ), 0.0)
                    FROM bills
                    WHERE DATE(created_at) >= $from
                      AND DATE(created_at) <= $to
                    GROUP BY period
                    ORDER BY period";

                cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                {
                    string raw = rdr.GetString(0);
                    string label = raw;

                    if (groupFormat == "%Y-%m-%d" && DateTime.TryParse(raw, out var dt))
                        label = dt.Day.ToString();
                    else if (groupFormat == "%Y-%m" && raw.Length == 7)
                        label = DateTime.ParseExact(raw + "-01", "yyyy-MM-dd", null)
                                        .ToString("MMM");

                    list.Add(new RevenuePoint { Label = label, Revenue = rdr.GetDouble(1) });
                }
                return list;
            });
        }

        private List<RoomTypeSlice> LoadRoomTypeRevenue()
        {
            return DbContext.Read(conn =>
            {
                var list = new List<RoomTypeSlice>();
                using var cmd = conn.CreateCommand();

                cmd.CommandText = @"
                    SELECT COALESCE(r.room_type, 'Standard') AS rt,
                           COALESCE(SUM(
                               b.total_amount
                               + COALESCE(b.room_service_charges, 0)
                               + COALESCE(b.extra_person_charges,  0)
                           ), 0.0) AS grand
                    FROM bills b
                    LEFT JOIN rooms r ON r.id = b.room_id
                    WHERE DATE(b.created_at) >= $from
                      AND DATE(b.created_at) <= $to
                    GROUP BY rt
                    ORDER BY grand DESC";

                cmd.Parameters.AddWithValue("$from", _from.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$to", _to.ToString("yyyy-MM-dd"));

                using var rdr = cmd.ExecuteReader();
                while (rdr.Read())
                    list.Add(new RoomTypeSlice
                    {
                        RoomType = rdr.GetString(0),
                        Revenue = rdr.GetDouble(1)
                    });
                return list;
            });
        }

        private int GetTotalRooms()
        {
            return DbContext.Read(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT COUNT(*) FROM rooms";
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            });
        }

        // ── Bar Chart ────────────────────────────────────────────────────────────

        private void DrawBarChart(List<RevenuePoint> points)
        {
            CanvasRevenue.Children.Clear();
            IcBarLabels.Items.Clear();

            double canvasW = CanvasRevenue.ActualWidth > 0 ? CanvasRevenue.ActualWidth : 500;
            double canvasH = CanvasRevenue.ActualHeight > 0 ? CanvasRevenue.ActualHeight : 220;

            if (!points.Any()) return;

            double maxRev = points.Max(p => p.Revenue);
            if (maxRev == 0) maxRev = 1;

            // Grid lines + Y-axis labels
            for (int i = 0; i <= 4; i++)
            {
                double y = canvasH - canvasH * i / 4.0 - 10;
                CanvasRevenue.Children.Add(new Line
                {
                    X1 = 0,
                    X2 = canvasW,
                    Y1 = y,
                    Y2 = y,
                    Stroke = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#F3F4F6")),
                    StrokeThickness = 1
                });

                var lbl = new TextBlock
                {
                    Text = $"₹{maxRev * i / 4:N0}",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#D1D5DB"))
                };
                Canvas.SetLeft(lbl, 2);
                Canvas.SetTop(lbl, y - 13);
                CanvasRevenue.Children.Add(lbl);
            }

            int n = points.Count;
            double barW = Math.Max(8, (canvasW - 40) / n * 0.6);
            double gapW = (canvasW - 40) / n;
            double startX = 40;

            for (int i = 0; i < n; i++)
            {
                double barH = points[i].Revenue / maxRev * (canvasH - 20);
                double x = startX + i * gapW + (gapW - barW) / 2;
                double y = canvasH - barH - 10;

                var bar = new Rectangle
                {
                    Width = barW,
                    Height = Math.Max(2, barH),
                    Fill = new LinearGradientBrush(
                        (Color)ColorConverter.ConvertFromString("#3B82F6"),
                        (Color)ColorConverter.ConvertFromString("#2563EB"),
                        new Point(0, 0), new Point(0, 1)),
                    RadiusX = 3,
                    RadiusY = 3
                };
                Canvas.SetLeft(bar, x);
                Canvas.SetTop(bar, y);
                CanvasRevenue.Children.Add(bar);

                var val = new TextBlock
                {
                    Text = $"₹{points[i].Revenue:N0}",
                    FontSize = 9,
                    Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#6B7280"))
                };
                Canvas.SetLeft(val, x - 4);
                Canvas.SetTop(val, Math.Max(0, y - 14));
                CanvasRevenue.Children.Add(val);

                IcBarLabels.Items.Add(points[i].Label);
            }
        }

        // ── Pie / Donut Chart ────────────────────────────────────────────────────

        private void DrawPieChart(List<RoomTypeSlice> slices)
        {
            CanvasPie.Children.Clear();
            IcLegend.Items.Clear();

            double cW = CanvasPie.ActualWidth > 0 ? CanvasPie.ActualWidth : 200;
            double cH = CanvasPie.ActualHeight > 0 ? CanvasPie.ActualHeight : 180;
            double cx = cW / 2, cy = cH / 2;
            double r = Math.Min(cx, cy) - 6;
            double innerR = r * 0.5;

            if (!slices.Any() || slices.Sum(s => s.Revenue) == 0)
            {
                var empty = new TextBlock
                {
                    Text = "No data",
                    Foreground = new SolidColorBrush(
                        (Color)ColorConverter.ConvertFromString("#D1D5DB")),
                    FontSize = 13
                };
                Canvas.SetLeft(empty, cx - 25);
                Canvas.SetTop(empty, cy - 10);
                CanvasPie.Children.Add(empty);
                return;
            }

            double total = slices.Sum(s => s.Revenue);
            double startAng = -90.0;

            for (int i = 0; i < slices.Count; i++)
            {
                double pct = slices[i].Revenue / total;
                double sweep = pct * 360.0;
                string color = PieColors[i % PieColors.Length];

                if (sweep > 0.5)
                    CanvasPie.Children.Add(
                        CreateArcPath(cx, cy, r, innerR, startAng, sweep, color));

                startAng += sweep;

                IcLegend.Items.Add(new LegendItem
                {
                    Color = color,
                    Label = slices[i].RoomType,
                    Percent = $"{pct:P0}"
                });
            }

            // Center labels
            var centerLbl = new TextBlock
            {
                Text = "Total",
                FontSize = 11,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#9CA3AF")),
                TextAlignment = TextAlignment.Center,
                Width = 50
            };
            Canvas.SetLeft(centerLbl, cx - 25);
            Canvas.SetTop(centerLbl, cy - 14);
            CanvasPie.Children.Add(centerLbl);

            var centerVal = new TextBlock
            {
                Text = $"₹{total:N0}",
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(
                    (Color)ColorConverter.ConvertFromString("#374151")),
                TextAlignment = TextAlignment.Center,
                Width = 60
            };
            Canvas.SetLeft(centerVal, cx - 30);
            Canvas.SetTop(centerVal, cy);
            CanvasPie.Children.Add(centerVal);
        }

        private static System.Windows.Shapes.Path CreateArcPath(
            double cx, double cy, double r, double innerR,
            double startDeg, double sweepDeg, string hexColor)
        {
            double startRad = startDeg * Math.PI / 180;
            double endRad = (startDeg + sweepDeg) * Math.PI / 180;

            var p1 = new Point(cx + r * Math.Cos(startRad), cy + r * Math.Sin(startRad));
            var p2 = new Point(cx + r * Math.Cos(endRad), cy + r * Math.Sin(endRad));
            var p3 = new Point(cx + innerR * Math.Cos(endRad), cy + innerR * Math.Sin(endRad));
            var p4 = new Point(cx + innerR * Math.Cos(startRad), cy + innerR * Math.Sin(startRad));

            bool large = sweepDeg > 180;
            var geo = new PathGeometry();
            var fig = new PathFigure { StartPoint = p1, IsClosed = true };
            fig.Segments.Add(new ArcSegment(p2, new Size(r, r), 0, large, SweepDirection.Clockwise, true));
            fig.Segments.Add(new LineSegment(p3, true));
            fig.Segments.Add(new ArcSegment(p4, new Size(innerR, innerR), 0, large, SweepDirection.Counterclockwise, true));
            geo.Figures.Add(fig);

            return new System.Windows.Shapes.Path
            {
                Data = geo,
                Fill = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor)),
                Stroke = new SolidColorBrush(Colors.White),
                StrokeThickness = 1.5
            };
        }

        // ── Phase Funnel ─────────────────────────────────────────────────────────

        private void DrawPhase(PhaseSummary ps)
        {
            TxtPhaseResCount.Text = $"{ps.ReservationCount} bookings";
            TxtPhaseResRev.Text = $"₹{ps.ReservationRevenue:N0}";
            TxtPhaseActiveCount.Text = $"{ps.ActiveCount} guests";
            TxtPhaseActiveRev.Text = $"₹{ps.ActiveRevenue:N0}";
            TxtPhaseCheckedOutCount.Text = $"{ps.CheckedOutCount} stays";
            TxtPhaseCheckedOutRev.Text = $"₹{ps.CheckedOutRevenue:N0}";
            TxtPhaseBilledCount.Text = $"{ps.BilledCount} bills";
            TxtPhaseBilledRev.Text = $"₹{ps.BilledRevenue:N0}";

            double maxRev = new[]
            {
                ps.ReservationRevenue, ps.ActiveRevenue,
                ps.CheckedOutRevenue,  ps.BilledRevenue
            }.Max();

            if (maxRev <= 0) maxRev = 1;

            GridPhases.SizeChanged -= GridPhases_SizeChanged;
            GridPhases.Tag = (ps, maxRev);
            GridPhases.SizeChanged += GridPhases_SizeChanged;
            ApplyPhaseBars(ps, maxRev);
        }

        private void GridPhases_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (GridPhases.Tag is (PhaseSummary ps, double maxRev))
                ApplyPhaseBars(ps, maxRev);
        }

        private void ApplyPhaseBars(PhaseSummary ps, double maxRev)
        {
            double avail = Math.Max(50, GridPhases.ActualWidth - 130 - 90 - 100 - 24);
            BarReservations.Width = ps.ReservationRevenue / maxRev * avail;
            BarActive.Width = ps.ActiveRevenue / maxRev * avail;
            BarCheckedOut.Width = ps.CheckedOutRevenue / maxRev * avail;
            BarBilled.Width = ps.BilledRevenue / maxRev * avail;
        }

        // ── CSV Export ───────────────────────────────────────────────────────────

        private void ExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                FileName = $"SalesReport_{_from:yyyyMMdd}_{_to:yyyyMMdd}.csv",
                DefaultExt = ".csv",
                Filter = "CSV Files (*.csv)|*.csv"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                using var sw = new StreamWriter(dlg.FileName);
                sw.WriteLine("Bill ID,Guest Name,Room,Check-In,Check-Out,Days,Rate/Day,Grand Total");
                foreach (var row in _billRows)
                    sw.WriteLine($"{row.BillId},{Q(row.GuestName)},{Q(row.RoomNumber)}," +
                                 $"{row.CheckIn},{row.CheckOut},{row.Days}," +
                                 $"{row.PricePerDay:F2},{row.TotalAmount:F2}");
                sw.WriteLine();
                sw.WriteLine($"Grand Total,,,,,,," +
                             $"{_billRows.Sum(r => r.TotalAmount):F2}");

                MessageBox.Show("Report exported successfully.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────────────

        private static string Q(string s) =>
            s.Contains(',') || s.Contains('"')
                ? $"\"{s.Replace("\"", "\"\"")}\""
                : s;

        private static string FormatDate(string iso)
        {
            if (DateTime.TryParse(iso, out var dt))
                return dt.ToString("dd MMM yyyy");
            return iso;
        }
    }
}