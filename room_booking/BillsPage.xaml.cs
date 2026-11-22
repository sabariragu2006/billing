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
    public class BillRowModel
    {
        public int BillId { get; set; }
        public string BillNoFormatted => $"#{BillId:D5}";
        public string GuestName { get; set; } = "";
        public string GuestInitial => GuestName.Length > 0 ? GuestName[0].ToString().ToUpper() : "?";
        public string IdType { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Aadhar { get; set; } = "";
        public string Address { get; set; } = "";
        public int Persons { get; set; } = 1;
        public int GuestId { get; set; }
        public string RoomNumber { get; set; } = "";
        public string RoomType { get; set; } = "";
        public int Floor { get; set; }
        public int RoomId { get; set; }
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }

        public int Days => Math.Max(1, (CheckOut.Date - CheckIn.Date).Days);

        public decimal PricePerDay { get; set; }

        // base room charges only (days × price_per_day), stored at checkout
        public decimal TotalAmount { get; set; }

        // The REAL advance paid at check-in. Never altered for display purposes.
        public decimal AdvancePaid { get; set; }

        // ── Stored directly from bills table ──────────────────────────────
        public decimal RoomServiceCharges { get; set; } = 0;
        public decimal ExtraPersonCharges { get; set; } = 0;
        public decimal ExtraPersonPrice { get; set; } = 0;

        // ── Stored from bills.balance_collected / bills.amount_returned ───
        // These are written at checkout — use them directly for display,
        // never recompute them live (the underlying total/advance numbers
        // could otherwise drift from what was actually settled at the time).
        public decimal BalanceCollectedStored { get; set; } = 0;
        public decimal AmountReturnedStored { get; set; } = 0;

        public decimal CgstRate => 2.5m;
        public decimal SgstRate => 2.5m;

        public decimal SubTotal => TotalAmount + ExtraPersonCharges + RoomServiceCharges;
        public decimal CgstAmount => Math.Round(SubTotal * CgstRate / 100, 2);
        public decimal SgstAmount => Math.Round(SubTotal * SgstRate / 100, 2);
        public decimal GrandTotal => SubTotal + CgstAmount + SgstAmount;

        // Kept for reference / sorting / reporting only — NOT used to decide
        // paid/unpaid status in the UI (see BalanceFormatted below).
        public decimal BalanceDue => GrandTotal - AdvancePaid;

        public string StayRange => $"{CheckIn:dd MMM yyyy}  →  {CheckOut:dd MMM yyyy}";
        public string DaysLabel => $"{Days} night(s)  ·  ₹{PricePerDay:F0}/night";
        public string TotalFormatted => $"₹{GrandTotal:F2}";
        public string AdvanceLabel => AdvancePaid > 0 ? $"₹{AdvancePaid:F0}" : "—";

        // Every row in `bills` exists only because Mark-as-Paid was clicked
        // (unpaid bills are deleted in CheckOut_Click before ever being
        // persisted). So every bill that reaches this page is, by
        // construction, fully paid — regardless of the advance/balance split.
        public string BalanceFormatted => "PAID ✓";
        public string BalanceBg => "#DCFCE7";
        public string BalanceFg => "#16A34A";
    }

    public partial class BillsPage : Page
    {
        private List<BillRowModel> _allBills = new();
        private List<BillRowModel> _filtered = new();
        private readonly DispatcherTimer _toastTimer = new();

        private const int PageSize = 10;
        private int _currentPage = 1;
        private int TotalPages => _filtered.Count == 0
            ? 1
            : (int)Math.Ceiling(_filtered.Count / (double)PageSize);

        public BillsPage()
        {
            InitializeComponent();
            _toastTimer.Interval = TimeSpan.FromSeconds(2.8);
            _toastTimer.Tick += (s, e) =>
            {
                ToastBar.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
            Loaded += (s, e) => LoadBills();
        }

        private void LoadBills()
        {
            try
            {
                bool tableExists = DbContext.Read(conn =>
                {
                    using var check = conn.CreateCommand();
                    check.CommandText =
                        "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='bills'";
                    return Convert.ToInt32(check.ExecuteScalar()) > 0;
                });

                if (!tableExists)
                {
                    BillsGrid.ItemsSource = null;
                    ChipTotal.Text = "0";
                    ChipRevenue.Text = "₹0";
                    return;
                }

                _allBills = DbContext.Read(conn =>
                {
                    var list = new List<BillRowModel>();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
    SELECT
        b.id,                                               -- 0
        g.full_name,                                        -- 1
        g.mobile,                                           -- 2
        g.aadhar,                                           -- 3
        g.address,                                          -- 4
        g.persons,                                          -- 5
        g.id_type,                                          -- 6
        g.id                    AS guest_id,                -- 7
        r.room_number,                                      -- 8
        r.room_type,                                        -- 9
        r.floor,                                            -- 10
        r.id                    AS room_id,                 -- 11
        b.check_in,                                         -- 12
        b.check_out,                                        -- 13
        b.price_per_day,                                    -- 14
        b.total_amount,                                     -- 15
        COALESCE(b.advance_paid, 0)            AS advance_paid,             -- 16
        COALESCE(b.room_service_charges, 0)    AS room_service_charges,     -- 17
        COALESCE(b.extra_person_charges, 0)    AS extra_person_charges,     -- 18
        COALESCE(g.extra_person_price, 0)      AS extra_person_price,       -- 19
        COALESCE(b.balance_collected, 0)       AS balance_collected,        -- 20
        COALESCE(b.amount_returned, 0)         AS amount_returned           -- 21
    FROM bills b
    JOIN guests g ON g.id = b.guest_id
    JOIN rooms  r ON r.id = b.room_id
    ORDER BY b.id DESC";

                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        list.Add(new BillRowModel
                        {
                            BillId = reader.GetInt32(0),
                            GuestName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                            Mobile = reader.IsDBNull(2) ? "" : reader.GetString(2),
                            Aadhar = reader.IsDBNull(3) ? "" : reader.GetString(3),
                            Address = reader.IsDBNull(4) ? "" : reader.GetString(4),
                            Persons = reader.IsDBNull(5) ? 1 : reader.GetInt32(5),
                            IdType = reader.IsDBNull(6) ? "" : reader.GetString(6),
                            GuestId = reader.GetInt32(7),
                            RoomNumber = reader.IsDBNull(8) ? "" : reader.GetString(8),
                            RoomType = reader.IsDBNull(9) ? "" : reader.GetString(9),
                            Floor = reader.IsDBNull(10) ? 0 : reader.GetInt32(10),
                            RoomId = reader.GetInt32(11),
                            CheckIn = DateTime.TryParse(reader.GetValue(12)?.ToString(), out var ci) ? ci : DateTime.Today,
                            CheckOut = DateTime.TryParse(reader.GetValue(13)?.ToString(), out var co) ? co : DateTime.Today,
                            PricePerDay = reader.IsDBNull(14) ? 0 : reader.GetDecimal(14),
                            TotalAmount = reader.IsDBNull(15) ? 0 : reader.GetDecimal(15),
                            AdvancePaid = reader.IsDBNull(16) ? 0 : reader.GetDecimal(16),
                            RoomServiceCharges = reader.IsDBNull(17) ? 0 : reader.GetDecimal(17),
                            ExtraPersonCharges = reader.IsDBNull(18) ? 0 : reader.GetDecimal(18),
                            ExtraPersonPrice = reader.IsDBNull(19) ? 0 : Convert.ToDecimal(reader.GetValue(19)),
                            BalanceCollectedStored = reader.IsDBNull(20) ? 0 : reader.GetDecimal(20),
                            AmountReturnedStored = reader.IsDBNull(21) ? 0 : reader.GetDecimal(21),
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
                BillsGrid.ItemsSource = null;
                ChipTotal.Text = "0";
                ChipRevenue.Text = "₹0";
            }
        }

        private void ApplyFilters()
        {
            if (BillsGrid == null) return;

            var query = TxtSearch?.Text.Trim().ToLower() ?? "";
            var filter = (CmbFilter?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "All Time";

            var now = DateTime.Today;
            var weekStart = now.AddDays(-(int)now.DayOfWeek);
            var monthStart = new DateTime(now.Year, now.Month, 1);

            _filtered = _allBills.Where(b =>
            {
                bool dateOk = filter switch
                {
                    "Today" => b.CheckOut.Date >= now,
                    "This Week" => b.CheckOut.Date >= weekStart,
                    "This Month" => b.CheckOut.Date >= monthStart,
                    _ => true
                };

                bool searchOk = string.IsNullOrEmpty(query)
                    || b.GuestName.ToLower().Contains(query)
                    || b.RoomNumber.ToLower().Contains(query)
                    || b.BillNoFormatted.ToLower().Contains(query)
                    || b.Mobile.Contains(query);

                return dateOk && searchOk;
            }).ToList();

            ChipTotal.Text = _filtered.Count.ToString();
            ChipRevenue.Text = $"₹{_filtered.Sum(b => b.GrandTotal):F0}";
            RenderCurrentPage();
        }

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

            BillsGrid.ItemsSource = page;
            TxtPageInfo.Text = $"Page {_currentPage} of {TotalPages}";
            TxtPageCount.Text = $"({_filtered.Count} total)";
            BtnPrev.IsEnabled = _currentPage > 1;
            BtnNext.IsEnabled = _currentPage < TotalPages;
        }

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
            LoadBills();
            ShowToast("✅ Bills refreshed.", true);
        }

        private void PrintBill_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not BillRowModel row) return;

            var billData = new BillData
            {
                BillId = row.BillId,
                RoomNumber = row.RoomNumber,
                RoomType = row.RoomType,
                Floor = row.Floor,
                GuestName = row.GuestName,
                IdType = row.IdType,
                Aadhar = row.Aadhar,
                Mobile = row.Mobile,
                Address = row.Address,
                Persons = row.Persons,
                CheckIn = row.CheckIn,
                CheckOut = row.CheckOut,
                Days = row.Days,
                PricePerDay = row.PricePerDay,
                TotalAmount = row.TotalAmount,
                RoomServiceCharges = row.RoomServiceCharges,
                ExtraPersonPrice = row.ExtraPersonPrice,
                ExtraPersonCharges = row.ExtraPersonCharges,

                // Real advance — exactly what the guest paid at check-in.
                AdvancePaid = row.AdvancePaid,

                // Use the values actually recorded at the moment of checkout
                // instead of recomputing live (avoids drift + avoids ever
                // showing a "balance due" on an already-closed bill).
                BalanceCollected = row.BalanceCollectedStored,
                AmountReturned = row.AmountReturnedStored,
            };

            // isHistorical = true: this is a reprint of an already-settled
            // bill, so the modal should present it as fully paid rather than
            // recomputing a live balance from GrandTotal - AdvancePaid.
            var modal = new BillPrintModal(billData, showMarkPaid: false, isHistorical: true)
            {
                Owner = Window.GetWindow(this)
            };
            modal.ShowDialog();
            LoadBills();
        }

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