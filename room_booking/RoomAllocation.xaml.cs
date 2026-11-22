#nullable enable
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using static room_booking.RoomAllocationPage;
using static System.Net.Mime.MediaTypeNames;

namespace room_booking
{
    // ── Data models ───────────────────────────────────────────────────────
    public class RoomModel
    {
        public int Id { get; set; }
        public string RoomNumber { get; set; } = "";
        public int Floor { get; set; }
        public int Capacity { get; set; }
        public string RoomType { get; set; } = "";
        public string Status { get; set; } = "";
        public List<GuestModel> Guests { get; set; } = new();
        public int Occupied => Guests.Count;
        // In RoomModel:
        public int TotalPersons => Guests.Sum(g => g.Persons + 1);  // +1 for the primary guest

        public string OccupancyState => TotalPersons >= 1 ? "full" : "empty";
    }
    public class GuestModel
    {
        public int AllocId { get; set; }
        public int GuestId { get; set; }
        public string FullName { get; set; } = "";
        public string IdType { get; set; } = "";
        public string Aadhar { get; set; } = "";
        public string Address { get; set; } = "";
        public string Mobile { get; set; } = "";
        public decimal PricePerDay { get; set; }
        public decimal ExtraPersonPrice { get; set; }
        public DateTime CheckIn { get; set; }
        public int Persons { get; set; } = 0;
    }
    public partial class RoomAllocationPage : Page
    {
        private List<RoomModel> _rooms = new();
        private RoomModel? _activeRoom;
        private readonly DispatcherTimer _toastTimer = new();
        private bool _suppressOverlayClose = false;
        public RoomAllocationPage()
        {
            InitializeComponent();
            _toastTimer.Interval = TimeSpan.FromSeconds(2.8);
            _toastTimer.Tick += (s, e) => { ToastBar.Visibility = Visibility.Collapsed; _toastTimer.Stop(); };
            Loaded += (s, e) =>
            {
                SeedRoomsIfEmpty();
                LoadAll();
            };
        }

        // ── Seed 15 rooms across 3 floors if the table is empty ───────────
        // Uses DbContext.WriteBoth so both AppData and D: drive DBs are seeded.
        private void SeedRoomsIfEmpty()
        {
            // ── Migrate id_type column (safe to run every startup) ────────────
            DbContext.WriteBoth(conn =>
            {
                bool columnExists = false;

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "PRAGMA table_info(guests);";

                    using var reader = cmd.ExecuteReader();

                    while (reader.Read())
                    {
                        if (reader["name"].ToString() == "id_type")
                        {
                            columnExists = true;
                            break;
                        }
                    }
                }

                if (!columnExists)
                {
                    using var alter = conn.CreateCommand();
                    alter.CommandText =
                        "ALTER TABLE guests ADD COLUMN id_type TEXT NOT NULL DEFAULT ''";
                    alter.ExecuteNonQuery();
                }
            });

            // ── Fix any rows where persons is NULL ────────────────────────────
            DbContext.WriteBoth(conn =>
            {
                using var fix = conn.CreateCommand();
                fix.CommandText = "UPDATE guests SET persons = 1 WHERE persons IS NULL";
                fix.ExecuteNonQuery();
            });

            var rooms = new (int floor, string number, int capacity, string type)[]
  {
    (1, "101", 3, "Triple"),
    (1, "102", 3, "Triple"),
    (1, "103", 3, "Triple"),
    (1, "104", 3, "Triple"),
    (2, "105", 2, "Double"),
    (2, "106", 2, "Double"),
    (2, "107", 4, "Family"),
  };

            // ── Delete rooms not in the new list (safe — only removes unoccupied) ─
            DbContext.WriteBoth(conn =>
            {
                var keepNumbers = string.Join(",", rooms.Select(r => $"'{r.number}'"));
                using var cmd = conn.CreateCommand();
                cmd.CommandText = $@"
        DELETE FROM rooms
        WHERE room_number NOT IN ({keepNumbers})
          AND id NOT IN (
              SELECT DISTINCT room_id FROM room_allocations
          )
          AND id NOT IN (
              SELECT DISTINCT room_id FROM bills WHERE room_id IS NOT NULL
          )
          AND id NOT IN (
              SELECT DISTINCT room_id FROM reservations WHERE room_id IS NOT NULL
          )";
                cmd.ExecuteNonQuery();
            });
            // ── Correct capacity/type for any mismatched rooms ────────────────────
            DbContext.WriteBoth(conn =>
            {
                using var tx = conn.BeginTransaction();
                foreach (var r in rooms)
                {
                    using var cmd = conn.CreateCommand();
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
                tx.Commit();
            });

            // ── Insert any missing rooms ──────────────────────────────────────────
            DbContext.WriteBoth(conn =>
            {
                using var tx = conn.BeginTransaction();
                foreach (var r in rooms)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
            INSERT OR IGNORE INTO rooms (room_number, floor, capacity, room_type, status)
            VALUES ($num, $floor, $cap, $type, 'available')";
                    cmd.Parameters.AddWithValue("$num", r.number);
                    cmd.Parameters.AddWithValue("$floor", r.floor);
                    cmd.Parameters.AddWithValue("$cap", r.capacity);
                    cmd.Parameters.AddWithValue("$type", r.type);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });

            // ── Seed rooms only if table is empty ─────────────────────────────
            int existing = DbContext.Read(conn =>
            {
                using var check = conn.CreateCommand();
                check.CommandText = "SELECT COUNT(*) FROM rooms";
                return Convert.ToInt32(check.ExecuteScalar());
            });

            if (existing > 0) return;

            DbContext.WriteBoth(conn =>
            {
                using var tx = conn.BeginTransaction();
                foreach (var r in rooms)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                INSERT OR IGNORE INTO rooms (room_number, floor, capacity, room_type, status)
                VALUES ($num, $floor, $cap, $type, 'available')";
                    cmd.Parameters.AddWithValue("$num", r.number);
                    cmd.Parameters.AddWithValue("$floor", r.floor);
                    cmd.Parameters.AddWithValue("$cap", r.capacity);
                    cmd.Parameters.AddWithValue("$type", r.type);
                    cmd.ExecuteNonQuery();
                }
                tx.Commit();
            });
        }
        private static string? ValidateIdNumber(string idType, string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "ID number is required.";

            return idType switch
            {
                "Aadhaar Card" =>
                    value.Length != 12 || value.Any(c => !char.IsDigit(c))
                        ? "Aadhaar must be exactly 12 digits, no letters or symbols."
                        : null,

                "Voter ID" =>
                    !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z]{3}[0-9]{7}$")
                        ? "Voter ID must be 3 uppercase letters followed by 7 digits (e.g. ABC1234567)."
                        : null,

                "PAN Card" =>
                    !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z]{5}[0-9]{4}[A-Z]{1}$")
                        ? "PAN must be 5 letters, 4 digits, 1 letter (e.g. ABCDE1234F)."
                        : null,

                "Driving License" =>
                    value.Trim().Length < 8 || value.Trim().Length > 16
                        ? "Driving License must be between 8 and 16 characters."
                        : null,

                "Passport" =>
                    !System.Text.RegularExpressions.Regex.IsMatch(value.Trim(), @"^[A-Z][0-9]{7}$")
                        ? "Passport must be 1 letter followed by 7 digits (e.g. A1234567)."
                        : null,

