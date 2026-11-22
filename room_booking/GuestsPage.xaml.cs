#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace room_booking
{
    public partial class GuestsPage : Page
    {
        private List<GuestRecord> _all = new();
        private List<GuestRecord> _filtered = new();
        private string _filter = "all";

        // ── Pagination ────────────────────────────────────────────────────
        private const int PageSize = 10;
        private int _currentPage = 1;
        private int TotalPages => _filtered.Count == 0
            ? 1
            : (int)Math.Ceiling(_filtered.Count / (double)PageSize);

        public GuestsPage()
        {
            InitializeComponent();
            Loaded += (s, e) => LoadGuests();
        }

        // ── Load ──────────────────────────────────────────────────────────
        private void LoadGuests()
        {
            _all = DbContext.Read(conn =>
            {
                var list = new List<GuestRecord>();
                using var cmd = conn.CreateCommand();
                // ── FIX: Added g.persons and g.extra_person_price to the SELECT query ──
                cmd.CommandText = @"
                    SELECT
                        g.id,
                        g.full_name,
                        g.aadhar,
                        g.mobile,
                        g.address,
                        g.price_per_day,
                        g.persons,
                        g.extra_person_price,
                        ra.check_in,
                        ra.check_out,
                        ra.status,
                        r.room_number,
                        r.room_type,
                        r.floor
                    FROM guests g
                    LEFT JOIN room_allocations ra
                        ON ra.guest_id = g.id
                        AND ra.id = (
                            SELECT id FROM room_allocations
                            WHERE guest_id = g.id
                            ORDER BY id DESC LIMIT 1
                        )
                    LEFT JOIN rooms r ON r.id = ra.room_id
                    ORDER BY g.id DESC";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new GuestRecord
                    {
                        GuestId = reader.GetInt32(0),
                        FullName = reader.GetString(1),
                        Aadhar = reader.IsDBNull(2) ? "" : reader.GetString(2),
                        Mobile = reader.IsDBNull(3) ? "" : reader.GetString(3),
                        Address = reader.IsDBNull(4) ? "" : reader.GetString(4),
                        PricePerDay = reader.IsDBNull(5) ? 0 : reader.GetDecimal(5),
                        // ── FIX: Map the new columns (indices shifted by 2) ──
                        Persons = reader.IsDBNull(6) ? 0 : reader.GetInt32(6),
                        ExtraPersonPrice = reader.IsDBNull(7) ? 0 : reader.GetDecimal(7),
                        CheckIn = reader.IsDBNull(8) ? null : reader.GetString(8),
                        CheckOut = reader.IsDBNull(9) ? null : reader.GetString(9),
                        AllocStatus = reader.IsDBNull(10) ? "" : reader.GetString(10),
                        RoomNumber = reader.IsDBNull(11) ? "" : reader.GetString(11),
                        RoomType = reader.IsDBNull(12) ? "" : reader.GetString(12),
                        Floor = reader.IsDBNull(13) ? 0 : reader.GetInt32(13),
                    });
                }
                return list;
            });

            _currentPage = 1;
            _filter = "all";
            SetFilterButtons("all"); // ensure "All" button shows active styling on first load
            ApplyFilter();
        }

        // ── Filter / search ───────────────────────────────────────────────
        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            _currentPage = 1;
            ApplyFilter();
        }

        private void BtnFilterAll_Click(object sender, RoutedEventArgs e)
        {
            _filter = "all";
            _currentPage = 1;
            SetFilterButtons("all");
            ApplyFilter();
        }

        private void BtnFilterActive_Click(object sender, RoutedEventArgs e)
        {
            _filter = "active";
            _currentPage = 1;
            SetFilterButtons("active");
            ApplyFilter();
        }

        private void BtnFilterOut_Click(object sender, RoutedEventArgs e)
        {
            _filter = "vacated";
            _currentPage = 1;
            SetFilterButtons("vacated");
            ApplyFilter();
        }

        private void SetFilterButtons(string active)
        {
            ResetBtn(BtnFilterAll);
            ResetBtn(BtnFilterActive);
            ResetBtn(BtnFilterOut);

            switch (active)
            {
                case "all": ActivateBtn(BtnFilterAll, isCheckout: false); break;
                case "active": ActivateBtn(BtnFilterActive, isCheckout: false); break;
                case "vacated": ActivateBtn(BtnFilterOut, isCheckout: true); break;
            }
        }

        private void ResetBtn(Button btn)
        {
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F1F8E9"));
            btn.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#2E7D32"));
            btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A5D6A7"));
        }

        private void ActivateBtn(Button btn, bool isCheckout)
        {
            string bg = isCheckout ? "#1565C0" : "#2E7D32";
            btn.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
            btn.Foreground = new SolidColorBrush(Colors.White);
            btn.BorderBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(bg));
        }

        private void ApplyFilter()
        {
            var q = TxtSearch.Text.Trim().ToLower();
            var result = _all.AsEnumerable();

            if (_filter == "active")
                result = result.Where(g => g.AllocStatus == "active");
            else if (_filter == "vacated")
                result = result.Where(g => g.AllocStatus == "vacated");

            if (!string.IsNullOrEmpty(q))
                result = result.Where(g =>
                    g.FullName.ToLower().Contains(q) ||
                    g.Mobile.Contains(q) ||
                    g.Aadhar.Contains(q));

            _filtered = result.ToList();
            RenderCurrentPage();
        }

        // ── Pagination helpers ────────────────────────────────────────────
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

            Render(page);

            // Update pagination UI
            TxtPageInfo.Text = $"Page {_currentPage} of {TotalPages}";
            TxtPageCount.Text = $"({_filtered.Count} total)";
            BtnPrev.IsEnabled = _currentPage > 1;
            BtnNext.IsEnabled = _currentPage < TotalPages;
        }

        // ── Render compact rows ───────────────────────────────────────────
        private void Render(List<GuestRecord> guests)
        {
            GuestListPanel.Children.Clear();

            if (_filtered.Count == 0)
            {
                GuestListPanel.Children.Add(new TextBlock
                {
                    Text = "No guests found.",
                    FontSize = 15,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    Margin = new Thickness(0, 40, 0, 0)
                });
                return;
            }

            foreach (var g in guests)
                GuestListPanel.Children.Add(BuildRow(g));
        }

        // ── Compact row (name + badge + amount) ───────────────────────────
        private UIElement BuildRow(GuestRecord g)
        {
            bool isActive = g.AllocStatus == "active";

            int days = 0;
            if (g.CheckIn != null)
            {
                var ci = DateTime.TryParse(g.CheckIn, out var d) ? d : DateTime.Today;
                var co = (g.CheckOut != null && DateTime.TryParse(g.CheckOut, out var d2)) ? d2 : DateTime.Today;
                days = Math.Max(1, (co - ci).Days);
            }

            // ── FIX: Calculate effective daily rate including extra persons ──
            int extraPersons = Math.Max(0, g.Persons);
            decimal dailyRate = g.PricePerDay + (extraPersons * g.ExtraPersonPrice);
            decimal total = days * dailyRate;

            // Clickable row
            var row = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(226, 232, 240)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(16, 14, 16, 14),
                Cursor = System.Windows.Input.Cursors.Hand,
                Tag = g
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // Avatar
            var avatar = new Border
            {
                Width = 42,
                Height = 42,
                CornerRadius = new CornerRadius(21),
                Background = new SolidColorBrush(isActive
                    ? Color.FromRgb(13, 148, 136)
                    : Color.FromRgb(148, 163, 184)),
                Margin = new Thickness(0, 0, 14, 0),
                VerticalAlignment = VerticalAlignment.Center
            };
            avatar.Child = new TextBlock
            {
                Text = g.FullName.Length > 0 ? g.FullName[0].ToString().ToUpper() : "?",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(avatar, 0);

            // Name + status badge
            var nameCol = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
            nameRow.Children.Add(new TextBlock
            {
                Text = g.FullName,
                FontSize = 16,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Margin = new Thickness(0, 0, 10, 0),
                VerticalAlignment = VerticalAlignment.Center
            });

            var (pillBg, pillFg, pillText) = isActive
                ? (Color.FromRgb(220, 252, 231), Color.FromRgb(22, 163, 74), "● Active")
                : g.AllocStatus == "vacated"
                    ? (Color.FromRgb(226, 232, 240), Color.FromRgb(100, 116, 139), "✓ Out")
                    : (Color.FromRgb(254, 226, 226), Color.FromRgb(220, 38, 38), "—");

            nameRow.Children.Add(new Border
            {
                Background = new SolidColorBrush(pillBg),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(8, 3, 8, 3),
                Child = new TextBlock
                {
                    Text = pillText,
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(pillFg)
                }
            });
            nameCol.Children.Add(nameRow);

            nameCol.Children.Add(new TextBlock
            {
                Text = !string.IsNullOrEmpty(g.RoomNumber)
                    ? $"Room {g.RoomNumber}  ·  {g.RoomType}"
                    : "No active booking",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 4, 0, 0)
            });

            // Mobile number, shown directly in the row for visibility
            if (!string.IsNullOrEmpty(g.Mobile))
            {
                nameCol.Children.Add(new TextBlock
                {
                    Text = $"📱 {g.Mobile}",
                    FontSize = 13,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(13, 148, 136)),
                    Margin = new Thickness(0, 3, 0, 0)
                });
            }

            Grid.SetColumn(nameCol, 1);

            // Amount (right side)
            var amtCol = new StackPanel
            {
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center
            };
            amtCol.Children.Add(new TextBlock
            {
                Text = g.CheckIn != null ? $"₹{total:F0}" : "—",
                FontSize = 17,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12)),
                HorizontalAlignment = HorizontalAlignment.Right
            });
            if (days > 0)
            {
                amtCol.Children.Add(new TextBlock
                {
                    // ── FIX: Show the effective daily rate if there are extra persons ──
                    Text = extraPersons > 0
                        ? $"{days}d × ₹{dailyRate:F0}"
                        : $"{days}d × ₹{g.PricePerDay:F0}",
                    FontSize = 12,
                    FontWeight = FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(Color.FromRgb(100, 116, 139)),
                    HorizontalAlignment = HorizontalAlignment.Right
                });
            }
            Grid.SetColumn(amtCol, 2);

            grid.Children.Add(avatar);
            grid.Children.Add(nameCol);
            grid.Children.Add(amtCol);
            row.Child = grid;

            row.MouseEnter += (s, e) => row.Background = new SolidColorBrush(Color.FromRgb(255, 247, 237));
            row.MouseLeave += (s, e) => row.Background = Brushes.White;
            row.MouseLeftButtonUp += (s, e) => OpenModal(g);

            return row;
        }

        // ── Modal ─────────────────────────────────────────────────────────
        private void OpenModal(GuestRecord g)
        {
            bool isActive = g.AllocStatus == "active";
            int days = 0;
            if (g.CheckIn != null)
            {
                var ci = DateTime.TryParse(g.CheckIn, out var d) ? d : DateTime.Today;
                var co = (g.CheckOut != null && DateTime.TryParse(g.CheckOut, out var d2)) ? d2 : DateTime.Today;
                days = Math.Max(1, (co - ci).Days);
            }

            // ── FIX: Calculate effective daily rate including extra persons ──
            int extraPersons = Math.Max(0, g.Persons);
            decimal dailyRate = g.PricePerDay + (extraPersons * g.ExtraPersonPrice);
            decimal total = days * dailyRate;

            var win = new Window
            {
                Title = $"Guest — {g.FullName}",
                Width = 480,
                SizeToContent = SizeToContent.Height,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Window.GetWindow(this),
                ResizeMode = ResizeMode.NoResize,
                Background = new SolidColorBrush(Color.FromRgb(248, 250, 252)),
                FontFamily = new FontFamily("Segoe UI")
            };

            var root = new StackPanel { Margin = new Thickness(0) };

            // Header bar
            var header = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Padding = new Thickness(24, 18, 24, 18)
            };
            var hRow = new Grid();
            hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            hRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var hAvatar = new Border
            {
                Width = 50,
                Height = 50,
                CornerRadius = new CornerRadius(25),
                Background = new SolidColorBrush(isActive
                    ? Color.FromRgb(13, 148, 136)
                    : Color.FromRgb(100, 116, 139)),
                Margin = new Thickness(0, 0, 16, 0)
            };
            hAvatar.Child = new TextBlock
            {
                Text = g.FullName.Length > 0 ? g.FullName[0].ToString().ToUpper() : "?",
                FontSize = 22,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(hAvatar, 0);

            var hInfo = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            hInfo.Children.Add(new TextBlock
            {
                Text = g.FullName,
                FontSize = 19,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White
            });

            var (pillBg, pillFg, pillText) = isActive
                ? (Color.FromRgb(220, 252, 231), Color.FromRgb(22, 163, 74), "● Active")
                : g.AllocStatus == "vacated"
                    ? (Color.FromRgb(226, 232, 240), Color.FromRgb(100, 116, 139), "✓ Checked Out")
                    : (Color.FromRgb(254, 226, 226), Color.FromRgb(220, 38, 38), "— No Booking");

            hInfo.Children.Add(new Border
            {
                Background = new SolidColorBrush(pillBg),
                CornerRadius = new CornerRadius(999),
                Padding = new Thickness(9, 3, 9, 3),
                Margin = new Thickness(0, 6, 0, 0),
                HorizontalAlignment = HorizontalAlignment.Left,
                Child = new TextBlock
                {
                    Text = pillText,
                    FontSize = 13,
                    FontWeight = FontWeights.Bold,
                    Foreground = new SolidColorBrush(pillFg)
                }
            });
            Grid.SetColumn(hInfo, 1);

            hRow.Children.Add(hAvatar);
            hRow.Children.Add(hInfo);
            header.Child = hRow;
            root.Children.Add(header);

            // Body
            var body = new StackPanel { Margin = new Thickness(24, 20, 24, 24) };

            void Section(string title) => body.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 12,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)),
                Margin = new Thickness(0, 18, 0, 10)
            });

            void Detail(string label, string value)
            {
                var r = new Grid { Margin = new Thickness(0, 0, 0, 12) };
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(140) });
                r.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var lbl = new TextBlock { Text = label, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)) };
                var val = new TextBlock { Text = value, FontSize = 16, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(15, 23, 42)), TextWrapping = TextWrapping.Wrap };
                Grid.SetColumn(lbl, 0); Grid.SetColumn(val, 1);
                r.Children.Add(lbl); r.Children.Add(val);
                body.Children.Add(r);
            }

            Section("PERSONAL DETAILS");
            Detail("📱  Mobile", string.IsNullOrEmpty(g.Mobile) ? "—" : g.Mobile);
            Detail("🪪  ID", string.IsNullOrEmpty(g.Aadhar) ? "—" : g.Aadhar);
            Detail("📍  Address", string.IsNullOrEmpty(g.Address) ? "—" : g.Address);

            // ── FIX: Show breakdown of rates if there are extra persons ──
            Detail("💰  Base Rate", $"₹{g.PricePerDay:F0} / day");
            if (extraPersons > 0)
            {
                Detail("👥  Extra Persons", $"{extraPersons} × ₹{g.ExtraPersonPrice:F0} / day");
                Detail("💵  Effective Rate", $"₹{dailyRate:F0} / day");
            }

            if (g.CheckIn != null)
            {
                Section("BOOKING DETAILS");
                Detail("🛏  Room", !string.IsNullOrEmpty(g.RoomNumber) ? $"{g.RoomNumber}  ·  {g.RoomType}" : "—");
                Detail("🪜  Floor", g.Floor > 0 ? g.Floor.ToString() : "—");
                Detail("📅  Check-In", DateTime.TryParse(g.CheckIn, out var ci) ? ci.ToString("dd MMM yyyy") : g.CheckIn);
                Detail("📅  Check-Out", g.CheckOut != null && DateTime.TryParse(g.CheckOut, out var co) ? co.ToString("dd MMM yyyy") : (isActive ? "Still Staying" : "—"));
                Detail("⏱  Days", $"{days} day(s)");

                Section("BILL SUMMARY");
                var statGrid = new Grid { Margin = new Thickness(0) };
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
                statGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

                UIElement StatBox(string lbl, string val, Color bg, Color fg)
                {
                    var b = new Border { Background = new SolidColorBrush(bg), CornerRadius = new CornerRadius(8), Padding = new Thickness(10) };
                    var sp = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center };
                    sp.Children.Add(new TextBlock { Text = val, FontSize = 20, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(fg), HorizontalAlignment = HorizontalAlignment.Center });
                    sp.Children.Add(new TextBlock { Text = lbl, FontSize = 12, FontWeight = FontWeights.Bold, Foreground = new SolidColorBrush(Color.FromRgb(71, 85, 105)), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) });
                    b.Child = sp;
                    return b;
                }

                var b1 = StatBox("Days", days.ToString(), Color.FromRgb(219, 234, 254), Color.FromRgb(37, 99, 235));
                // ── FIX: Show the effective daily rate in the stat box ──
                var b2 = StatBox("Rate", $"₹{dailyRate:F0}", Color.FromRgb(220, 252, 231), Color.FromRgb(22, 163, 74));
                var b3 = StatBox("Total", $"₹{total:F0}", Color.FromRgb(255, 237, 213), Color.FromRgb(234, 88, 12));
                Grid.SetColumn(b1, 0); Grid.SetColumn(b2, 2); Grid.SetColumn(b3, 4);
                statGrid.Children.Add(b1); statGrid.Children.Add(b2); statGrid.Children.Add(b3);
                body.Children.Add(statGrid);
            }

            var closeBtn = new Button
            {
                Content = "Close",
                Height = 44,
                Margin = new Thickness(0, 24, 0, 0),
                Background = new SolidColorBrush(Color.FromRgb(15, 23, 42)),
                Foreground = Brushes.White,
                FontSize = 15,
                FontWeight = FontWeights.Bold,
                Cursor = System.Windows.Input.Cursors.Hand,
                BorderThickness = new Thickness(0)
            };
            var closeTpl = new ControlTemplate(typeof(Button));
            var closeBd = new FrameworkElementFactory(typeof(Border));
            closeBd.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = new System.Windows.Data.RelativeSource(System.Windows.Data.RelativeSourceMode.TemplatedParent) });
            closeBd.SetValue(Border.CornerRadiusProperty, new CornerRadius(10));
            var closeCP = new FrameworkElementFactory(typeof(ContentPresenter));
            closeCP.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            closeCP.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            closeBd.AppendChild(closeCP);
            closeTpl.VisualTree = closeBd;
            closeBtn.Template = closeTpl;
            closeBtn.Click += (s, e) => win.Close();
            body.Children.Add(closeBtn);

            root.Children.Add(body);
            win.Content = root;
            win.ShowDialog();
        }
    }

    // ── Model ─────────────────────────────────────────────────────────────
    public class GuestRecord
    {
        public int GuestId { get; set; }
        public string FullName { get; set; } = "";
        public string Aadhar { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Address { get; set; } = "";
        public decimal PricePerDay { get; set; }
        // ── FIX: Added properties to hold extra person data ──
        public int Persons { get; set; }
        public decimal ExtraPersonPrice { get; set; }
        public string? CheckIn { get; set; }
        public string? CheckOut { get; set; }
        public string AllocStatus { get; set; } = "";
        public string RoomNumber { get; set; } = "";
        public string RoomType { get; set; } = "";
        public int Floor { get; set; }
    }
}