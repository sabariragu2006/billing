#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace room_booking
{
    public class ReservationModel
    {
        public int Id { get; set; }
        public int RoomId { get; set; }
        public string RoomNumber { get; set; } = "";
        public string RoomType { get; set; } = "";
        public string GuestName { get; set; } = "";
        public string IdType { get; set; } = "";
        public string Aadhar { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Address { get; set; } = "";
        public int Persons { get; set; } = 1;
        public decimal ExtraPersonPrice { get; set; } = 0;   // ← ADD THIS
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }
        public decimal PricePerDay { get; set; }
        public decimal AdvancePaid { get; set; }
        public string Status { get; set; } = "reserved";
        public int Days => Math.Max(1, (CheckOut - CheckIn).Days);
        // ← FIX: base + (extraPersons × extraPersonPrice) per day
        public decimal TotalAmount => Days * (PricePerDay + (Persons * ExtraPersonPrice));
        public decimal BalanceDue => TotalAmount - AdvancePaid;
        public bool IsToday => CheckIn.Date == DateTime.Today;
        public bool IsUpcoming => CheckIn.Date >= DateTime.Today && Status == "reserved";
    }
    public partial class ReservationPage : Page
    {
        private List<ReservationModel> _reservations = new();
        private List<RoomModel> _availableRooms = new();
        private ReservationModel? _activeReservation;
        private readonly DispatcherTimer _toastTimer = new();

        // ── Same room definitions as RoomAllocationPage ───────────────────
        private static readonly (int floor, string number, int capacity, string type)[] _roomDefs =
        {
            (1, "101", 3, "Triple"),
            (1, "102", 3, "Triple"),
            (1, "103", 3, "Triple"),
            (1, "104", 3, "Triple"),
            (2, "105", 2, "Double"),
            (2, "106", 2, "Double"),
            (2, "107", 4, "Family"),
        };

        public ReservationPage()
        {
            InitializeComponent();
            _toastTimer.Interval = TimeSpan.FromSeconds(2.8);
            _toastTimer.Tick += (s, e) =>
            {
                ToastBar.Visibility = Visibility.Collapsed;
                _toastTimer.Stop();
            };
            Loaded += (s, e) => LoadAll();
        }

        // ══════════════════════════════════════════════════════════════════
        //  ENSURE ROOMS EXIST (called before every LoadAll)
        // ══════════════════════════════════════════════════════════════════

        private void EnsureRoomsAndSchema()
        {
            DbContext.WriteBoth(conn =>
            {
                // ── 1. Migrate reservations.id_type if missing ────────────
                bool hasIdType = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(reservations);";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (r["name"].ToString() == "id_type") { hasIdType = true; break; }
                }
                // ── 3b. Migrate reservations.extra_person_price if missing ────────
                bool hasExtraPersonPrice = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(reservations);";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (r["name"].ToString() == "extra_person_price") { hasExtraPersonPrice = true; break; }
                }
                if (!hasExtraPersonPrice)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "ALTER TABLE reservations ADD COLUMN extra_person_price DECIMAL NOT NULL DEFAULT 0";
                    cmd.ExecuteNonQuery();
                }
                if (!hasIdType)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "ALTER TABLE reservations ADD COLUMN id_type TEXT NOT NULL DEFAULT ''";
                    cmd.ExecuteNonQuery();
                }

                // ── 2. Migrate guests.id_type if missing ──────────────────
                bool hasGuestIdType = false;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(guests);";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                        if (r["name"].ToString() == "id_type") { hasGuestIdType = true; break; }
                }
                if (!hasGuestIdType)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "ALTER TABLE guests ADD COLUMN id_type TEXT NOT NULL DEFAULT ''";
                    cmd.ExecuteNonQuery();
                }

                // ── 3. Fix NULL persons ───────────────────────────────────
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE guests SET persons = 1 WHERE persons IS NULL";
                    cmd.ExecuteNonQuery();
                }

                // ── 4. Insert any missing rooms ───────────────────────────
                using var tx = conn.BeginTransaction();
                foreach (var r in _roomDefs)
                {
                    // Correct capacity/type if room exists but is wrong
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            UPDATE rooms
                            SET capacity = $cap, room_type = $type, floor = $floor
                            WHERE room_number = $num
                              AND (capacity != $cap OR room_type != $type OR floor != $floor)";
                        cmd.Parameters.AddWithValue("$num", r.number);
                        cmd.Parameters.AddWithValue("$floor", r.floor);
                        cmd.Parameters.AddWithValue("$cap", r.capacity);
                        cmd.Parameters.AddWithValue("$type", r.type);
                        cmd.ExecuteNonQuery();
                    }
                    // Insert if missing
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                            INSERT OR IGNORE INTO rooms
                                (room_number, floor, capacity, room_type, status)
                            VALUES ($num, $floor, $cap, $type, 'available')";
                        cmd.Parameters.AddWithValue("$num", r.number);
                        cmd.Parameters.AddWithValue("$floor", r.floor);
                        cmd.Parameters.AddWithValue("$cap", r.capacity);
                        cmd.Parameters.AddWithValue("$type", r.type);
                        cmd.ExecuteNonQuery();
                    }
                }
                tx.Commit();
            });
        }

        // ══════════════════════════════════════════════════════════════════
        //  VALIDATION
        // ══════════════════════════════════════════════════════════════════

        private static string? ValidateName(string v)
        {
            if (string.IsNullOrWhiteSpace(v)) return "Name is required.";
            if (!System.Text.RegularExpressions.Regex.IsMatch(v, @"^[a-zA-Z\s.\-]+$"))
                return "Name must contain only letters, spaces, dots, or hyphens.";
            if (v.Trim().Length < 2) return "Name must be at least 2 characters.";
            return null;
        }

        private static string? ValidateIdNumber(string idType, string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "ID number is required.";
            return idType switch
            {
                "Aadhaar Card" =>
                    value.Length != 12 || value.Any(c => !char.IsDigit(c))
                        ? "Aadhaar must be exactly 12 digits." : null,
                "Voter ID" =>
                    !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z]{3}[0-9]{7}$")
                        ? "Voter ID: 3 uppercase letters + 7 digits (e.g. ABC1234567)." : null,
                "PAN Card" =>
                    !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$")
                        ? "PAN: 5 letters + 4 digits + 1 letter (e.g. ABCDE1234F)." : null,
                "Driving License" =>
                    value.Trim().Length < 8 || value.Trim().Length > 16
                        ? "Driving License: 8–16 characters." : null,
                "Passport" =>
                    !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z][0-9]{7}$")
                        ? "Passport: 1 letter + 7 digits (e.g. A1234567)." : null,
                _ => null
            };
        }

        private static string? ValidateMobile(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Mobile number is required.";
            var digits = System.Text.RegularExpressions.Regex.Replace(value, @"\D", "");
            if (digits.Length != 10) return "Mobile must be exactly 10 digits.";

            return null;
        }


        private static string? ValidatePrice(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "Price is required.";
            if (!decimal.TryParse(value.Trim(), out var p) || p <= 0)
                return "Enter a valid price greater than 0.";
            return null;
        }

        private static string? ValidateAdvance(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            if (!decimal.TryParse(value.Trim(), out var a)) return "Advance must be a valid number.";
            if (a < 0) return "Advance cannot be negative.";
            return null;
        }

        private static TextBlock ErrorLabel() => new()
        {
            FontSize = 12,                    // was 11
            FontWeight = FontWeights.Bold,    // was SemiBold
            Foreground = new SolidColorBrush(Color.FromRgb(220, 38, 38)),
            Margin = new Thickness(2, 0, 0, 8),
            Visibility = Visibility.Collapsed
        };

        private static void SetError(TextBox? field, TextBlock label, string? message)
        {
            if (message == null)
            {
                label.Visibility = Visibility.Collapsed;
                label.Text = "";
                if (field != null)
                    field.BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240));
            }
            else
            {
                label.Text = "⚠ " + message;
                label.Visibility = Visibility.Visible;
                if (field != null)
                    field.BorderBrush = new SolidColorBrush(Color.FromRgb(220, 38, 38));
            }
        }

        private bool IsMobileAlreadyReserved(string mobile, int excludeId,
       out string existingName, out string existingRoom)
        {
            existingName = ""; existingRoom = "";
            try
            {
                var result = DbContext.Read(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                SELECT r.guest_name, ro.room_number
                FROM reservations r
                JOIN rooms ro ON ro.id = r.room_id
                WHERE r.mobile = $mob AND r.status = 'reserved' AND r.id <> $eid
                LIMIT 1";
                    cmd.Parameters.AddWithValue("$mob", mobile);
                    cmd.Parameters.AddWithValue("$eid", excludeId);
                    using var rd = cmd.ExecuteReader();
                    return rd.Read() ? (rd.GetString(0), rd.GetString(1)) : ("", "");
                });
                if (!string.IsNullOrEmpty(result.Item1))
                {
                    existingName = result.Item1; existingRoom = result.Item2; return true;
                }
            }
            catch { }
            return false;
        }

        private bool IsMobileAlreadyActiveAlloc(string mobile,
            out string existingName, out string existingRoom)
        {
            existingName = ""; existingRoom = "";
            try
            {
                var result = DbContext.Read(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT g.full_name, r.room_number
                        FROM room_allocations ra
                        JOIN guests g ON g.id = ra.guest_id
                        JOIN rooms  r ON r.id = ra.room_id
                        WHERE g.mobile = $mob AND ra.status = 'active'
                        LIMIT 1";
                    cmd.Parameters.AddWithValue("$mob", mobile);
                    using var rd = cmd.ExecuteReader();
                    return rd.Read() ? (rd.GetString(0), rd.GetString(1)) : ("", "");
                });
                if (!string.IsNullOrEmpty(result.Item1))
                {
                    existingName = result.Item1; existingRoom = result.Item2; return true;
                }
            }
            catch { }
            return false;
        }

        // ══════════════════════════════════════════════════════════════════
        //  AUTO-FILL
        // ══════════════════════════════════════════════════════════════════

        private void TryAutoFill(
            string mobileDigits,
            TextBox txtName, ComboBox cmbIdType, TextBox txtAadhar,
            TextBox txtAddress, TextBox txtPrice,
            TextBlock errName, TextBlock errIdType, TextBlock errAadhar,
            int excludeId)
        {
            try
            {
                var guest = DbContext.Read<(string Name, string IdType, string Aadhar,
                    string Address, int Persons, decimal Price)?>(conn =>
                    {
                        using var cmd = conn.CreateCommand();
                        cmd.CommandText = @"
                            SELECT full_name, id_type, aadhar, address, persons, price_per_day
                            FROM guests WHERE mobile = $mob ORDER BY id DESC LIMIT 1";
                        cmd.Parameters.AddWithValue("$mob", mobileDigits);
                        using var r = cmd.ExecuteReader();
                        if (r.Read())
                            return (r.IsDBNull(0) ? "" : r.GetString(0),
                                    r.IsDBNull(1) ? "" : r.GetString(1),
                                    r.IsDBNull(2) ? "" : r.GetString(2),
                                    r.IsDBNull(3) ? "" : r.GetString(3),
                                    r.IsDBNull(4) ? 1 : r.GetInt32(4),
                                    r.IsDBNull(5) ? 0m : r.GetDecimal(5));
                        return null;
                    });

                if (!guest.HasValue) return;

                if (IsMobileAlreadyActiveAlloc(mobileDigits, out var actName, out var actRoom))
                {
                    ShowToast($"❌ {actName} is currently checked-in to Room {actRoom}.", false);
                    return;
                }
                if (IsMobileAlreadyReserved(mobileDigits, excludeId, out var resName, out var resRoom))
                {
                    ShowToast($"❌ {resName} already has a reservation for Room {resRoom}.", false);
                    return;
                }

                txtName.Text = guest.Value.Name;
                if (!string.IsNullOrWhiteSpace(guest.Value.IdType))
                {
                    foreach (ComboBoxItem item in cmbIdType.Items)
                    {
                        if (string.Equals(item.Content?.ToString(), guest.Value.IdType,
                                StringComparison.OrdinalIgnoreCase))
                        { cmbIdType.SelectedItem = item; break; }
                    }
                }
                txtAadhar.Text = guest.Value.Aadhar;
                txtAddress.Text = guest.Value.Address;
                if (guest.Value.Price > 0) txtPrice.Text = guest.Value.Price.ToString("F0");

                SetError(txtName, errName, null);
                SetError(null, errIdType, null);
                SetError(txtAadhar, errAadhar, null);
                ShowToast("✅ Returning guest detected! Details auto-filled.", true);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Auto-fill error: {ex.Message}");
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  LOAD
        // ══════════════════════════════════════════════════════════════════

        private void LoadAll()
        {
            // ── Always ensure rooms + schema are ready first ───────────────
            EnsureRoomsAndSchema();

            _reservations = DbContext.Read(conn =>
            {
                var list = new List<ReservationModel>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
    SELECT r.id, r.room_id, ro.room_number, ro.room_type,
           r.guest_name, r.id_type, r.aadhar, r.mobile, r.address,
           r.persons, r.extra_person_price, r.check_in, r.check_out,
           r.price_per_day, r.advance_paid, r.status
    FROM reservations r
    JOIN rooms ro ON ro.id = r.room_id
    WHERE r.status IN ('reserved','cancelled')
    ORDER BY CASE r.status WHEN 'reserved' THEN 0 ELSE 1 END, r.check_in ASC";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    list.Add(new ReservationModel
                    {
                        Id = rd.GetInt32(0),
                        RoomId = rd.GetInt32(1),
                        RoomNumber = rd.GetString(2),
                        RoomType = rd.GetString(3),
                        GuestName = rd.GetString(4),
                        IdType = rd.IsDBNull(5) ? "" : rd.GetString(5),
                        Aadhar = rd.IsDBNull(6) ? "" : rd.GetString(6),
                        Mobile = rd.IsDBNull(7) ? "" : rd.GetString(7),
                        Address = rd.IsDBNull(8) ? "" : rd.GetString(8),
                        Persons = rd.IsDBNull(9) ? 1 : rd.GetInt32(9),
                        ExtraPersonPrice = rd.IsDBNull(10) ? 0 : rd.GetDecimal(10),
                        CheckIn = DateTime.Parse(rd.GetValue(11).ToString()!),
                        CheckOut = DateTime.Parse(rd.GetValue(12).ToString()!),
                        PricePerDay = rd.IsDBNull(13) ? 0 : rd.GetDecimal(13),
                        AdvancePaid = rd.IsDBNull(14) ? 0 : rd.GetDecimal(14),
                        Status = rd.GetString(15)
                    });
                return list;
            });

            // ── Load ALL rooms for the combo (not just 'available') ────────
            _availableRooms = DbContext.Read(conn =>
            {
                var rooms = new List<RoomModel>();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT id, room_number, floor, capacity, room_type, status
                    FROM rooms
                    ORDER BY floor, room_number";
                using var rd = cmd.ExecuteReader();
                while (rd.Read())
                    rooms.Add(new RoomModel
                    {
                        Id = rd.GetInt32(0),
                        RoomNumber = rd.GetString(1),
                        Floor = rd.GetInt32(2),
                        Capacity = rd.GetInt32(3),
                        RoomType = rd.GetString(4),
                        Status = rd.GetString(5)
                    });
                return rooms;
            });

            // ── Warn if still empty after seeding ─────────────────────────
            if (_availableRooms.Count == 0)
                ShowToast("⚠️ No rooms found in database. Please check setup.", false);

            UpdateStats();
            RenderList();
        }

        private void UpdateStats()
        {
            StatUpcoming.Text = _reservations.Count(r => r.IsUpcoming).ToString();
            StatToday.Text = _reservations.Count(r => r.IsToday && r.Status == "reserved").ToString();
            var monthStart = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            StatCancelled.Text = _reservations.Count(r => r.Status == "cancelled" && r.CheckIn >= monthStart).ToString();
        }

        // ══════════════════════════════════════════════════════════════════
        //  RENDER LIST
        // ══════════════════════════════════════════════════════════════════

        private void RenderList()
        {
            ReservationsPanel.Children.Clear();

            if (_reservations.Count == 0)
            {
                ReservationsPanel.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(240, 253, 244)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(187, 247, 208)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(12),
                    Padding = new Thickness(0, 40, 0, 40),
                    Child = new TextBlock
                    {
                        Text = "📋  No reservations yet. Click '+ New Reservation' to get started.",
                        FontSize = 14,
                        Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)),
                        HorizontalAlignment = HorizontalAlignment.Center
                    }
                });
                return;
            }

            var upcoming = _reservations.Where(r => r.Status == "reserved").ToList();
            var cancelled = _reservations.Where(r => r.Status == "cancelled").ToList();

            if (upcoming.Any())
            {
                ReservationsPanel.Children.Add(GroupLabel("UPCOMING RESERVATIONS", upcoming.Count));
                foreach (var res in upcoming) ReservationsPanel.Children.Add(BuildCard(res));
            }
            if (cancelled.Any())
            {
                ReservationsPanel.Children.Add(GroupLabel("CANCELLED", cancelled.Count));
                foreach (var res in cancelled) ReservationsPanel.Children.Add(BuildCard(res));
            }
        }

        private static TextBlock GroupLabel(string text, int count) => new()
        {
            Text = $"{text}  ·  {count}",
            FontSize = 14,                    // was 11
            FontWeight = FontWeights.Bold,    // already bold
            Foreground = new SolidColorBrush(Color.FromRgb(27, 94, 32)),  // was 100,116,139 → now #1B5E20
            Margin = new Thickness(0, 0, 0, 10)
        };

        private Border BuildCard(ReservationModel res)
        {
            bool isCancelled = res.Status == "cancelled";
            bool isToday = res.IsToday;

            var card = new Border
            {
                Background = new SolidColorBrush(isCancelled ? Color.FromRgb(254, 242, 242) : Color.FromRgb(255, 255, 255)),
                BorderBrush = new SolidColorBrush(isCancelled ? Color.FromRgb(252, 165, 165) : isToday ? Color.FromRgb(234, 88, 12) : Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(isToday ? 2 : 1),
                CornerRadius = new CornerRadius(12),
                Padding = new Thickness(18, 14, 18, 14),
                Margin = new Thickness(0, 0, 0, 10)
            };

            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel();
            var topRow = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

            var avatar = new Border
            {
                Width = 34,
                Height = 34,
                CornerRadius = new CornerRadius(17),
                Background = new SolidColorBrush(isCancelled ? Color.FromRgb(252, 165, 165) : Color.FromRgb(22, 163, 74)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            avatar.Child = new TextBlock
            {
                Text = res.GuestName.Length > 0 ? res.GuestName[0].ToString().ToUpper() : "?",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            topRow.Children.Add(avatar);

            var nameBlock = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            nameBlock.Children.Add(new TextBlock
            {
                Text = res.GuestName,
                FontSize = 17,                    // was 14
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(isCancelled ? Color.FromRgb(153, 27, 27) : Color.FromRgb(0, 0, 0))  // black instead of #0F172A
            });
            nameBlock.Children.Add(new TextBlock
            {
                Text = $"Room {res.RoomNumber}  ·  {res.RoomType}  ·  {res.Persons} extra person(s)",
                FontSize = 13,                    // was 11
                FontWeight = FontWeights.Bold,    // added
                Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50))  // was 100,116,139 → now #2E7D32
            });
            topRow.Children.Add(nameBlock);

            if (isToday && !isCancelled)
                topRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(234, 88, 12)),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "TODAY", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = System.Windows.Media.Brushes.White }
                });

            if (isCancelled)
                topRow.Children.Add(new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(254, 202, 202)),
                    CornerRadius = new CornerRadius(999),
                    Padding = new Thickness(10, 3, 10, 3),
                    Margin = new Thickness(10, 0, 0, 0),
                    VerticalAlignment = VerticalAlignment.Center,
                    Child = new TextBlock { Text = "CANCELLED", FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(153, 27, 27)) }
                });

            left.Children.Add(topRow);

            var stayGrid = new Grid { Margin = new Thickness(0, 4, 0, 0) };
            for (int i = 0; i < 4; i++)
                stayGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ms0 = MiniStat("📅 Check-In", res.CheckIn.ToString("dd MMM yy"));
            var ms1 = MiniStat("🚪 Check-Out", res.CheckOut.ToString("dd MMM yy"));
            var ms2 = MiniStat("🌙 Nights", res.Days.ToString());
            var ms3 = MiniStat("💰 Total", $"₹{res.TotalAmount:F0}");
            Grid.SetColumn(ms1, 1); Grid.SetColumn(ms2, 2); Grid.SetColumn(ms3, 3);
            stayGrid.Children.Add(ms0); stayGrid.Children.Add(ms1);
            stayGrid.Children.Add(ms2); stayGrid.Children.Add(ms3);
            left.Children.Add(stayGrid);
            Grid.SetColumn(left, 0);

            var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 0, 0) };

            if (!isCancelled)
            {
                var checkInBtn = new Button
                {
                    Content = "✅ Check In",
                    Style = (Style)FindResource("PrimaryBtn"),
                    Height = 34,
                    Padding = new Thickness(14, 0, 14, 0),
                    Margin = new Thickness(0, 0, 0, 6),
                    Tag = res
                };
                checkInBtn.Click += ConvertToCheckIn_Click;
                right.Children.Add(checkInBtn);

                var editBtn = new Button
                {
                    Content = "✏ Edit",
                    Style = (Style)FindResource("DangerBtn"),
                    Margin = new Thickness(0, 0, 0, 6),
                    Tag = res
                };
                editBtn.Click += EditReservation_Click;
                right.Children.Add(editBtn);

                var cancelBtn = new Button { Content = "✕ Cancel", Style = (Style)FindResource("DangerBtn"), Tag = res };
                cancelBtn.Click += Cancel_Click;
                right.Children.Add(cancelBtn);
            }
            else
            {
                var deleteBtn = new Button { Content = "🗑 Delete", Style = (Style)FindResource("DangerBtn"), Tag = res };
                deleteBtn.Click += Delete_Click;
                right.Children.Add(deleteBtn);
            }

            Grid.SetColumn(right, 1);
            root.Children.Add(left);
            root.Children.Add(right);
            card.Child = root;
            return card;
        }

        private static StackPanel MiniStat(string label, string value)
        {
            var sp = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            sp.Children.Add(new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)) }); // 13→16, color → black
            sp.Children.Add(new TextBlock { Text = label, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(46, 125, 50)) }); // 10→12, color → #2E7D32, bold added
            return sp;
        }

        // ══════════════════════════════════════════════════════════════════
        //  NEW / EDIT RESERVATION
        // ══════════════════════════════════════════════════════════════════

        private void NewReservation_Click(object sender, RoutedEventArgs e)
        {
            _activeReservation = null;
            ModalTitle.Text = "New Reservation";
            ModalSubtitle.Text = "Fill in guest and stay details";
            ModalBody.Children.Clear();
            BuildReservationForm(null);
            ModalOverlay.Visibility = Visibility.Visible;
        }

        private void EditReservation_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReservationModel res) return;
            _activeReservation = res;
            ModalTitle.Text = "Edit Reservation";
            ModalSubtitle.Text = $"Editing reservation for {res.GuestName}";
            ModalBody.Children.Clear();
            BuildReservationForm(res);
            ModalOverlay.Visibility = Visibility.Visible;
        }

        // ══════════════════════════════════════════════════════════════════
        //  RESERVATION FORM
        // ══════════════════════════════════════════════════════════════════

        private void BuildReservationForm(ReservationModel? existing)
        {
            int excludeId = existing?.Id ?? 0;

            Grid TwoCol(FrameworkElement left, FrameworkElement right)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Grid.SetColumn(left, 0); Grid.SetColumn(right, 2);
                g.Children.Add(left); g.Children.Add(right);
                return g;
            }

            Grid FourCol(FrameworkElement a, FrameworkElement b, FrameworkElement c, FrameworkElement d)
            {
                var g = new Grid { Margin = new Thickness(0, 0, 0, 10) };
                for (int i = 0; i < 7; i++)
                    g.ColumnDefinitions.Add(new ColumnDefinition
                    {
                        Width = i % 2 == 0 ? new GridLength(1, GridUnitType.Star) : new GridLength(14)
                    });
                Grid.SetColumn(a, 0); Grid.SetColumn(b, 2); Grid.SetColumn(c, 4); Grid.SetColumn(d, 6);
                g.Children.Add(a); g.Children.Add(b); g.Children.Add(c); g.Children.Add(d);
                return g;
            }

            StackPanel Group(string label, FrameworkElement field, TextBlock? err = null)
            {
                var sp = new StackPanel();
                sp.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 15,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Colors.Black),
                    Margin = new Thickness(0, 0, 0, 4)
                });
                if (field is Control c) c.Height = 46;
                if (field is TextBox tb) tb.FontSize = 17;
                if (field is ComboBox cb) cb.FontSize = 17;
                field.Margin = new Thickness(0, 0, 0, 4);
                sp.Children.Add(field);
                if (err != null) sp.Children.Add(err);
                return sp;
            }

            // ── Room picker ───────────────────────────────────────────────
            ModalBody.Children.Add(SectionLabel("SELECT ROOM"));
            var roomCombo = new ComboBox
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5),
                VerticalContentAlignment = VerticalAlignment.Center
            };

            if (_availableRooms.Count == 0)
            {
                roomCombo.Items.Add(new ComboBoxItem
                {
                    Content = "⚠ No rooms available — please visit the Rooms page first",
                    IsEnabled = false
                });
            }
            else
            {
                foreach (var room in _availableRooms)
                    roomCombo.Items.Add(new ComboBoxItem
                    {
                        Content = $"Room {room.RoomNumber}  —  {room.RoomType}  (Cap {room.Capacity})",
                        Tag = room
                    });

                if (existing != null)
                {
                    foreach (ComboBoxItem item in roomCombo.Items)
                        if (item.Tag is RoomModel rm && rm.Id == existing.RoomId)
                        { roomCombo.SelectedItem = item; break; }
                }

                if (roomCombo.SelectedIndex < 0)
                    roomCombo.SelectedIndex = 0;
            }

            var errRoom = ErrorLabel();
            ModalBody.Children.Add(Group("Room *", roomCombo, errRoom));
            RoomModel? SelectedRoom() => (roomCombo.SelectedItem as ComboBoxItem)?.Tag as RoomModel;

            // ── Guest details ─────────────────────────────────────────────
            ModalBody.Children.Add(SectionLabel("GUEST DETAILS"));

            var txtMobile = StyledField();
            txtMobile.MaxLength = 10;
            txtMobile.PreviewTextInput += (s, e) =>
            {
                e.Handled = !e.Text.All(char.IsDigit);
            };
            DataObject.AddPastingHandler(txtMobile, (s, e) =>
            {
                if (e.DataObject.GetDataPresent(DataFormats.Text))
                {
                    var paste = System.Text.RegularExpressions.Regex.Replace(
                        (string)e.DataObject.GetData(DataFormats.Text), @"\D", "");
                    var remaining = 10 - txtMobile.Text.Length;
                    if (remaining <= 0 || paste.Length == 0) { e.CancelCommand(); return; }
                    Clipboard.SetText(paste[..Math.Min(paste.Length, remaining)]);
                }
                else e.CancelCommand();
            });
            txtMobile.Text = existing?.Mobile ?? "";
            var errMobile = ErrorLabel();

            var txtName = StyledField();
            txtName.Text = existing?.GuestName ?? "";
            var errName = ErrorLabel();

            ModalBody.Children.Add(TwoCol(
                Group("Mobile Number * (10 digits)", txtMobile, errMobile),
                Group("Full Name *", txtName, errName)));

            var cmbIdType = new ComboBox
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            foreach (var t in new[] { "Aadhaar Card", "Voter ID", "PAN Card", "Driving License", "Passport" })
                cmbIdType.Items.Add(new ComboBoxItem { Content = t });
            if (!string.IsNullOrWhiteSpace(existing?.IdType))
            {
                foreach (ComboBoxItem item in cmbIdType.Items)
                    if (string.Equals(item.Content?.ToString(), existing.IdType, StringComparison.OrdinalIgnoreCase))
                    { cmbIdType.SelectedItem = item; break; }
            }
            if (cmbIdType.SelectedIndex < 0) cmbIdType.SelectedIndex = 0;

            var txtAadhar = StyledField();
            txtAadhar.Text = existing?.Aadhar ?? "";
            var errAadhar = ErrorLabel();
            var errIdType = ErrorLabel();

            ModalBody.Children.Add(TwoCol(
                Group("Govt Id *", cmbIdType, errIdType),
                Group("ID Number *", txtAadhar, errAadhar)));
            txtAadhar.TextChanged += (s, e) =>
            {
                var box = (TextBox)s;
                var text = box.Text;
                var upper = text.ToUpper();
                if (text != upper)
                {
                    int caret = box.CaretIndex;
                    box.Text = upper;
                    box.CaretIndex = Math.Min(caret, upper.Length);
                }
            };

            void UpdateIdFieldForType()
            {
                var sel = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                txtAadhar.ToolTip = sel switch
                {
                    "Aadhaar Card" => "12 digits only",
                    "Voter ID" => "3 letters + 7 digits (e.g. ABC1234567)",
                    "PAN Card" => "5 letters + 4 digits + 1 letter",
                    "Driving License" => "8–16 alphanumeric characters",
                    "Passport" => "1 letter + 7 digits (e.g. A1234567)",
                    _ => "Enter ID number"
                };
                if (!string.IsNullOrWhiteSpace(txtAadhar.Text))
                    SetError(txtAadhar, errAadhar, ValidateIdNumber(sel, txtAadhar.Text.Trim()));
            }
            cmbIdType.SelectionChanged += (s, e) => UpdateIdFieldForType();
            UpdateIdFieldForType();

            txtAadhar.LostFocus += (s, e) =>
            {
                var idType = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Aadhaar Card";
                SetError(txtAadhar, errAadhar, ValidateIdNumber(idType, txtAadhar.Text.Trim()));
            };

            var txtAddress = new TextBox
            {
                Height = 56,
                Padding = new Thickness(10, 10, 10, 10),
                FontSize = 17,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5),
                Background = System.Windows.Media.Brushes.White,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 0, 0, 4),
                Text = existing?.Address ?? ""
            };
            ModalBody.Children.Add(Group("Address", txtAddress));

            // ── Occupancy & Pricing (4 fields in one row) ──────────────────
            ModalBody.Children.Add(SectionLabel("OCCUPANCY & PRICING"));

            var txtPersons = StyledField();
            txtPersons.Text = existing?.Persons.ToString() ?? "0";
            txtPersons.GotFocus += (s, e) =>
            {
                if (txtPersons.Text == "0") txtPersons.Text = "";
            };
            txtPersons.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPersons.Text))
                    txtPersons.Text = "0";
            };
            var errPersons = ErrorLabel();

            var txtExtraPersonPrice = StyledField();
            txtExtraPersonPrice.Text = existing?.ExtraPersonPrice.ToString("F0") ?? "0";
            txtExtraPersonPrice.GotFocus += (s, e) =>
            {
                if (txtExtraPersonPrice.Text == "0") txtExtraPersonPrice.Text = "";
            };
            txtExtraPersonPrice.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtExtraPersonPrice.Text))
                    txtExtraPersonPrice.Text = "0";
            };

            var txtPrice = StyledField();
            txtPrice.Text = existing?.PricePerDay.ToString("F0") ?? "";
            var errPrice = ErrorLabel();

            var txtAdvance = StyledField();
            txtAdvance.Text = existing?.AdvancePaid.ToString("F0") ?? "0";
            txtAdvance.GotFocus += (s, e) =>
            {
                if (txtAdvance.Text == "0") txtAdvance.Text = "";
            };
            txtAdvance.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtAdvance.Text))
                    txtAdvance.Text = "0";
            };
            var errAdvance = ErrorLabel();

            ModalBody.Children.Add(FourCol(
                Group("Extra Persons *", txtPersons, errPersons),
                Group("Price per Extra Person (₹)", txtExtraPersonPrice),
                Group("Price per Day (₹) *", txtPrice, errPrice),
                Group("Advance Paid (₹)", txtAdvance, errAdvance)));

            txtPersons.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(char.IsDigit);
            txtExtraPersonPrice.PreviewTextInput += (s, e) => e.Handled = !(e.Text.All(char.IsDigit) || e.Text == ".");

            // ── Dates ─────────────────────────────────────────────────────
            ModalBody.Children.Add(SectionLabel("STAY DATES"));

            var dpCheckIn = new DatePicker
            {
                SelectedDate = existing?.CheckIn ?? DateTime.Today,
                FontSize = 17,
                Height = 46,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5)
            };
            var errCheckIn = ErrorLabel();

            var dpCheckOut = new DatePicker
            {
                SelectedDate = existing?.CheckOut ?? DateTime.Today.AddDays(1),
                FontSize = 17,
                Height = 46,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5)
            };
            var errCheckOut = ErrorLabel();

            ModalBody.Children.Add(TwoCol(
                Group("Check-In Date *", dpCheckIn, errCheckIn),
                Group("Check-Out Date *", dpCheckOut, errCheckOut)));

            // ── Live summary ──────────────────────────────────────────────
            ModalBody.Children.Add(SectionLabel("BOOKING SUMMARY"));

            var summaryCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(27, 94, 32)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 6)
            };
            var sumGrid = new Grid();
            for (int i = 0; i < 4; i++)
            {
                sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                if (i < 3) sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            }
            var tbDays = SumBlock("1");
            var tbPersons = SumBlock("1");
            var tbTotal = SumBlock("₹0", Color.FromRgb(253, 186, 116));
            var tbBalance = SumBlock("₹0", Color.FromRgb(134, 239, 172));
            var c0 = SumColumn("📅 Days", tbDays, Color.FromRgb(46, 125, 50));
            var c2 = SumColumn("👥 Extra Persons", tbPersons, Color.FromRgb(46, 125, 50));
            var c4 = SumColumn("💰 Total", tbTotal, Color.FromRgb(46, 125, 50));
            var c6 = SumColumn("💳 Balance", tbBalance, Color.FromRgb(46, 125, 50));
            Grid.SetColumn(c0, 0);
            Grid.SetColumn(c2, 2);
            Grid.SetColumn(c4, 4);
            Grid.SetColumn(c6, 6);
            sumGrid.Children.Add(c0);
            sumGrid.Children.Add(c2);
            sumGrid.Children.Add(c4);
            sumGrid.Children.Add(c6);
            summaryCard.Child = sumGrid;
            ModalBody.Children.Add(summaryCard);

            void ApplyIdTypeConstraints()
            {
                var idType = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";

                txtAadhar.PreviewTextInput -= IdNumber_PreviewTextInput;
                txtAadhar.PreviewTextInput += IdNumber_PreviewTextInput;

                switch (idType)
                {
                    case "Aadhaar Card":
                        txtAadhar.MaxLength = 12;
                        txtAadhar.CharacterCasing = CharacterCasing.Normal;
                        break;
                    case "Voter ID":          // ABC1234567 — 10 chars
                        txtAadhar.MaxLength = 10;
                        txtAadhar.CharacterCasing = CharacterCasing.Upper;
                        break;
                    case "PAN Card":          // ABCDE1234F — 10 chars
                        txtAadhar.MaxLength = 10;
                        txtAadhar.CharacterCasing = CharacterCasing.Upper;
                        break;
                    case "Driving License":   // 8–16 alphanumeric
                        txtAadhar.MaxLength = 16;
                        txtAadhar.CharacterCasing = CharacterCasing.Upper;
                        break;
                    case "Passport":          // A1234567 — 8 chars
                        txtAadhar.MaxLength = 8;
                        txtAadhar.CharacterCasing = CharacterCasing.Upper;
                        break;
                    default:
                        txtAadhar.MaxLength = 20;
                        txtAadhar.CharacterCasing = CharacterCasing.Normal;
                        break;
                }
                txtAadhar.Clear(); // clear stale value when switching ID type
            }

            void IdNumber_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
            {
                var idType = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                e.Handled = idType switch
                {
                    "Aadhaar Card" => !e.Text.All(char.IsDigit),
                    "Voter ID" => !e.Text.All(char.IsLetterOrDigit),
                    "PAN Card" => !e.Text.All(char.IsLetterOrDigit),
                    "Driving License" => !e.Text.All(char.IsLetterOrDigit),
                    "Passport" => !e.Text.All(char.IsLetterOrDigit),
                    _ => false
                };
            }

            cmbIdType.SelectionChanged += (s, e) => ApplyIdTypeConstraints();
            ApplyIdTypeConstraints(); // apply on load for default selection

            void Recalc()
            {
                int days = 1;
                if (dpCheckIn.SelectedDate.HasValue && dpCheckOut.SelectedDate.HasValue)
                {
                    var span = (dpCheckOut.SelectedDate.Value - dpCheckIn.SelectedDate.Value).Days;
                    days = span > 0 ? span : 1;
                }
                int extraPersons = int.TryParse(txtPersons.Text.Trim(), out var ps) ? Math.Max(0, ps) : 0;
                decimal price = decimal.TryParse(txtPrice.Text.Trim(), out var p) ? p : 0;
                decimal extraPrice = decimal.TryParse(txtExtraPersonPrice.Text.Trim(), out var ep) ? ep : 0;
                decimal advance = decimal.TryParse(txtAdvance.Text.Trim(), out var a) ? a : 0;

                decimal dailyTotal = price + (extraPersons * extraPrice);
                decimal total = days * dailyTotal;
                decimal balance = total - advance;

                tbDays.Text = days.ToString();
                tbPersons.Text = (extraPersons).ToString(); // +1 for primary guest
                tbTotal.Text = $"₹{total:F0}";
                tbBalance.Text = balance >= 0 ? $"₹{balance:F0}" : $"-₹{Math.Abs(balance):F0}";
                tbBalance.Foreground = new SolidColorBrush(balance < 0
                    ? Color.FromRgb(252, 165, 165) : Color.FromRgb(134, 239, 172));

              
            }

            dpCheckIn.SelectedDateChanged += (s, e) => Recalc();
            dpCheckOut.SelectedDateChanged += (s, e) => Recalc();
            txtPrice.TextChanged += (s, e) => Recalc();
            txtAdvance.TextChanged += (s, e) => Recalc();
            txtPersons.TextChanged += (s, e) => Recalc();
            txtExtraPersonPrice.TextChanged += (s, e) => Recalc();
            Recalc();

            // ── LostFocus validation ──────────────────────────────────────
            txtName.LostFocus += (s, e) =>
                SetError(txtName, errName, ValidateName(txtName.Text.Trim()));

            txtAdvance.LostFocus += (s, e) =>
                SetError(txtAdvance, errAdvance, ValidateAdvance(txtAdvance.Text.Trim()));

            txtMobile.LostFocus += (s, e) =>
            {
                var v = ValidateMobile(txtMobile.Text.Trim());
                if (v != null) { SetError(txtMobile, errMobile, v); return; }
                var digits = System.Text.RegularExpressions.Regex.Replace(txtMobile.Text, @"\D", "");
                if (IsMobileAlreadyActiveAlloc(digits, out var an, out var ar))
                { SetError(txtMobile, errMobile, $"{an} is already checked-in to Room {ar}."); return; }
                if (IsMobileAlreadyReserved(digits, excludeId, out var rn, out var rr))
                { SetError(txtMobile, errMobile, $"{rn} already has a reservation for Room {rr}."); return; }
                SetError(txtMobile, errMobile, null);
            };

            txtPrice.LostFocus += (s, e) =>
                SetError(txtPrice, errPrice, ValidatePrice(txtPrice.Text.Trim()));

            dpCheckIn.LostFocus += (s, e) =>
                SetError(null, errCheckIn, dpCheckIn.SelectedDate.HasValue ? null : "Check-in date is required.");

            dpCheckOut.LostFocus += (s, e) =>
            {
                string? v = null;
                if (!dpCheckOut.SelectedDate.HasValue) v = "Check-out date is required.";
                else if (dpCheckIn.SelectedDate.HasValue && dpCheckOut.SelectedDate.Value <= dpCheckIn.SelectedDate.Value)
                    v = "Check-out must be after check-in.";
                SetError(null, errCheckOut, v);
            };

            // ── Auto-fill on mobile ───────────────────────────────────────
            txtMobile.TextChanged += (s, e) =>
            {
                var digits = System.Text.RegularExpressions.Regex.Replace(txtMobile.Text, @"\D", "");
                if (digits.Length == 10)
                    TryAutoFill(digits, txtName, cmbIdType, txtAadhar, txtAddress,
                        txtPrice, errName, errIdType, errAadhar, excludeId);
            };

            // ── Input guards ──────────────────────────────────────────────
            txtMobile.PreviewTextInput += (s, e) => e.Handled = !e.Text.All(char.IsDigit);
            txtPrice.PreviewTextInput += (s, e) => e.Handled = !(e.Text.All(char.IsDigit) || e.Text == ".");
            txtAdvance.PreviewTextInput += (s, e) => e.Handled = !(e.Text.All(char.IsDigit) || e.Text == ".");

            void MoveOnEnter(object sender, System.Windows.Input.KeyEventArgs e)
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    (sender as UIElement)?.MoveFocus(
                        new System.Windows.Input.TraversalRequest(
                            System.Windows.Input.FocusNavigationDirection.Next));
                }
            }

            txtMobile.KeyDown += MoveOnEnter;
            txtName.KeyDown += MoveOnEnter;
            cmbIdType.KeyDown += MoveOnEnter;
            txtAadhar.KeyDown += MoveOnEnter;
            txtAddress.KeyDown += MoveOnEnter;
            txtPersons.KeyDown += MoveOnEnter;
            txtExtraPersonPrice.KeyDown += MoveOnEnter;
            txtPrice.KeyDown += MoveOnEnter;

            // ── Save button ───────────────────────────────────────────────
            var saveBtn = new Button
            {
                Content = existing == null ? "📋  Confirm Reservation" : "💾  Update Reservation",
                Style = (Style)FindResource("PrimaryBtn"),
                Height = 44,
                Margin = new Thickness(0, 4, 0, 0)
            };
            ModalBody.Children.Add(saveBtn);

            saveBtn.Click += (s, e) =>
            {
                var name = txtName.Text.Trim();
                var idType = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Aadhaar Card";
                var aadhar = txtAadhar.Text.Trim();
                var mobile = txtMobile.Text.Trim();
                var address = txtAddress.Text.Trim();
                decimal.TryParse(txtPrice.Text.Trim(), out var priceVal);
                decimal.TryParse(txtAdvance.Text.Trim(), out var advanceVal);
                decimal.TryParse(txtExtraPersonPrice.Text.Trim(), out var extraPersonPriceVal);

                var checkIn = dpCheckIn.SelectedDate ?? DateTime.Today;
                var checkOut = dpCheckOut.SelectedDate ?? DateTime.Today.AddDays(1);
                var selectedRoom = SelectedRoom();
                int personsVal = int.TryParse(txtPersons.Text.Trim(), out var pv) ? Math.Max(0, pv) : 0;
                var vName = ValidateName(name);
                var vIdType = string.IsNullOrWhiteSpace(idType) ? "Please select an ID type." : null;
                var vAadhar = ValidateIdNumber(idType, aadhar);
                var vMobile = ValidateMobile(mobile);
                var vPrice = ValidatePrice(txtPrice.Text.Trim());
                var vAdvance = ValidateAdvance(txtAdvance.Text.Trim());
                var vRoom = selectedRoom == null ? "Please select a room." : null;
                var vPersons = personsVal < 0 ? "Persons cannot be negative." : null;

                string? vCheckIn = dpCheckIn.SelectedDate.HasValue ? null : "Check-in date is required.";
                string? vCheckOut = null;
                if (!dpCheckOut.SelectedDate.HasValue) vCheckOut = "Check-out date is required.";
                else if (checkOut <= checkIn) vCheckOut = "Check-out must be after check-in.";

                SetError(txtName, errName, vName);
                SetError(null, errIdType, vIdType);
                SetError(txtAadhar, errAadhar, vAadhar);
                SetError(txtMobile, errMobile, vMobile);
                SetError(txtPrice, errPrice, vPrice);
                SetError(txtAdvance, errAdvance, vAdvance);
                SetError(null, errCheckIn, vCheckIn);
                SetError(null, errCheckOut, vCheckOut);
                SetError(null, errRoom, vRoom);
                SetError(txtPersons, errPersons, vPersons);

                if (vName != null || vIdType != null || vAadhar != null || vMobile != null ||
                    vPrice != null || vAdvance != null || vCheckIn != null || vCheckOut != null ||
                    vRoom != null || vPersons != null)
                { ShowToast("⚠️ Please fix the errors above.", false); return; }

                var mobileDigits = System.Text.RegularExpressions.Regex.Replace(mobile, @"\D", "");
                if (IsMobileAlreadyActiveAlloc(mobileDigits, out var actName, out var actRoom))
                {
                    SetError(txtMobile, errMobile, $"{actName} is already checked-in to Room {actRoom}.");
                    ShowToast($"❌ Mobile already in use by {actName} in Room {actRoom}.", false); return;
                }
                if (IsMobileAlreadyReserved(mobileDigits, excludeId, out var resName, out var resRoom))
                {
                    SetError(txtMobile, errMobile, $"{resName} already has a reservation for Room {resRoom}.");
                    ShowToast($"❌ {resName} already reserved Room {resRoom}.", false); return;
                }

                if (DbContext.HasConflict(selectedRoom!.Id, checkIn, checkOut, excludeReservationId: excludeId))
                {
                    string conflictMsg = DbContext.Read(conn =>
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
                        SELECT g.full_name FROM room_allocations ra
                        JOIN guests g ON g.id = ra.guest_id
                        WHERE ra.room_id = $rid AND ra.status = 'active'
                          AND $ci < ra.check_out AND $co > ra.check_in LIMIT 1";
                            cmd.Parameters.AddWithValue("$rid", selectedRoom.Id);
                            cmd.Parameters.AddWithValue("$ci", checkIn.ToString("yyyy-MM-dd"));
                            cmd.Parameters.AddWithValue("$co", checkOut.ToString("yyyy-MM-dd"));
                            using var rd = cmd.ExecuteReader();
                            if (rd.Read()) return $"{(rd.IsDBNull(0) ? "Someone" : rd.GetString(0))} is checked in on those dates.";
                        }
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
                        SELECT guest_name FROM reservations
                        WHERE room_id = $rid AND status = 'reserved' AND id != $eid
                          AND $ci < check_out AND $co > check_in LIMIT 1";
                            cmd.Parameters.AddWithValue("$rid", selectedRoom.Id);
                            cmd.Parameters.AddWithValue("$eid", excludeId);
                            cmd.Parameters.AddWithValue("$ci", checkIn.ToString("yyyy-MM-dd"));
                            cmd.Parameters.AddWithValue("$co", checkOut.ToString("yyyy-MM-dd"));
                            using var rd = cmd.ExecuteReader();
                            if (rd.Read()) return $"{(rd.IsDBNull(0) ? "Someone" : rd.GetString(0))} has a reservation on those dates.";
                        }
                        return "The room is occupied or reserved on those dates.";
                    });
                    SetError(null, errRoom, conflictMsg);
                    ShowToast($"❌ Room {selectedRoom.RoomNumber}: {conflictMsg}", false); return;
                }

                SaveReservation(excludeId, selectedRoom.Id,
                    name, idType, aadhar, mobile, address, extraPersonPriceVal,
                    personsVal, checkIn, checkOut, priceVal, advanceVal);
            };
        }


        private void SaveReservation(
 int existingId, int roomId,
 string name, string idType, string aadhar, string mobile, string address,
 decimal extraPersonPrice,   // ← decimal, position 8
 int persons,                // ← int, position 9
 DateTime checkIn, DateTime checkOut,
 decimal price, decimal advance)
        {
            try
            {
                DbContext.WriteBoth(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    if (existingId == 0)
                    {
                        cmd.CommandText = @"
                    INSERT INTO reservations
                        (room_id, guest_name, id_type, aadhar, mobile, address, persons,
                         extra_person_price, check_in, check_out, price_per_day, advance_paid, status)
                    VALUES ($rid,$nm,$idt,$aa,$mo,$addr,$pers,
                            $epp,$ci,$co,$ppd,$adv,'reserved')";
                    }
                    else
                    {
                        cmd.CommandText = @"
                    UPDATE reservations SET
                        room_id=$rid, guest_name=$nm, id_type=$idt, aadhar=$aa,
                        mobile=$mo, address=$addr, persons=$pers,
                        extra_person_price=$epp,
                        check_in=$ci, check_out=$co,
                        price_per_day=$ppd, advance_paid=$adv
                    WHERE id=$eid";
                        cmd.Parameters.AddWithValue("$eid", existingId);
                    }
                    cmd.Parameters.AddWithValue("$rid", roomId);
                    cmd.Parameters.AddWithValue("$nm", name);
                    cmd.Parameters.AddWithValue("$idt", idType);
                    cmd.Parameters.AddWithValue("$aa", aadhar);
                    cmd.Parameters.AddWithValue("$mo", mobile);
                    cmd.Parameters.AddWithValue("$addr", address);
                    cmd.Parameters.AddWithValue("$pers", persons);
                    cmd.Parameters.AddWithValue("$epp", extraPersonPrice);   // ← ADD
                    cmd.Parameters.AddWithValue("$ci", checkIn.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$co", checkOut.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$ppd", price);
                    cmd.Parameters.AddWithValue("$adv", advance);
                    cmd.ExecuteNonQuery();
                });
                // ... rest unchanged

                ShowToast(existingId == 0 ? "✅ Reservation confirmed!" : "✅ Reservation updated!", true);
                ModalOverlay.Visibility = Visibility.Collapsed;
                LoadAll();
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", false); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  CONVERT → CHECK-IN
        // ══════════════════════════════════════════════════════════════════

        private void ConvertToCheckIn_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReservationModel res) return;

            if (res.CheckIn.Date != DateTime.Today)
            {
                var msg = res.CheckIn.Date > DateTime.Today
                    ? $"Check-in date is {res.CheckIn:dd MMM yyyy}, which is in the future."
                    : $"Check-in date was {res.CheckIn:dd MMM yyyy}, which has already passed. Please update the dates.";
                MessageBox.Show(msg, "Cannot Check In", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Check for active allocation conflicts on those dates
            if (DbContext.HasConflict(res.RoomId, res.CheckIn, res.CheckOut,
                    incomingPersons: res.Persons, excludeReservationId: res.Id, skipReservationCheck: true))
            {
                MessageBox.Show(
                    $"Room {res.RoomNumber} is already occupied on those dates.",
                    "Cannot Check In", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Check in {res.GuestName} now?\n\n" +
                $"Room {res.RoomNumber}  ·  {res.Days} night(s)  ·  ₹{res.TotalAmount:F0}\n\n" +
                $"This will allocate the room and prevent any new check-ins until checkout.",
                "Confirm Check-In", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                DbContext.WriteBoth(conn =>
                {
                    // ── 1. Insert guest record ────────────────────────────────
                    int guestId;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    INSERT INTO guests (full_name, id_type, aadhar, mobile, address,
                                        persons, price_per_day, extra_person_price)
                    VALUES ($n,$idt,$a,$m,$addr,$pers,$p,$epp);
                    SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("$n", res.GuestName);
                        cmd.Parameters.AddWithValue("$idt", res.IdType);
                        cmd.Parameters.AddWithValue("$a", res.Aadhar);
                        cmd.Parameters.AddWithValue("$m", res.Mobile);
                        cmd.Parameters.AddWithValue("$addr", res.Address);
                        cmd.Parameters.AddWithValue("$pers", res.Persons);
                        cmd.Parameters.AddWithValue("$p", res.PricePerDay);
                        cmd.Parameters.AddWithValue("$epp", res.ExtraPersonPrice);
                        guestId = Convert.ToInt32(cmd.ExecuteScalar());
                    }

                    // ── 2. Create active room allocation ──────────────────────
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    INSERT INTO room_allocations
                        (room_id, guest_id, check_in, check_out, status, advance_paid)
                    VALUES ($rid, $gid, $ci, $co, 'active', $adv)";
                        cmd.Parameters.AddWithValue("$rid", res.RoomId);
                        cmd.Parameters.AddWithValue("$gid", guestId);
                        // Use actual now as check-in timestamp, checkout from reservation
                        cmd.Parameters.AddWithValue("$ci", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("$co", res.CheckOut.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("$adv", res.AdvancePaid);
                        cmd.ExecuteNonQuery();
                    }

                    // ── 3. Mark reservation as checked-in ────────────────────
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = "UPDATE reservations SET status='checked_in' WHERE id=$id";
                        cmd.Parameters.AddWithValue("$id", res.Id);
                        cmd.ExecuteNonQuery();
                    }

                    // ── 4. Update room status correctly (full/partial/available)
                    //       using the same logic as direct check-in ────────────
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
                    UPDATE rooms SET status = CASE
                        WHEN (
                            SELECT COALESCE(SUM(g.persons), 0)
                            FROM room_allocations ra
                            JOIN guests g ON g.id = ra.guest_id
                            WHERE ra.room_id = $rid AND ra.status = 'active'
                        ) = 0
                        THEN 'available'
                        WHEN (
                            SELECT COALESCE(SUM(g.persons), 0)
                            FROM room_allocations ra
                            JOIN guests g ON g.id = ra.guest_id
                            WHERE ra.room_id = $rid AND ra.status = 'active'
                        ) >= (SELECT capacity FROM rooms WHERE id = $rid)
                        THEN 'full'
                        ELSE 'partial'
                    END
                    WHERE id = $rid";
                        cmd.Parameters.AddWithValue("$rid", res.RoomId);
                        cmd.ExecuteNonQuery();
                    }
                });

                DbContext.SyncBackup();

                ShowToast($"✅ {res.GuestName} checked in to Room {res.RoomNumber}!", true);
                LoadAll();
            }
            catch (Exception ex)
            {
                ShowToast($"Check-in failed: {ex.Message}", false);
            }
        }
        // ══════════════════════════════════════════════════════════════════
        //  CANCEL / DELETE
        // ══════════════════════════════════════════════════════════════════

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReservationModel res) return;
            var confirm = MessageBox.Show(
                $"Cancel reservation for {res.GuestName}?\nRoom {res.RoomNumber}  ·  {res.CheckIn:dd MMM yyyy}",
                "Cancel Reservation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            try
            {
                DbContext.WriteBoth(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "UPDATE reservations SET status='cancelled' WHERE id=$id";
                    cmd.Parameters.AddWithValue("$id", res.Id);
                    cmd.ExecuteNonQuery();
                });
                ShowToast("Reservation cancelled.", true);
                LoadAll();
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", false); }
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not ReservationModel res) return;
            var confirm = MessageBox.Show(
                $"Permanently delete this cancelled reservation for {res.GuestName}?",
                "Delete Reservation", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm != MessageBoxResult.Yes) return;
            try
            {
                DbContext.WriteBoth(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM reservations WHERE id=$id";
                    cmd.Parameters.AddWithValue("$id", res.Id);
                    cmd.ExecuteNonQuery();
                });
                ShowToast("Reservation deleted.", true);
                LoadAll();
            }
            catch (Exception ex) { ShowToast($"Error: {ex.Message}", false); }
        }

        // ══════════════════════════════════════════════════════════════════
        //  MODAL / TOAST
        // ══════════════════════════════════════════════════════════════════

        private void CloseModal_Click(object sender, RoutedEventArgs e)
            => ModalOverlay.Visibility = Visibility.Collapsed;

        private void Overlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ModalOverlay.Visibility = Visibility.Collapsed;

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

        // ══════════════════════════════════════════════════════════════════
        //  UI HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            FontSize = 13,                    // was 11
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(27, 94, 32)),  // was 100,116,139 → #1B5E20
            Margin = new Thickness(0, 0, 0, 8)
        };

        private static TextBlock FieldLabel(string text) => new()
        {
            Text = text,
            FontSize = 13,                    // was 11
            FontWeight = FontWeights.Bold,    // was SemiBold
            Foreground = new SolidColorBrush(Color.FromRgb(0, 0, 0)),  // was 100,116,139 → black
            Margin = new Thickness(0, 0, 0, 4)
        };

        private TextBox StyledField() => new()
        {
            Style = (Style)FindResource("FormField"),
            Margin = new Thickness(0, 0, 0, 4)
        };

        private static TextBlock SumBlock(string text, Color? color = null) => new()
        {
            Text = text,
            FontSize = 20,                    // was 18
            FontWeight = FontWeights.Bold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Foreground = new SolidColorBrush(color ?? Color.FromRgb(200, 230, 201))  // lighten default for dark bg
        };

        private static Border SumColumn(string label, TextBlock valueBlock, Color bg)
        {
            var b = new Border { Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(8, 10, 8, 10) };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(valueBlock);
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,                 // was 10
                FontWeight = FontWeights.Bold, // was SemiBold
                Foreground = new SolidColorBrush(Color.FromRgb(200, 230, 201)),  // was 100,116,139 → light green for contrast on dark card
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            b.Child = sp;
            return b;
        }

    }
} 