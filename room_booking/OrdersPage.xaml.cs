#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace room_booking
{
    public class ActiveStayModel
    {
        public int AllocId { get; set; }
        public int GuestId { get; set; }
        public string GuestName { get; set; } = "";
        public string GuestInitial => GuestName.Length > 0 ? GuestName[0].ToString().ToUpper() : "?";
        public string Mobile { get; set; } = "";
        public string Aadhar { get; set; } = "";
        public string Address { get; set; } = "";
        public int Persons { get; set; } = 1;
        public string RoomNumber { get; set; } = "";
        public string RoomType { get; set; } = "";
        public int Floor { get; set; }
        public int RoomId { get; set; }
        public DateTime CheckIn { get; set; }
        public DateTime? CheckOutDate { get; set; }
        public decimal PricePerDay { get; set; }
        public decimal AdvancePaid { get; set; }

        public decimal ExtraPersonPrice { get; set; }

        public int DaysSoFar => Math.Max(1, (DateTime.Today - CheckIn).Days);
        public int Days => CheckOutDate.HasValue
            ? Math.Max(1, (CheckOutDate.Value - CheckIn).Days)
            : DaysSoFar;

        public decimal TotalAmount => Days * (PricePerDay + (Persons * ExtraPersonPrice));
        public decimal BalanceDue => TotalAmount - AdvancePaid;

        public string RoomLabel => $"Room {RoomNumber}";

        public string StayRange => CheckOutDate.HasValue
            ? $"{CheckIn:dd MMM} → {CheckOutDate.Value:dd MMM yy}"
            : $"{CheckIn:dd MMM yyyy}  ·  open";

        public string DaysLabel => CheckOutDate.HasValue
     ? $"{Days} night(s)  ·  ₹{PricePerDay:F0} + {Persons}×₹{ExtraPersonPrice:F0}  ·  Day {Math.Min(DaysSoFar, Days)} of {Days}"
     : $"{DaysSoFar} day(s) so far  ·  ₹{PricePerDay:F0} + {Persons}×₹{ExtraPersonPrice:F0}/night";

        public string TotalFormatted => $"₹{TotalAmount:F0}";
        public string AdvanceFormatted => AdvancePaid > 0 ? $"₹{AdvancePaid:F0}" : "—";
        public string BalanceFormatted => BalanceDue <= 0 ? "PAID ✓" : $"₹{BalanceDue:F0}";
        public string BalanceBg => BalanceDue <= 0 ? "#DCFCE7" : "#FFF7ED";
        public string BalanceFg => BalanceDue <= 0 ? "#16A34A" : "#EA580C";
    }

    public partial class OrdersPage : Page
    {
        private List<ActiveStayModel> _allActive = new();
        private List<ActiveStayModel> _filtered = new();
        private readonly DispatcherTimer _toastTimer = new();

        // ── Pagination ────────────────────────────────────────────────────
        private const int PageSize = 10;
        private int _currentPage = 1;
        private int TotalPages => _filtered.Count == 0
            ? 1
            : (int)Math.Ceiling(_filtered.Count / (double)PageSize);

        public OrdersPage()
        {
            InitializeComponent();
            _toastTimer.Interval = TimeSpan.FromSeconds(2.8);
            _toastTimer.Tick += (s, e) =>
            {
                ToastBar.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
            Loaded += (s, e) => LoadActive();
        }

        // ── Load from DB ──────────────────────────────────────────────────
        private void LoadActive()
        {
            try
            {
                _allActive = DbContext.Read(conn =>
                {
                    var list = new List<ActiveStayModel>();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT
                    ra.id,
                    g.id,
                    g.full_name,
                    g.mobile,
                    g.aadhar,
                    g.address,
                    g.persons,
g.price_per_day,
g.extra_person_price,
r.id,
                    r.room_number,
                    r.room_type,
                    r.floor,
                    ra.check_in,
                    ra.check_out,
                    ra.advance_paid
                FROM room_allocations ra
                JOIN guests g ON g.id = ra.guest_id
                JOIN rooms  r ON r.id = ra.room_id
                WHERE ra.status = 'active'
                ORDER BY ra.check_in ASC";

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        DateTime? checkOut = null;
                        if (!reader.IsDBNull(14) &&
                            DateTime.TryParse(reader.GetValue(14)?.ToString(), out var co))
                            checkOut = co;

                        list.Add(new ActiveStayModel
                        {
                            AllocId = reader.GetInt32(0),
                            GuestId = reader.GetInt32(1),
                            GuestName = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Mobile = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Aadhar = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Address = reader.IsDBNull(5) ? "" : reader.GetString(5),
                            Persons = reader.IsDBNull(6) ? 1 : reader.GetInt32(6),
                            PricePerDay = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                            ExtraPersonPrice = reader.IsDBNull(8) ? 0 : reader.GetDecimal(8),
                            RoomId = reader.GetInt32(9),
                            RoomNumber = reader.IsDBNull(10) ? "" : reader.GetString(10),
                            RoomType = reader.IsDBNull(11) ? "" : reader.GetString(11),
                            Floor = reader.IsDBNull(12) ? 0 : reader.GetInt32(12),
                            CheckIn = DateTime.TryParse(reader.GetValue(13)?.ToString(), out var ci)
                                                       ? ci : DateTime.Today,
                            CheckOutDate = checkOut,
                            AdvancePaid = reader.IsDBNull(15) ? 0 : reader.GetDecimal(15)
                        });
                    }
                    return list;
                });

                _currentPage = 1;
                ApplyFilters();
            }
            catch (Exception ex)
            {
                ShowToast($"Load error: {ex.Message}", false);
                OrdersGrid.ItemsSource = null;
                ChipActive.Text = "0";
                ChipPersons.Text = "0";
                ChipTotal.Text = "₹0";
                ChipBalance.Text = "₹0";
            }
        }

        // ── Apply search + floor filter ───────────────────────────────────
        private void ApplyFilters()
        {
            if (OrdersGrid == null) return;

            var query = TxtSearch?.Text.Trim().ToLower() ?? "";
            var filter = (CmbFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Floors";

            _filtered = _allActive.Where(a =>
            {
                bool floorOk = filter switch
                {
                    "Floor 1" => a.Floor == 1,
                    "Floor 2" => a.Floor == 2,
                    "Floor 3" => a.Floor == 3,
                    _ => true
                };

                bool searchOk = string.IsNullOrEmpty(query)
                    || a.GuestName.ToLower().Contains(query)
                    || a.RoomNumber.ToLower().Contains(query)
                    || a.Mobile.Contains(query);

                return floorOk && searchOk;
            }).ToList();

            // Summary chips always reflect the full filtered set
            ChipActive.Text = _filtered.Count.ToString();
            ChipPersons.Text = _filtered.Sum(a => a.Persons).ToString();
            ChipTotal.Text = $"₹{_filtered.Sum(a => a.TotalAmount):F0}";
            ChipBalance.Text = $"₹{_filtered.Sum(a => Math.Max(0, a.BalanceDue)):F0}";

            RenderCurrentPage();
        }

        // ── Pagination ────────────────────────────────────────────────────
        private void BtnPrev_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage > 1) { _currentPage--; RenderCurrentPage(); }
        }

        private void BtnNext_Click(object sender, RoutedEventArgs e)
        {
            if (_currentPage < TotalPages) { _currentPage++; RenderCurrentPage(); }
        }

        private void RenderCurrentPage()
        {
            var page = _filtered
                .Skip((_currentPage - 1) * PageSize)
                .Take(PageSize)
                .ToList();

            OrdersGrid.ItemsSource = page;

            TxtPageInfo.Text = $"Page {_currentPage} of {TotalPages}";
            TxtPageCount.Text = $"({_filtered.Count} total)";
            BtnPrev.IsEnabled = _currentPage > 1;
            BtnNext.IsEnabled = _currentPage < TotalPages;
        }

        // ── Events ────────────────────────────────────────────────────────
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentPage = 1;
            ApplyFilters();
        }

        private void CmbFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            _currentPage = 1;
            ApplyFilters();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            TxtSearch.Text = "";
            CmbFilter.SelectedIndex = 0;
            LoadActive();
            ShowToast("✅ Active guests refreshed.", true);
        }

        // ── Toast ─────────────────────────────────────────────────────────
        private void ShowToast(string msg, bool ok)
        {
            ToastText.Text = msg;
            ToastBar.Background = ok
                ? new SolidColorBrush(Color.FromRgb(13, 148, 136))
                : new SolidColorBrush(Color.FromRgb(239, 68, 68));
            ToastBar.Visibility = Visibility.Visible;
            _toastTimer.Stop();
            _toastTimer.Start();
        }
    }
} 