                _ => null
            };
        }
        private void LoadAll()
        {
            _rooms = DbContext.Read(conn =>
            {
                var rooms = new List<RoomModel>();

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText =
                        @"SELECT id, room_number, floor, capacity, room_type, status
                          FROM rooms
                          ORDER BY floor, room_number";

                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        rooms.Add(new RoomModel
                        {
                            Id = r.GetInt32(0),
                            RoomNumber = r.GetString(1),
                            Floor = r.GetInt32(2),
                            Capacity = r.GetInt32(3),
                            RoomType = r.GetString(4),
                            Status = r.GetString(5)
                        });
                    }
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
    SELECT
        ra.id,
        ra.room_id,
        g.id,
        g.full_name,
        g.aadhar,
        g.address,
        g.mobile,
        g.price_per_day,
        ra.check_in,
        g.persons,
        g.id_type,
        g.extra_person_price
    FROM room_allocations ra
    JOIN guests g ON g.id = ra.guest_id
    WHERE ra.status = 'active'";
                    using var r = cmd.ExecuteReader();
                    while (r.Read())
                    {
                        int roomId = r.GetInt32(1);
                        var room = rooms.FirstOrDefault(x => x.Id == roomId);
                        if (room == null) continue;

                        room.Guests.Add(new GuestModel
                        {
                            AllocId = r.GetInt32(0),
                            GuestId = r.GetInt32(2),
                            FullName = r.GetString(3),
                            Aadhar = r.IsDBNull(4) ? "" : r.GetString(4),
                            Address = r.IsDBNull(5) ? "" : r.GetString(5),
                            Mobile = r.IsDBNull(6) ? "" : r.GetString(6),
                            PricePerDay = r.IsDBNull(7) ? 0 : r.GetDecimal(7),
                            CheckIn = r.IsDBNull(8)
                                ? DateTime.Today
                                : DateTime.Parse(r.GetValue(8).ToString()!),
                            Persons = r.IsDBNull(9) ? 0 : r.GetInt32(9),
                            IdType = r.IsDBNull(10) ? "" : r.GetString(10),
                            ExtraPersonPrice = r.IsDBNull(11) ? 0 : r.GetDecimal(11)
                        });
                    }
                    
                }
                return rooms;
            });

            UpdateChips();
            RenderRooms();
        }

        // ── Stats chips ───────────────────────────────────────────────────
        private void UpdateChips()
        {
            ChipEmpty.Text = $"{_rooms.Count(r => r.OccupancyState == "empty")} Empty";
            ChipFull.Text = $"{_rooms.Count(r => r.OccupancyState == "full")} Occupied";
        }
        // ── Render room grid grouped by floor ────────────────────────────
        // ── Render all rooms in a single grid (no floor grouping) ─────────
        private void RenderRooms()
        {
            RoomsPanel.Children.Clear();

            var grid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 24) };
            foreach (var room in _rooms.OrderBy(r => r.RoomNumber))
                grid.Children.Add(BuildRoomCard(room));

            RoomsPanel.Children.Add(grid);
        }

        private Button? _expandedCardRoom = null;

        private FrameworkElement BuildRoomCard(RoomModel room)
        {
            var (bg, borderColor, numColor, typeColor, pillBg, pillFg) = room.OccupancyState switch
            {
                "full" => ("#FEF2F2", "#FCA5A5", "#991B1B", "#DC2626",
                           Color.FromArgb(140, 153, 27, 27), Color.FromRgb(255, 215, 215)),
                _ => ("#F0FDF4", "#86EFAC", "#14532D", "#16A34A",
                           Color.FromArgb(140, 20, 83, 45), Color.FromRgb(190, 255, 210))
            };

            var container = new Grid
            {
                Margin = new Thickness(0, 0, 14, 14)
            };

            var cardBtn = new Button
            {
                Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg)),
                BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(borderColor)),
                BorderThickness = new Thickness(1.5),
                Cursor = Cursors.Hand,
                Tag = room,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            cardBtn.Style = (Style)FindResource("RoomCardStyle");
            cardBtn.Click += RoomCard_Click;

            var root = new Grid { Margin = new Thickness(16, 16, 16, 14) };
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // header
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // guest info
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(8) }); // fixed spacer ← was Star
            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });   // bed indicators

            // ── Row 0: room number + type ─────────────────────────────────────
            var headerStack = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };

            var topRow = new Grid();
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            topRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerStack.Children.Add(new TextBlock
            {
                Text = room.RoomNumber,
                FontSize = 38,
                FontWeight = FontWeights.ExtraBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(numColor)),
                LineHeight = 40
            });

            var typeAndCapRow = new StackPanel { Orientation = Orientation.Horizontal };
            typeAndCapRow.Children.Add(new TextBlock
            {
                Text = room.RoomType.ToUpper(),
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeColor))
            });
            typeAndCapRow.Children.Add(new TextBlock
            {
                Text = $"  ·  {room.Capacity} beds",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeColor)),
                Opacity = 0.7
            });
            headerStack.Children.Add(typeAndCapRow);

            Grid.SetRow(headerStack, 0);
            root.Children.Add(headerStack);

            // ── Row 1: guest info area ────────────────────────────────────────
            var guestArea = new Grid { Margin = new Thickness(0, 0, 0, 8) };
            guestArea.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            guestArea.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var guestInfoPanel = new StackPanel();
            Grid.SetColumn(guestInfoPanel, 0);

            var arrowPanel = new StackPanel
            {
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(6, 0, 0, 0),
                Visibility = room.Guests.Count > 1 ? Visibility.Visible : Visibility.Collapsed
            };
            Grid.SetColumn(arrowPanel, 1);

            int guestIndex = 0;

            void RenderGuestInfo()
            {
                guestInfoPanel.Children.Clear();

                if (room.Occupied == 0)
                {
                    var availPill = new Border
                    {
                        Background = new SolidColorBrush(Color.FromArgb(90, 20, 83, 45)),
                        CornerRadius = new CornerRadius(8),
                        Padding = new Thickness(10, 5, 10, 5),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    availPill.Child = new TextBlock
                    {
                        Text = "Available",
                        FontSize = 14,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeColor))
                    };
                    guestInfoPanel.Children.Add(availPill);
                }
                else
                {
                    var g = room.Guests[guestIndex];

                    if (room.Guests.Count > 1)
                    {
                        var counterPill = new Border
                        {
                            Background = new SolidColorBrush(Color.FromArgb(55, 0, 0, 0)),
                            CornerRadius = new CornerRadius(5),
                            Padding = new Thickness(7, 2, 7, 2),
                            Margin = new Thickness(0, 0, 0, 5),
                            HorizontalAlignment = HorizontalAlignment.Left
                        };
                        counterPill.Child = new TextBlock
                        {
                            Text = $"Guest {guestIndex + 1} of {room.Guests.Count}",
                            FontSize = 12,
                            FontWeight = FontWeights.SemiBold,
                            Foreground = Brushes.White
                        };
                        guestInfoPanel.Children.Add(counterPill);
                    }

                    var namePill = new Border
                    {
                        Background = new SolidColorBrush(pillBg),
                        CornerRadius = new CornerRadius(7),
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(0, 0, 0, 4),
                        HorizontalAlignment = HorizontalAlignment.Left
                    };
                    namePill.Child = new TextBlock
                    {
                        Text = g.FullName.Split(' ')[0],
                        FontSize = 15,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(pillFg),
                        TextTrimming = TextTrimming.CharacterEllipsis,
                        MaxWidth = 130
                    };
                    guestInfoPanel.Children.Add(namePill);

                    var personRow = new StackPanel { Orientation = Orientation.Horizontal };
                    personRow.Children.Add(new TextBlock
                    {
                        Text = $"👤 {g.Persons} Extra person(s)",
                        FontSize = 13,
                        FontWeight = FontWeights.SemiBold,
                        Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(typeColor))
                    });
                    guestInfoPanel.Children.Add(personRow);
                }
            }

            RenderGuestInfo();

            if (room.Guests.Count > 1)
            {
                static Button ArrowBtn(string content) => new()
                {
                    Content = content,
                    FontSize = 10,
                    Width = 24,
                    Height = 24,
                    Background = new SolidColorBrush(Color.FromArgb(65, 0, 0, 0)),
                    Foreground = Brushes.White,
                    BorderThickness = new Thickness(0),
                    Cursor = Cursors.Hand
                };

                var upArrow = ArrowBtn("▲");
                var downArrow = ArrowBtn("▼");
                upArrow.Margin = new Thickness(0, 0, 0, 3);

                upArrow.Click += (s, e) =>
                {
                    e.Handled = true;
                    guestIndex = (guestIndex - 1 + room.Guests.Count) % room.Guests.Count;
                    RenderGuestInfo();
                };
                downArrow.Click += (s, e) =>
                {
                    e.Handled = true;
                    guestIndex = (guestIndex + 1) % room.Guests.Count;
                    RenderGuestInfo();
                };

                arrowPanel.Children.Add(upArrow);
                arrowPanel.Children.Add(downArrow);
            }

            guestArea.Children.Add(guestInfoPanel);
            guestArea.Children.Add(arrowPanel);
            Grid.SetRow(guestArea, 1);
            root.Children.Add(guestArea);

            // ── Row 3: bed indicators ─────────────────────────────────────────
            var bedRow = new WrapPanel { Margin = new Thickness(0, 4, 0, 0) };
            bool isOccupied = room.Occupied > 0;

            for (int i = 0; i < room.Capacity; i++)
            {
                var bed = new Border
                {
                    Width = 26,
                    Height = 26,
                    CornerRadius = new CornerRadius(5),
                    Background = new SolidColorBrush(isOccupied
                        ? Color.FromRgb(220, 38, 38)
                        : Color.FromRgb(22, 163, 74)),
                    BorderBrush = new SolidColorBrush(isOccupied
                        ? Color.FromRgb(185, 28, 28)
                        : Color.FromRgb(21, 128, 61)),
                    BorderThickness = new Thickness(1.5),
                    Margin = new Thickness(0, 0, 5, 0),
                    ToolTip = isOccupied ? "Occupied" : "Available"
                };
                bedRow.Children.Add(bed);


            }
            Grid.SetRow(bedRow, 3);         // ← ADD THIS
            root.Children.Add(bedRow);

            cardBtn.Content = root;
            container.Children.Add(cardBtn);
            return container;
        }
        private void RoomCard_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is RoomModel room)
                OpenModal(room);
        }

        // ── Modal open ────────────────────────────────────────────────────
        private void OpenModal(RoomModel room)
        {
            _activeRoom = room;
            ModalRoomNumber.Text = room.RoomNumber;
            ModalRoomMeta.Text = $"{room.RoomType}  ·  Capacity {room.Capacity}";
            ModalBody.Children.Clear();

            if (room.OccupancyState == "empty")
                BuildCheckInForm();
            else
                BuildRoomInfoView();

            ModalOverlay.Visibility = Visibility.Visible;
        }
        private static string? ValidateName(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Name is required.";
            // Only letters, spaces, dots, hyphens — no digits or special chars
            if (!System.Text.RegularExpressions.Regex.IsMatch(value, @"^[a-zA-Z\s.\-]+$"))
                return "Name must contain only letters, spaces, dots, or hyphens.";
            if (value.Length < 2)
                return "Name must be at least 2 characters.";
            return null;
        }

        private static string? ValidateIdType(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "ID type is required.";
            return null;
        }

        private static string? ValidateAadhar(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Identity number is required.";
            return null;   // no digit-count constraint — accepts any ID
        }
        private static string? ValidateMobile(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return "Mobile number is required.";
            var digits = System.Text.RegularExpressions.Regex.Replace(value, @"\D", "");
            if (digits.Length != 10)
                return "Mobile must be exactly 10 digits.";
            return null;
        }

        private static TextBlock ErrorLabel() => new()
        {
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
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
        private void BuildCheckInForm()
        {
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
            // ── GUEST DETAILS ───────────────────────────────────────────────────
            ModalBody.Children.Add(SectionLabel("GUEST DETAILS"));

            var txtMobile = Field("txtMobile", "e.g. 9876543210");
            var errMobile = ErrorLabel();

            var txtName = Field("txtName", "e.g. Ramesh Kumar");
            var errName = ErrorLabel();

            ModalBody.Children.Add(TwoCol(
                Group("Mobile Number * (10 digits)", txtMobile, errMobile),
                Group("Full Name *", txtName, errName)));

            var cmbIdType = new ComboBox
            {
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5),
                VerticalContentAlignment = VerticalAlignment.Center,
                MaxDropDownHeight = 200
            };

            // Force dropdown to render outside the modal's clipping boundary
            cmbIdType.Loaded += (s, e) =>
            {
                if (cmbIdType.Template.FindName("PART_Popup", cmbIdType) is System.Windows.Controls.Primitives.Popup popup)
                {
                    popup.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
                    popup.PlacementTarget = cmbIdType;
                    popup.AllowsTransparency = true;
                }
            };
            foreach (var t in new[] { "Aadhaar Card", "Voter ID", "PAN Card", "Driving License", "Passport" })
                cmbIdType.Items.Add(new ComboBoxItem { Content = t });
            cmbIdType.SelectedIndex = 0;

            var txtAadhar = Field("txtAadhar", "e.g. 123456789012");
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

            // ── Dynamic tooltip + re-validate when ID type changes ────────────────
            void UpdateIdFieldForType()
            {
                var selected = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "";
                txtAadhar.ToolTip = selected switch
                {
                    "Aadhaar Card" => "12 digits only (e.g. 123456789012)",
                    "Voter ID" => "3 uppercase letters + 7 digits (e.g. ABC1234567)",
                    "PAN Card" => "5 letters + 4 digits + 1 letter (e.g. ABCDE1234F)",
                    "Driving License" => "8–16 alphanumeric characters",
                    "Passport" => "1 letter + 7 digits (e.g. A1234567)",
                    _ => "Enter ID number"
                };
                if (!string.IsNullOrWhiteSpace(txtAadhar.Text))
                    SetError(txtAadhar, errAadhar, ValidateIdNumber(selected, txtAadhar.Text.Trim()));
            }
            cmbIdType.SelectionChanged += (s, e) => UpdateIdFieldForType();
            UpdateIdFieldForType();

            // ── Address (full width, compact) ──────────────────────────────────
            var txtAddress = new TextBox
            {
                Height = 56,
                Padding = new Thickness(10, 10, 10, 10),
                Foreground = new SolidColorBrush(Colors.Black),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5),
                Background = Brushes.White,
                FontSize = 17,
                AcceptsReturn = false,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                TextWrapping = TextWrapping.Wrap
            };
            ModalBody.Children.Add(Group("Address", txtAddress));

            // ── OCCUPANCY & PRICING (4 fields in one row) ──────────────────────
            ModalBody.Children.Add(SectionLabel("OCCUPANCY & PRICING"));

            var txtPersons = Field("txtPersons", "e.g. 2");
            txtPersons.Text = "0";
            txtPersons.GotFocus += (s, e) =>
            {
                if (txtPersons.Text == "0") txtPersons.Text = "";
            };
            txtPersons.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtPersons.Text))
                    txtPersons.Text = "0";
            };

            var txtExtraPersonPrice = Field("txtExtraPersonPrice", "e.g. 200");
            txtExtraPersonPrice.Text = "0";
            txtExtraPersonPrice.GotFocus += (s, e) =>
            {
                if (txtExtraPersonPrice.Text == "0") txtExtraPersonPrice.Text = "";
            };
            txtExtraPersonPrice.LostFocus += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(txtExtraPersonPrice.Text))
                    txtExtraPersonPrice.Text = "0";
            };

            // ── NEW: disable extra person price when extra persons = 0 ─────────
            void SyncExtraPersonPriceState()
            {
                int extraPersons = int.TryParse(txtPersons.Text.Trim(), out var ep) ? ep : 0;
                bool enable = extraPersons > 0;

                txtExtraPersonPrice.IsEnabled = enable;
                if (!enable)
                {
                    txtExtraPersonPrice.Text = "0";
                    txtExtraPersonPrice.Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)); // greyed bg
                    txtExtraPersonPrice.Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)); // muted text
                }
                else
                {
                    txtExtraPersonPrice.Background = Brushes.White;
                    txtExtraPersonPrice.Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42));
                }
            }

            txtPersons.TextChanged += (s, e) => SyncExtraPersonPriceState();
            SyncExtraPersonPriceState(); // initial state on form load
            var txtPrice = Field("txtPrice", "e.g. 800");
            var errPrice = ErrorLabel();
            var errPersons = ErrorLabel();
            var txtAdvance = Field("txtAdvance", "e.g. 500");
            var errAdvance = ErrorLabel();

            ModalBody.Children.Add(FourCol(
                Group("Extra Persons *", txtPersons, errPersons),
                Group("Price per extra person (₹)", txtExtraPersonPrice),
                Group("Price per Day (₹) *", txtPrice, errPrice),
                Group("Advance Paid (₹)", txtAdvance, errAdvance)));

            var dpCheckOut = new DatePicker
            {
                SelectedDate = DateTime.Today.AddDays(1),
                FontSize = 17,
                Height = 46,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(1.5)
            };
            var errCheckOut = ErrorLabel();

            ModalBody.Children.Add(TwoCol(
                Group("Check-Out Date *", dpCheckOut, errCheckOut),
                new Border())); // empty right column to keep date picker half-width

            // ── Live summary card (compact single row) ─────────────────────────
            var summaryCard = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Margin = new Thickness(0, 4, 0, 6)
            };

            var sumGrid = new Grid();
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var tbDays = new TextBlock { Text = "1", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), HorizontalAlignment = HorizontalAlignment.Center };
            var tbPersons = new TextBlock { Text = "0", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(148, 163, 184)), HorizontalAlignment = HorizontalAlignment.Center };
            var tbTotal = new TextBlock { Text = "₹0", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(253, 186, 116)), HorizontalAlignment = HorizontalAlignment.Center };
            var tbRemaining = new TextBlock { Text = "₹0", FontSize = 18, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(134, 239, 172)), HorizontalAlignment = HorizontalAlignment.Center };
            var col0 = StatColumn("📅 Days", tbDays, Color.FromRgb(30, 41, 59));
            var col2 = StatColumn("👥 Extra Persons", tbPersons, Color.FromRgb(30, 41, 59));
            var col4 = StatColumn("💰 Total", tbTotal, Color.FromRgb(30, 41, 59));
            var col6 = StatColumn("💳 Remaining", tbRemaining, Color.FromRgb(30, 41, 59));

            Grid.SetColumn(col0, 0); Grid.SetColumn(col2, 2);
            Grid.SetColumn(col4, 4); Grid.SetColumn(col6, 6);
            sumGrid.Children.Add(col0); sumGrid.Children.Add(col2);
            sumGrid.Children.Add(col4); sumGrid.Children.Add(col6);
            summaryCard.Child = sumGrid;
            ModalBody.Children.Add(summaryCard);

           

            // ── Auto-fill guest details when 10-digit mobile is entered ────────
            txtMobile.TextChanged += (s, e) =>
            {
                var digits = System.Text.RegularExpressions.Regex.Replace(txtMobile.Text, @"\D", "");
                if (digits.Length == 10)
                {
                    try
                    {
                        var guest = DbContext.Read<(string Name, string Aadhar, string Address, int Persons, decimal Price, string IdType)?>(conn =>
                        {
                            using var cmd = conn.CreateCommand();
                            cmd.CommandText = @"
            SELECT full_name, aadhar, address, persons, price_per_day, id_type 
            FROM guests 
            WHERE mobile = $mob 
            ORDER BY id DESC LIMIT 1";
                            cmd.Parameters.AddWithValue("$mob", digits);

                            using var r = cmd.ExecuteReader();
                            if (r.Read())
                            {
                                return (
                                    Name: r.IsDBNull(0) ? "" : r.GetString(0),
                                    Aadhar: r.IsDBNull(1) ? "" : r.GetString(1),
                                    Address: r.IsDBNull(2) ? "" : r.GetString(2),
                                    Persons: r.IsDBNull(3) ? 1 : r.GetInt32(3),
                                    Price: r.IsDBNull(4) ? 0m : r.GetDecimal(4),
                                    IdType: r.IsDBNull(5) ? "" : r.GetString(5)
                                );
                            }
                            return null;
                        });

                        if (guest.HasValue)
                        {
                            if (IsMobileAlreadyUsed(digits, out var activeName, out var activeRoom, out var activeContext))
                            {
                                SetError(txtMobile, errMobile,
                                    $"{activeName} is already {activeContext} in Room {activeRoom}.");
                                ShowToast($"❌ {activeName} is currently {activeContext} in Room {activeRoom}.", false);
                                return;
                            }

                            txtName.Text = guest.Value.Name;
                            txtAadhar.Text = guest.Value.Aadhar;
                            txtAddress.Text = guest.Value.Address;

                            if (guest.Value.Price > 0)
                                txtPrice.Text = guest.Value.Price.ToString("F0");

                            if (!string.IsNullOrWhiteSpace(guest.Value.IdType))
                            {
                                bool matched = false;
                                foreach (ComboBoxItem item in cmbIdType.Items)
                                {
                                    if (string.Equals(item.Content?.ToString(), guest.Value.IdType,
                                            StringComparison.OrdinalIgnoreCase))
                                    {
                                        cmbIdType.SelectedItem = item;
                                        matched = true;
                                        break;
                                    }
                                }
                                if (!matched)
                                    cmbIdType.SelectedIndex = 0;
                            }

                            SetError(txtName, errName, null);
                            SetError(txtAadhar, errAadhar, null);
                            SetError(null, errIdType, null);

                            ShowToast("✅ Returning guest detected! Details auto-filled.", true);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"Auto-fill error: {ex.Message}");
                    }
                }
            };

            // ── Save button ───────────────────────────────────────────────────
            var saveBtn = new Button
            {
                Content = "🧾  Check In ",
                Style = (Style)FindResource("PrimaryBtn"),
                Height = 40,
                Margin = new Thickness(0, 2, 0, 0)
            };
            ModalBody.Children.Add(saveBtn);

            // ── Helper: compute current total amount from days × price ───────
            decimal CurrentTotalAmount()
            {
                int days = 1;
                if (dpCheckOut.SelectedDate.HasValue)
                {
                    var span = (dpCheckOut.SelectedDate.Value - DateTime.Today).Days;
                    days = span > 0 ? span : 1;
                }
                decimal pricePerDay = decimal.TryParse(txtPrice.Text.Trim(), out var p) ? p : 0;
                return days * pricePerDay;


            }
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

            void Recalculate()
            {
                int days = 1;
                if (dpCheckOut.SelectedDate.HasValue)
                {
                    var span = (dpCheckOut.SelectedDate.Value - DateTime.Today).Days;
                    days = span > 0 ? span : 1;
                }

                int persons = int.TryParse(txtPersons.Text.Trim(), out var ps) ? Math.Max(0, ps) : 0;
                int extraPersons = persons;
                decimal pricePerDay = decimal.TryParse(txtPrice.Text.Trim(), out var p) ? p : 0;
                decimal extraPersonPrice = decimal.TryParse(txtExtraPersonPrice.Text.Trim(), out var ep) ? ep : 0;
                decimal advance = decimal.TryParse(txtAdvance.Text.Trim(), out var a) ? a : 0;

                decimal dailyTotal = pricePerDay + (extraPersons * extraPersonPrice);
                decimal totalAmount = days * dailyTotal;
                decimal remaining = totalAmount - advance;

                tbDays.Text = days.ToString();
                tbPersons.Text = persons.ToString();
                tbTotal.Text = $"₹{totalAmount:F0}";
                tbRemaining.Text = remaining >= 0 ? $"₹{remaining:F0}" : $"-₹{Math.Abs(remaining):F0}";
                tbRemaining.Foreground = remaining < 0
                    ? new SolidColorBrush(Color.FromRgb(252, 165, 165))
                    : new SolidColorBrush(Color.FromRgb(134, 239, 172));

               
            }

            dpCheckOut.SelectedDateChanged += (s, e) => Recalculate();
            txtPrice.TextChanged += (s, e) => Recalculate();
            txtAdvance.TextChanged += (s, e) => Recalculate();
            txtPersons.TextChanged += (s, e) => Recalculate();
            txtExtraPersonPrice.TextChanged += (s, e) => Recalculate();
            Recalculate();

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

            // ── Inline validation on lost focus ──────────────────────────────
            txtMobile.LostFocus += (s, e) =>
            {
                var vMob = ValidateMobile(txtMobile.Text.Trim());
                if (vMob != null) { SetError(txtMobile, errMobile, vMob); return; }

                var digits = System.Text.RegularExpressions.Regex.Replace(txtMobile.Text, @"\D", "");
                if (IsMobileAlreadyUsed(digits, out var eName, out var eRoom, out var eContext))
                {
                    SetError(txtMobile, errMobile,
                        $"{eName} is already {eContext} in Room {eRoom} with this number.");
                    return;
                }

                var currentName = txtName.Text.Trim();
                if (!string.IsNullOrWhiteSpace(currentName) &&
                    IsMobileBoundToDifferentGuest(digits, currentName, out var ownerName2))
                {
                    SetError(txtMobile, errMobile,
                        $"This mobile number is already registered to {ownerName2}.");
                    return;
                }

                SetError(txtMobile, errMobile, null);
            }; txtName.LostFocus += (s, e) =>
            {
                var v = ValidateName(txtName.Text.Trim());
                SetError(txtName, errName, v);
            };

            txtAadhar.LostFocus += (s, e) =>
            {
                var idType = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Aadhaar Card";
                SetError(txtAadhar, errAadhar, ValidateIdNumber(idType, txtAadhar.Text.Trim()));
            };

            txtPrice.LostFocus += (s, e) =>
            {
                var v = decimal.TryParse(txtPrice.Text.Trim(), out var pv) && pv > 0
                    ? null : "Enter a valid price.";
                SetError(txtPrice, errPrice, v);
            };
            txtAdvance.LostFocus += (s, e) =>
            {
                decimal advance = decimal.TryParse(txtAdvance.Text.Trim(), out var a) ? a : 0;
                if (advance < 0)
                {
                    SetError(txtAdvance, errAdvance, "Advance cannot be negative.");
                    return;
                }
            };
            dpCheckOut.LostFocus += (s, e) =>
            {
                var v = dpCheckOut.SelectedDate.HasValue && dpCheckOut.SelectedDate.Value > DateTime.Today
                    ? null : "Check-out must be a future date.";
                SetError(null, errCheckOut, v);
            };
            txtPersons.PreviewTextInput += (s, e) =>
                e.Handled = !e.Text.All(char.IsDigit);
            txtExtraPersonPrice.PreviewTextInput += (s, e) =>
                e.Handled = !(e.Text.All(char.IsDigit) || e.Text == ".");

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

            txtAdvance.KeyDown += (s, e) =>
            {
                if (e.Key == System.Windows.Input.Key.Enter)
                    saveBtn.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };

            // ── Save click ────────────────────────────────────────────────────
            saveBtn.Click += (s, e) =>
            {
                var name = txtName.Text.Trim();
                var idType = (cmbIdType.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Aadhaar Card";
                var aadhar = txtAadhar.Text.Trim();
                var mobile = txtMobile.Text.Trim();
                var address = txtAddress.Text.Trim();
                var checkOut = dpCheckOut.SelectedDate ?? DateTime.Today.AddDays(1);

                decimal.TryParse(txtPrice.Text.Trim(), out var priceVal);
                decimal.TryParse(txtExtraPersonPrice.Text.Trim(), out var extraPersonPriceVal);
                decimal.TryParse(txtAdvance.Text.Trim(), out var advance);
                int personsVal = int.TryParse(txtPersons.Text.Trim(), out var pv) ? Math.Max(0, pv) : 0;
                int advDays = 1;
                if (dpCheckOut.SelectedDate.HasValue)
                {
                    var span = (dpCheckOut.SelectedDate.Value - DateTime.Today).Days;
                    advDays = span > 0 ? span : 1;
                }
                int extraPersons = Math.Max(0, personsVal);
                decimal dailyTotal = priceVal + (extraPersons * extraPersonPriceVal);
                decimal totalForAdvCheck = advDays * dailyTotal;

                var vName = ValidateName(name);
                var vIdType = string.IsNullOrWhiteSpace(idType) ? "Please select an ID type." : null;
                var vAadhar = ValidateIdNumber(idType, aadhar);
                var vMobile = ValidateMobile(mobile);
                var vPrice = priceVal > 0 ? null : "Enter a valid price.";
                var vCheckOut = dpCheckOut.SelectedDate.HasValue && dpCheckOut.SelectedDate.Value > DateTime.Today
                                ? null : "Check-out must be a future date.";

                int cap = _activeRoom?.Capacity ?? 1;
               

                var vAdvance = advance < 0 ? "Advance cannot be negative." : null;

                SetError(txtName, errName, vName);
                SetError(null, errIdType, vIdType);
                SetError(txtAadhar, errAadhar, vAadhar);
                SetError(txtMobile, errMobile, vMobile);
                SetError(txtPrice, errPrice, vPrice);
                SetError(txtAdvance, errAdvance, vAdvance);
                SetError(null, errCheckOut, vCheckOut);

                if (vName != null || vIdType != null || vAadhar != null || vMobile != null ||
                    vPrice != null || vAdvance != null || vCheckOut != null)
                {
                    ShowToast("⚠️ Please fix the errors above.", false);
                    return;
                }

                var mobileDigits = System.Text.RegularExpressions.Regex.Replace(mobile, @"\D", "");
                if (IsMobileAlreadyUsed(mobileDigits, out var dupName, out var dupRoom, out var dupContext))
                {
                    SetError(txtMobile, errMobile,
                        $"{dupName} is already {dupContext} in Room {dupRoom} with this number.");
                    ShowToast($"❌ Mobile already in use by {dupName} ({dupContext}) in Room {dupRoom}.", false);
                    return;
                }

                // ── NEW: block mobile reuse across different guest identities ───────
                if (IsMobileBoundToDifferentGuest(mobileDigits, name, out var ownerName))
                {
                    SetError(txtMobile, errMobile,
                        $"This mobile number is already registered to {ownerName}. Each guest must have a unique number.");
                    ShowToast($"❌ This number belongs to {ownerName}. Use a different number.", false);
                    return;
                }

                if (_activeRoom != null && DbContext.HasConflict(
                        _activeRoom.Id, DateTime.Today, checkOut,
                        skipReservationCheck: false,
                        skipAllocationCheck: true))
                {
                    ShowToast($"❌ Room {_activeRoom.RoomNumber} has a reservation conflict on those dates.", false);
                    return;
                }

                SaveCheckIn(name, idType, aadhar, mobileDigits, address,
                 priceVal, extraPersonPriceVal,
                 personsVal, advance, DateTime.Now, checkOut);
            };
        }
        private static Border StatColumn(string label, TextBlock valueBlock, Color bgColor)
        {
            var b = new Border
            {
                Background = new SolidColorBrush(bgColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(8, 10, 8, 10)
            };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(valueBlock);
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 3, 0, 0)
            });
            b.Child = sp;
            return b;
        }
        private void BuildRoomInfoView()
        {
            if (_activeRoom == null) return;

            // ── Room summary strip ────────────────────────────────────────
            var infoGrid = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(20, 16, 20, 16),
                Margin = new Thickness(0, 0, 0, 20)
            };

            var infoRow = new Grid();
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            infoRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            static Border RoomStatBox(string label, string value)
            {
                var b = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(30, 41, 59)),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(10, 12, 10, 12)
                };
                var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                sp.Children.Add(new TextBlock
                {
                    Text = value,
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    HorizontalAlignment = HorizontalAlignment.Center
                });
                sp.Children.Add(new TextBlock
                {
                    Text = label,
                    FontSize = 11,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 3, 0, 0)
                });
                b.Child = sp;
                return b;
            }

            var b0 = RoomStatBox("🏠 Room", _activeRoom.RoomNumber);
            var b2 = RoomStatBox("🛏 Type", _activeRoom.RoomType);
            var b4 = RoomStatBox("👥 Capacity", $"{_activeRoom.Capacity} beds");
            var b6 = RoomStatBox("📊 Occupied", $"{_activeRoom.TotalPersons} / {_activeRoom.Capacity}");
            Grid.SetColumn(b0, 0); Grid.SetColumn(b2, 2);
            Grid.SetColumn(b4, 4); Grid.SetColumn(b6, 6);
            infoRow.Children.Add(b0); infoRow.Children.Add(b2);
            infoRow.Children.Add(b4); infoRow.Children.Add(b6);
            infoGrid.Child = infoRow;
            ModalBody.Children.Add(infoGrid);

            // ── Guest navigation ──────────────────────────────────────────
            var guests = _activeRoom.Guests;
            int guestIndex = 0;

            var counterLabel = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };

            var arrowRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
            arrowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            arrowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            arrowRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var prevBtn = new Button
            {
                Content = "◀  Prev",
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(241, 245, 249)),
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                Cursor = Cursors.Hand
            };

            var nextBtn = new Button
            {
                Content = "Next  ▶",
                Height = 36,
                Padding = new Thickness(16, 0, 16, 0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Background = new SolidColorBrush(Color.FromRgb(234, 88, 12)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                Cursor = Cursors.Hand
            };

            void RoundBtn(Button b, CornerRadius cr)
            {
                var bd = new FrameworkElementFactory(typeof(Border));
                bd.SetValue(Border.CornerRadiusProperty, cr);
                bd.SetBinding(Border.BackgroundProperty,
                    new System.Windows.Data.Binding("Background")
                    {
                        RelativeSource = new System.Windows.Data.RelativeSource(
                            System.Windows.Data.RelativeSourceMode.TemplatedParent)
                    });
                bd.SetBinding(Border.BorderBrushProperty,
                    new System.Windows.Data.Binding("BorderBrush")
                    {
                        RelativeSource = new System.Windows.Data.RelativeSource(
                            System.Windows.Data.RelativeSourceMode.TemplatedParent)
                    });
                bd.SetBinding(Border.BorderThicknessProperty,
                    new System.Windows.Data.Binding("BorderThickness")
                    {
                        RelativeSource = new System.Windows.Data.RelativeSource(
                            System.Windows.Data.RelativeSourceMode.TemplatedParent)
                    });
                var cp = new FrameworkElementFactory(typeof(ContentPresenter));
                cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
                cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
                cp.SetValue(ContentPresenter.MarginProperty, b.Padding);
                bd.AppendChild(cp);
                b.Template = new ControlTemplate(typeof(Button)) { VisualTree = bd };
            }
            RoundBtn(prevBtn, new CornerRadius(8));
            RoundBtn(nextBtn, new CornerRadius(8));

            Grid.SetColumn(prevBtn, 0);
            Grid.SetColumn(counterLabel, 1);
            Grid.SetColumn(nextBtn, 2);
            arrowRow.Children.Add(prevBtn);
            arrowRow.Children.Add(counterLabel);
            arrowRow.Children.Add(nextBtn);
            arrowRow.Visibility = guests.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

            ModalBody.Children.Add(new TextBlock
            {
                Text = "GUESTS",
                FontSize = 11,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 0, 10)
            });
            ModalBody.Children.Add(arrowRow);

            var guestCardHolder = new StackPanel();
            ModalBody.Children.Add(guestCardHolder);

            var grandTotalHolder = new StackPanel();
            ModalBody.Children.Add(grandTotalHolder);

            // ── Big InfoRow helper ────────────────────────────────────────
            static Grid BigInfoRow(string label, string value)
            {
                var g = new Grid { Margin = new Thickness(0, 5, 0, 5) };
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
                g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var lbl = new TextBlock
                {
                    Text = label,
                    FontSize = 14,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139))
                };
                var val = new TextBlock
                {
                    Text = value,
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    TextWrapping = TextWrapping.Wrap
                };
                Grid.SetColumn(lbl, 0);
                Grid.SetColumn(val, 1);
                g.Children.Add(lbl);
                g.Children.Add(val);
                return g;
            }

            void RenderGuest(int idx)
            {
                guestCardHolder.Children.Clear();
                grandTotalHolder.Children.Clear();

                if (guests.Count == 0) return;

                var guest = guests[idx];
                counterLabel.Text = guests.Count > 1
                    ? $"Guest {idx + 1} of {guests.Count}"
                    : "Guest Details";

                prevBtn.IsEnabled = idx > 0;
                nextBtn.IsEnabled = idx < guests.Count - 1;
                prevBtn.Opacity = idx > 0 ? 1.0 : 0.4;
                nextBtn.Opacity = idx < guests.Count - 1 ? 1.0 : 0.4;

                int days = Math.Max(1, (DateTime.Today - guest.CheckIn).Days);

                // ── FIX: Subtract 1 to get actual extra persons ─────────
                int extraPersons = Math.Max(0, guest.Persons );
                decimal dailyRate = guest.PricePerDay + (extraPersons * guest.ExtraPersonPrice);
                decimal total = days * dailyRate;

                var gCard = new Border
                {
                    Background = Brushes.White,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(20, 18, 20, 18),
                    Margin = new Thickness(0, 0, 0, 14)
                };
                var gStack = new StackPanel();

                // ── Avatar + name header ──────────────────────────────────
                var nameRow = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Margin = new Thickness(0, 0, 0, 16)
                };
                var avatar = new Border
                {
                    Width = 52,
                    Height = 52,
                    CornerRadius = new CornerRadius(26),
                    Background = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
                    Margin = new Thickness(0, 0, 14, 0),
                    VerticalAlignment = VerticalAlignment.Center
                };
                avatar.Child = new TextBlock
                {
                    Text = guest.FullName.Length > 0 ? guest.FullName[0].ToString().ToUpper() : "?",
                    FontSize = 22,
                    FontWeight = FontWeights.Bold,
                    Foreground = Brushes.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                nameRow.Children.Add(avatar);

                var nameStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
                nameStack.Children.Add(new TextBlock
                {
                    Text = guest.FullName,
                    FontSize = 18,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42))
                });
                nameStack.Children.Add(new TextBlock
                {
                    // ── FIX: Use extraPersons instead of guest.Persons ────
                    Text = $"{extraPersons} extra + 1 primary = {extraPersons + 1} total  ·  ₹{guest.PricePerDay:F0}/day",
                    FontSize = 13,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(0, 3, 0, 0)
                });
                nameRow.Children.Add(nameStack);
                gStack.Children.Add(nameRow);

                // ── Guest details ─────────────────────────────────────────
                var detailsBorder = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(10),
                    Padding = new Thickness(16, 12, 16, 12),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                var detailsStack = new StackPanel();
                detailsStack.Children.Add(BigInfoRow("🪪  ID Type", string.IsNullOrWhiteSpace(guest.IdType) ? "—" : guest.IdType));
                detailsStack.Children.Add(BigInfoRow("🔢  ID Number", string.IsNullOrWhiteSpace(guest.Aadhar) ? "—" : guest.Aadhar));
                detailsStack.Children.Add(BigInfoRow("📱  Mobile", string.IsNullOrWhiteSpace(guest.Mobile) ? "—" : guest.Mobile));
                detailsStack.Children.Add(BigInfoRow("📍  Address", string.IsNullOrWhiteSpace(guest.Address) ? "—" : guest.Address));
                detailsStack.Children.Add(BigInfoRow("📅  Check-In", guest.CheckIn.ToString("dd MMM yyyy  hh:mm tt")));
                detailsBorder.Child = detailsStack;
                gStack.Children.Add(detailsBorder);

                // ── Bill stat boxes ───────────────────────────────────────
                var billRow = new Grid { Margin = new Thickness(0, 0, 0, 14) };
                billRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                billRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                billRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                billRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                billRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                var daysBox = BillStatBox("📅 Days", days.ToString(), Color.FromRgb(219, 234, 254), Color.FromRgb(37, 99, 235));
                
                var persBox = BillStatBox("👤 Extra Persons", extraPersons.ToString(), Color.FromRgb(220, 252, 231), Color.FromRgb(22, 163, 74));
                var totalBox = BillStatBox("💵 Total", $"₹{total:F0}", Color.FromRgb(255, 237, 213), Color.FromRgb(234, 88, 12));
                Grid.SetColumn(daysBox, 0); Grid.SetColumn(persBox, 2); Grid.SetColumn(totalBox, 4);
                billRow.Children.Add(daysBox); billRow.Children.Add(persBox); billRow.Children.Add(totalBox);
                gStack.Children.Add(billRow);

                // ── Breakdown ─────────────────────────────────────────────
                var breakdown = new Border
                {
                    Background = new SolidColorBrush(Color.FromRgb(255, 247, 237)),
                    BorderBrush = new SolidColorBrush(Color.FromRgb(253, 186, 116)),
                    BorderThickness = new Thickness(1),
                    CornerRadius = new CornerRadius(8),
                    Padding = new Thickness(14, 10, 14, 10),
                    Margin = new Thickness(0, 0, 0, 16)
                };
                breakdown.Child = new TextBlock
                {
                    FontSize = 14,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(154, 52, 18)),
                    Text = extraPersons > 0
                        ? $"{days} day(s)  ×  (₹{guest.PricePerDay:F0} base + {extraPersons} extra × ₹{guest.ExtraPersonPrice:F0})  =  ₹{total:F2}"
                        : $"{days} day(s)  ×  ₹{guest.PricePerDay:F0}/day  =  ₹{total:F2}",
                    TextWrapping = TextWrapping.Wrap
                };
                gStack.Children.Add(breakdown);

                // ── Room Service Charges ──────────────────────────────────────────────
                gStack.Children.Add(new TextBlock
                {
                    Text = "ROOM SERVICE CHARGES",
                    FontSize = 11,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    Margin = new Thickness(0, 0, 0, 10)
                });

                var serviceChargesBox = new TextBox
                {
                    Text = "0",
                    FontSize = 16,
                    Height = 46,
                    Padding = new Thickness(12, 0, 12, 0),
                    VerticalContentAlignment = VerticalAlignment.Center,
                    BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                    BorderThickness = new Thickness(1.5),
                    Background = Brushes.White,
                    Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                    ToolTip = "Total room service charges in ₹",
                    Margin = new Thickness(0, 0, 0, 16)
                };
                serviceChargesBox.GotFocus += (s, e) =>
                {
                    if (serviceChargesBox.Text == "0") serviceChargesBox.Text = "";
                };
                serviceChargesBox.LostFocus += (s, e) =>
                {
                    if (string.IsNullOrWhiteSpace(serviceChargesBox.Text))
                        serviceChargesBox.Text = "0";
                };
                serviceChargesBox.PreviewTextInput += (s, e) =>
                    e.Handled = !(e.Text.All(char.IsDigit) || e.Text == ".");
                gStack.Children.Add(serviceChargesBox);
               

                // ── Check-out button ──────────────────────────────────────────────────
                var checkOutBtn = new Button
                {
                    Content = "🚪  Check Out & Generate Bill",
                    Style = (Style)FindResource("PrimaryBtn"),
                    Height = 46,
                    FontSize = 15,
                    Tag = guest
                };
                checkOutBtn.Click += (s, e) =>
                {
                    decimal serviceCharges = decimal.TryParse(serviceChargesBox.Text.Trim(), out var sc) ? sc : 0;
                    CheckOut_Click(guest, _activeRoom!, serviceCharges);
                };
                gStack.Children.Add(checkOutBtn);

                gCard.Child = gStack;
                guestCardHolder.Children.Add(gCard);

                // ── Grand total (when >1 guest) ───────────────────────────
                if (guests.Count > 1)
                {
                    int maxDays = guests.Max(g => Math.Max(1, (DateTime.Today - g.CheckIn).Days));
                    decimal grandTotal = guests.Sum(g =>
                    {
                        int gDays = Math.Max(1, (DateTime.Today - g.CheckIn).Days);
                        int gExtra = Math.Max(0, g.Persons);
                        return gDays * (g.PricePerDay + (gExtra * g.ExtraPersonPrice));
                    });
                    int totalPers = guests.Sum(g => g.Persons);

                    grandTotalHolder.Children.Add(new TextBlock
                    {
                        Text = "ROOM TOTAL",
                        FontSize = 11,
                        FontWeight = FontWeights.Bold,
                        Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                        Margin = new Thickness(0, 4, 0, 10)
                    });

                    var summaryCard = new Border
                    {
                        Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                        CornerRadius = new CornerRadius(12),
                        Padding = new Thickness(16, 14, 16, 14),
                        Margin = new Thickness(0, 0, 0, 4)
                    };
                    var sumGrid = new Grid();
                    sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                    sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                    sumGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                    var sb1 = BillStatBox("🗓 Days", maxDays.ToString(), Color.FromRgb(30, 41, 59), Color.FromRgb(148, 163, 184));
                    var sb2 = BillStatBox("👥 Extra Persons", totalPers.ToString(), Color.FromRgb(30, 41, 59), Color.FromRgb(148, 163, 184));
                    var sb3 = BillStatBox("💰 Total", $"₹{grandTotal:F0}", Color.FromRgb(234, 88, 12), Color.FromRgb(255, 237, 213));
                    Grid.SetColumn(sb1, 0); Grid.SetColumn(sb2, 2); Grid.SetColumn(sb3, 4);
                    sumGrid.Children.Add(sb1); sumGrid.Children.Add(sb2); sumGrid.Children.Add(sb3);
                    summaryCard.Child = sumGrid;
                    grandTotalHolder.Children.Add(summaryCard);
                }
            }

            prevBtn.Click += (s, e) =>
            {
                if (guestIndex > 0) { guestIndex--; RenderGuest(guestIndex); }
            };
            nextBtn.Click += (s, e) =>
            {
                if (guestIndex < guests.Count - 1) { guestIndex++; RenderGuest(guestIndex); }
            };

            RenderGuest(0);
        }
        // ── Bill stat box helper ──────────────────────────────────────────
        private static Border BillStatBox(string label, string value, Color bgColor, Color textColor)
        {
            // Derive a softer label colour by blending textColor toward bgColor
            var labelColor = Color.FromRgb(
                (byte)((textColor.R * 0.65) + (bgColor.R * 0.35)),
                (byte)((textColor.G * 0.65) + (bgColor.G * 0.35)),
                (byte)((textColor.B * 0.65) + (bgColor.B * 0.35)));

            var b = new Border
            {
                Background = new SolidColorBrush(bgColor),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(10, 10, 10, 10)
            };
            var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
            sp.Children.Add(new TextBlock
            {
                Text = value,
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(textColor),
                HorizontalAlignment = HorizontalAlignment.Center
            });
            sp.Children.Add(new TextBlock
            {
                Text = label,
                FontSize = 11,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(labelColor),
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 2, 0, 0)
            });
            b.Child = sp;
            return b;
        }

        private void SaveCheckIn(
    string name, string idType, string aadhar, string mobile,
    string address, decimal price, decimal extraPersonPrice,
    int persons, decimal advance, DateTime checkIn, DateTime checkOut)
        {
            if (_activeRoom == null) return;
            try
            {
                DbContext.WriteBoth(conn =>
                {
                    int guestId;
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO guests (full_name, aadhar, mobile, address, price_per_day, id_type, persons, extra_person_price)
VALUES ($n, $a, $m, $addr, $p, $idt, $pers, $epp);
SELECT last_insert_rowid();";
                        cmd.Parameters.AddWithValue("$n", name);
                        cmd.Parameters.AddWithValue("$a", aadhar);
                        cmd.Parameters.AddWithValue("$m", mobile);
                        cmd.Parameters.AddWithValue("$addr", address);
                        cmd.Parameters.AddWithValue("$p", price);
                        cmd.Parameters.AddWithValue("$idt", idType);
                        cmd.Parameters.AddWithValue("$pers", persons);
                        cmd.Parameters.AddWithValue("$epp", extraPersonPrice);
                        guestId = Convert.ToInt32(cmd.ExecuteScalar());
                    }



                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
INSERT INTO room_allocations
    (room_id, guest_id, check_in, check_out, status, advance_paid)
VALUES ($rid, $gid, $ci, $co, 'active', $adv)";
                        cmd.Parameters.AddWithValue("$rid", _activeRoom.Id);
                        cmd.Parameters.AddWithValue("$gid", guestId);
                        cmd.Parameters.AddWithValue("$ci", checkIn.ToString("yyyy-MM-dd HH:mm:ss"));
                        cmd.Parameters.AddWithValue("$co", checkOut.ToString("yyyy-MM-dd"));
                        cmd.Parameters.AddWithValue("$adv", advance);
                        cmd.ExecuteNonQuery();
                    }

                    UpdateRoomStatus(_activeRoom.Id, conn);
                });

                ShowToast("✅ Checked in successfully!", true);
                ModalOverlay.Visibility = Visibility.Collapsed;
                LoadAll();
            }
            catch (Exception ex)
            {
                ShowToast($"Error: {ex.Message}", false);
            }
        }
        private bool IsMobileBoundToDifferentGuest(string mobile, string currentName, out string existingName)
        {
            existingName = "";
            try
            {
                var result = DbContext.Read(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
        SELECT full_name
        FROM guests
        WHERE mobile = $mob
        ORDER BY id DESC
        LIMIT 1";
                    cmd.Parameters.AddWithValue("$mob", mobile);
                    using var rd = cmd.ExecuteReader();
                    if (rd.Read())
                        return rd.GetString(0);
                    return "";
                });

                if (!string.IsNullOrEmpty(result) &&
                    !string.Equals(result.Trim(), currentName.Trim(), StringComparison.OrdinalIgnoreCase))
                {
                    existingName = result;
                    return true;
                }
            }
            catch { }
            return false;
        }

        private bool IsMobileAlreadyUsed(string mobile, out string existingName, out string existingRoom, out string context)
        {
            existingName = "";
            existingRoom = "";
            context = "";
            try
            {
                var result = DbContext.Read(conn =>
                {
                    // Check active allocations (checked-in guests)
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
            SELECT g.full_name, r.room_number, 'checked in'
            FROM room_allocations ra
            JOIN guests g ON g.id = ra.guest_id
            JOIN rooms  r ON r.id = ra.room_id
            WHERE g.mobile = $mob AND ra.status = 'active'
            LIMIT 1";
                        cmd.Parameters.AddWithValue("$mob", mobile);
                        using var rd = cmd.ExecuteReader();
                        if (rd.Read())
                            return (rd.GetString(0), rd.GetString(1), rd.GetString(2));
                    }

                    // Check active reservations
                    using (var cmd = conn.CreateCommand())
                    {
                        cmd.CommandText = @"
            SELECT res.guest_name, r.room_number, 'reserved'
            FROM reservations res
            JOIN rooms r ON r.id = res.room_id
            WHERE res.mobile = $mob AND res.status = 'reserved'
            LIMIT 1";
                        cmd.Parameters.AddWithValue("$mob", mobile);
                        using var rd = cmd.ExecuteReader();
                        if (rd.Read())
                            return (rd.GetString(0), rd.GetString(1), rd.GetString(2));
                    }

                    return ("", "", "");
                });

                if (!string.IsNullOrEmpty(result.Item1))
                {
                    existingName = result.Item1;
                    existingRoom = result.Item2;
                    context = result.Item3;
                    return true;
                }
            }
            catch { }
            return false;
        }
        // ── Full updated CheckOut_Click method with OnConfirmPayment fix ──────────
        // Replace the entire CheckOut_Click method in RoomAllocationPage.xaml.cs

        private void CheckOut_Click(GuestModel guest, RoomModel activeRoom, decimal roomServiceCharges)
        {
            int days = Math.Max(1, (DateTime.Today - guest.CheckIn).Days);
            int extraPersonsForTotal = Math.Max(0, guest.Persons);
            decimal extraPersonCharges = days * extraPersonsForTotal * guest.ExtraPersonPrice;
            decimal total = days * guest.PricePerDay; // base only

            var capturedRoom = activeRoom;
            var capturedGuest = guest;

            decimal advancePaid = 0;
            try
            {
                advancePaid = DbContext.Read(conn =>
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText =
                        "SELECT COALESCE(advance_paid, 0) FROM room_allocations WHERE id=$aid";
                    cmd.Parameters.AddWithValue("$aid", guest.AllocId);
                    return Convert.ToDecimal(cmd.ExecuteScalar() ?? 0);
                });
            }
            catch { }

            int newBillId = 0;
            try
            {
                using var conn = DbContext.OpenPrimary();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
INSERT INTO bills
    (room_id, guest_id, alloc_id, check_in, check_out,
     days, price_per_day, total_amount, advance_paid,
     room_service_charges, extra_person_charges)
VALUES ($rid,$gid,$aid,$ci,datetime('now'),$d,$ppd,$tot,$adv,$rsc,$epc);
SELECT last_insert_rowid();";
                cmd.Parameters.AddWithValue("$rid", capturedRoom.Id);
                cmd.Parameters.AddWithValue("$gid", capturedGuest.GuestId);
                cmd.Parameters.AddWithValue("$aid", capturedGuest.AllocId);
                cmd.Parameters.AddWithValue("$ci", capturedGuest.CheckIn.ToString("yyyy-MM-dd HH:mm:ss"));
                cmd.Parameters.AddWithValue("$d", days);
                cmd.Parameters.AddWithValue("$ppd", capturedGuest.PricePerDay);
                cmd.Parameters.AddWithValue("$tot", total);
                cmd.Parameters.AddWithValue("$adv", advancePaid);
                cmd.Parameters.AddWithValue("$rsc", roomServiceCharges);
                cmd.Parameters.AddWithValue("$epc", extraPersonCharges);
                newBillId = Convert.ToInt32(cmd.ExecuteScalar());
            }
            catch (Exception ex)
            {
                ShowToast($"Could not prepare bill: {ex.Message}", false);
                return;
            }

            var billData = new BillData
            {
                BillId = newBillId,
                RoomNumber = capturedRoom.RoomNumber,
                RoomType = capturedRoom.RoomType,
                Floor = capturedRoom.Floor,
                GuestName = capturedGuest.FullName,
                IdType = capturedGuest.IdType,
                Aadhar = capturedGuest.Aadhar,
                Mobile = capturedGuest.Mobile,
                Address = capturedGuest.Address,
                Persons = capturedGuest.Persons,
                CheckIn = capturedGuest.CheckIn,
                CheckOut = DateTime.Now,
                Days = days,
                PricePerDay = capturedGuest.PricePerDay,
                AdvancePaid = advancePaid,
                RoomServiceCharges = roomServiceCharges,
                TotalAmount = total,
                ExtraPersonCharges = extraPersonCharges,
                ExtraPersonPrice = capturedGuest.ExtraPersonPrice
            };

            var printModal = new BillPrintModal(billData)
            {
                Owner = Window.GetWindow(this),

                OnConfirmPayment = () =>
                {
                    billData.CheckOut = DateTime.Now;
                    var checkOutStr = billData.CheckOut.ToString("yyyy-MM-dd HH:mm:ss");

                    DbContext.WriteBoth(conn =>
                    {
                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
UPDATE bills
SET check_out            = $co,
    total_amount         = $tot,
    extra_person_charges = $epc,
    room_service_charges = $rsc,
    balance_collected    = $bc,
    amount_returned      = $ar
WHERE id = $bid";
                            cmd.Parameters.AddWithValue("$co", checkOutStr);
                            cmd.Parameters.AddWithValue("$tot", billData.TotalAmount);
                            cmd.Parameters.AddWithValue("$epc", billData.ExtraPersonCharges);
                            cmd.Parameters.AddWithValue("$rsc", billData.RoomServiceCharges);
                            cmd.Parameters.AddWithValue("$bc", billData.BalanceCollected);
                            cmd.Parameters.AddWithValue("$ar", billData.AmountReturned);
                            cmd.Parameters.AddWithValue("$bid", billData.BillId);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = conn.CreateCommand())
                        {
                            cmd.CommandText = @"
UPDATE room_allocations
SET status = 'vacated', check_out = $co
WHERE id = $aid";
                            cmd.Parameters.AddWithValue("$co", checkOutStr);
                            cmd.Parameters.AddWithValue("$aid", capturedGuest.AllocId);
                            cmd.ExecuteNonQuery();
                        }

                        UpdateRoomStatus(capturedRoom.Id, conn);

                        using var fix = conn.CreateCommand();
                        fix.CommandText = "UPDATE guests SET persons = 1 WHERE persons IS NULL";
                        fix.ExecuteNonQuery();
                    });

                    DbContext.SyncBackup();
                }
            };

            _suppressOverlayClose = true;
            printModal.ShowDialog();
            _suppressOverlayClose = false;

            // ── If user closed without paying, delete the placeholder bill ────
            if (!printModal.WasPaid)
            {
                try
                {
                    using var conn = DbContext.OpenPrimary();
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "DELETE FROM bills WHERE id = $bid";
                    cmd.Parameters.AddWithValue("$bid", newBillId);
                    cmd.ExecuteNonQuery();
                }
                catch { }
            }
            else
            {
                LoadAll();
            }

            ModalOverlay.Visibility = Visibility.Collapsed;
            ModalBody.Children.Clear();
        }
        private void UpdateRoomStatus(int roomId, SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        UPDATE rooms SET status = CASE
            WHEN (
                SELECT COALESCE(SUM(g.persons), 0)
                FROM room_allocations ra
                JOIN guests g ON g.id = ra.guest_id
                WHERE ra.room_id = $rid AND ra.status = 'active'
            ) = 0 THEN 'available'
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
            cmd.Parameters.AddWithValue("$rid", roomId);
            cmd.ExecuteNonQuery();
        }
        private void CloseModal_Click(object sender, RoutedEventArgs e)
        {
            if (!_suppressOverlayClose)
                ModalOverlay.Visibility = Visibility.Collapsed;
        }

        private void Overlay_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (!_suppressOverlayClose)
                ModalOverlay.Visibility = Visibility.Collapsed;
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

        // ── UI helpers ────────────────────────────────────────────────────
        private static TextBlock SectionLabel(string text) => new()
        {
            Text = text,
            FontSize = 18,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Color.FromRgb(27, 94, 32)),
            Margin = new Thickness(0, 6, 0, 10)
        };

        private static TextBlock FieldLabel(string text) => new()
        {
            Text = text,
            FontSize = 15,
            FontWeight = FontWeights.Bold,
            Foreground = new SolidColorBrush(Colors.Black),
            Margin = new Thickness(0, 0, 0, 4)
        };

        private TextBox Field(string name, string placeholder) => new()
        {
            Name = name,
            Style = (Style)FindResource("FormField"),
            Margin = new Thickness(0, 0, 0, 12),
            ToolTip = placeholder
        };

        private static Grid InfoRow(string label, string value)
        {
            var g = new Grid { Margin = new Thickness(0, 3, 0, 3) };
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
            g.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var lbl = new TextBlock
            {
                Text = label,
                FontSize = 12,
                Foreground = new SolidColorBrush(Colors.Black),
            };
            var val = new TextBlock
            {
                Text = value,
                FontSize = 12,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                TextWrapping = TextWrapping.Wrap
            };
            Grid.SetColumn(lbl, 0);
            Grid.SetColumn(val, 1);
            g.Children.Add(lbl);
            g.Children.Add(val);
            return g;
        }
    }

    // ── Extension helper ──────────────────────────────────────────────────
    internal static class WpfExt
    {
        internal static T Also<T>(this T self, Action<T> action) { action(self); return self; }
    }
} 