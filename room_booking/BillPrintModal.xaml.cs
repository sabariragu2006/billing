#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace room_booking
{
    public class BillData
    {
        public int BillId { get; set; }

        // ── Settable instead of always-recomputed ─────────────────────────
        // For a LIVE checkout (new bill, not yet persisted), the caller
        // should leave these at their default and let CheckOut_Click /
        // OnConfirmPayment compute + store them for the first time.
        // For a REPRINT of an already-settled bill, the caller should pass
        // in the values that were actually recorded in the bills table at
        // checkout time (bills.balance_collected / bills.amount_returned),
        // so the modal reflects history rather than recomputing live
        // (which can drift if charges or advance were stored differently
        // than what was actually paid).
        public decimal BalanceCollected { get; set; } = 0;
        public decimal AmountReturned { get; set; } = 0;

        public string RoomNumber { get; set; } = "";
        public string RoomType { get; set; } = "";
        public int Floor { get; set; }
        public string GuestName { get; set; } = "";
        public string IdType { get; set; } = "";
        public string Aadhar { get; set; } = "";
        public string Mobile { get; set; } = "";
        public string Address { get; set; } = "";
        public int Persons { get; set; } = 1;
        public DateTime CheckIn { get; set; }
        public DateTime CheckOut { get; set; }
        public int Days { get; set; }
        public decimal PricePerDay { get; set; }
        public decimal TotalAmount { get; set; }

        // The REAL advance the guest paid at check-in. Never inflate this
        // to GrandTotal for display purposes — it must stay accurate so the
        // bill correctly shows advance vs. balance vs. total.
        public decimal AdvancePaid { get; set; }

        public decimal RoomServiceCharges { get; set; } = 0;
        public decimal ExtraPersonCharges { get; set; } = 0;
        public decimal CgstRate { get; set; } = 2.5m;
        public decimal SgstRate { get; set; } = 2.5m;
        public decimal ExtraPersonPrice { get; set; } = 0;
        public decimal SubTotal => TotalAmount + ExtraPersonCharges + RoomServiceCharges;
        public decimal CgstAmount => Math.Round(SubTotal * CgstRate / 100, 2);
        public decimal SgstAmount => Math.Round(SubTotal * SgstRate / 100, 2);
        public decimal GrandTotal => SubTotal + CgstAmount + SgstAmount;
        public decimal BalanceDue => GrandTotal - AdvancePaid;
        public decimal BalanceDueDisplay => BalanceDue > 0 ? BalanceDue : 0;
    }

    public partial class BillPrintModal : Window
    {
        private readonly BillData _bill;
        private bool _isPaid = false;

        // True when reprinting an already-settled bill from history (e.g.
        // the Bills page "View" button). In that case the bill is always
        // treated as fully paid for display purposes, regardless of how
        // GrandTotal - AdvancePaid happens to compute right now.
        private readonly bool _isHistorical;

        public bool WasPaid { get; private set; } = false;
        public Action? OnConfirmPayment { get; set; }

        public BillPrintModal(BillData bill, bool showMarkPaid = true, bool isHistorical = false)
        {
            InitializeComponent();
            _bill = bill;
            _isHistorical = isHistorical;
            Loaded += (s, e) =>
            {
                PopulateUI();
                if (!showMarkPaid)
                {
                    BtnMarkPaid.Visibility = Visibility.Collapsed;
                    BtnPrintOnly.Visibility = Visibility.Visible;
                }
            };
        }

        private void PopulateUI()
        {
            // ── Header ────────────────────────────────────────────────────
            TxtBillNo.Text = $"# BILL-{_bill.BillId:D5}";
            TxtRoomInfo.Text = $"Room {_bill.RoomNumber}";
            TxtCheckOutDate.Text = _bill.CheckOut.ToString("dd MMM yyyy");
            TxtCheckInFull.Text = _bill.CheckIn.ToString("dd MMM yyyy  hh:mm tt");
            TxtCheckOutFull.Text = _bill.CheckOut.ToString("dd MMM yyyy  hh:mm tt");
            TxtGeneratedTime.Text = DateTime.Now.ToString("dd MMM yyyy\nhh:mm tt");

            // ── Guest details ─────────────────────────────────────────────
            TxtGuestInitial.Text = _bill.GuestName.Length > 0
                ? _bill.GuestName[0].ToString().ToUpper() : "?";
            TxtGuestName.Text = _bill.GuestName;

            TxtIdTypeLabel.Text = string.IsNullOrWhiteSpace(_bill.IdType) ? "Govt Id" : _bill.IdType;
            TxtAadhar.Text = string.IsNullOrWhiteSpace(_bill.Aadhar) ? "—" : _bill.Aadhar;
            TxtMobile.Text = string.IsNullOrWhiteSpace(_bill.Mobile) ? "—" : _bill.Mobile;
            TxtAddress.Text = string.IsNullOrWhiteSpace(_bill.Address) ? "—" : _bill.Address;
            TxtPersons.Text = $"{_bill.Persons} person(s)";

            // ── Stay table ────────────────────────────────────────────────
            TxtRoomDesc.Text = $"Room {_bill.RoomNumber} — {_bill.RoomType}";
            TxtRoomSubDesc.Text = $"₹{_bill.PricePerDay:F0} / night";
            TxtCheckIn.Text = _bill.CheckIn.ToString("dd MMM yy  hh:mm tt");
            TxtNights.Text = _bill.Days.ToString();
            TxtLineAmount.Text = $"₹{_bill.TotalAmount:F2}";

            // ── Extra person charges ──────────────────────────────────────
            TxtExtraPersonTotal.Text = $"₹{_bill.ExtraPersonCharges:F2}";

            // ── Room service charges ──────────────────────────────────────
            TxtServiceTotal.Text = $"₹{_bill.RoomServiceCharges:F2}";

            // ── Totals ────────────────────────────────────────────────────
            TxtCgst.Text = $"₹{_bill.CgstAmount:F2}";
            TxtSgst.Text = $"₹{_bill.SgstAmount:F2}";
            TxtGrandTotal.Text = $"₹{_bill.GrandTotal:F2}";

            // Advance shown is always the REAL advance paid at check-in.
            TxtAdvance.Text = $"- ₹{_bill.AdvancePaid:F2}";

            // ── Balance collected / amount returned ───────────────────────
            // For a live checkout these come from BillData.BalanceCollected/
            // AmountReturned as set by CheckOut_Click before this modal is
            // shown. For a historical reprint, BillsPage.PrintBill_Click has
            // already populated these from the stored bills.balance_collected
            // / bills.amount_returned columns.

            TxtAmountReturned.Text = $"₹{_bill.AmountReturned:F2}";
            AmountReturnedRow.Visibility = Visibility.Visible;

            // Also refresh balance collected with the final computed value
            TxtBalanceCollected.Text = $"₹{_bill.BalanceCollected:F2}";

            RefreshBalanceBox();

            // When the advance already covers the full bill (live checkout
            // case), OR this is a historical reprint of an already-settled
            // bill, show the paid stamp. For the live case we deliberately
            // keep _isPaid = false so MarkPaid_Click still triggers the
            // actual checkout/payment confirmation when clicked.
            if (_bill.BalanceDue <= 0 || _isHistorical)
            {
                _isPaid = false;
                WasPaid = false;
                PaidStamp.Visibility = Visibility.Visible;
                TxtPaidOn.Text = _isHistorical
                    ? "Paid in full at checkout"
                    : "Advance covers full amount";
                RefreshBalanceBox();
            }
        }

        private void RefreshBalanceBox()
        {
            if (_isPaid || _isHistorical || _bill.BalanceDue <= 0)
            {
                TxtBalanceDue.Text = "₹0.00";  // ← Always zero when paid/settled
                TxtBalanceDue.FontSize = 20;
                TxtBalanceDue.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74)); // ← Add green color here too
                BalanceBox.Background = new SolidColorBrush(Color.FromRgb(240, 253, 244));
                TxtBalanceNote.Text = _bill.AmountReturned > 0
                    ? $"₹{_bill.AmountReturned:F2} returned to guest"
                    : "Fully settled";

                if (BalanceBox.Child is System.Windows.Controls.Grid g)
                    if (g.Children[0] is StackPanel sp && sp.Children[0] is TextBlock lbl)
                        lbl.Foreground = new SolidColorBrush(Color.FromRgb(22, 163, 74));
            }
            else
            {
                TxtBalanceDue.Text = $"₹{_bill.BalanceDue:F2}";
                TxtBalanceDue.FontSize = 26;
                TxtBalanceDue.Foreground = new SolidColorBrush(Color.FromRgb(234, 88, 12));
                TxtBalanceNote.Text = _bill.AdvancePaid > 0
                    ? $"Advance: ₹{_bill.AdvancePaid:F0}  |  Collected: ₹{_bill.BalanceCollected:F0}"
                    : "No advance collected";
            }
        }

        private void MarkPaid_Click(object sender, RoutedEventArgs e)
        {
            if (_isPaid) { DoPrint(); return; }

            var result = MessageBox.Show(
                $"Confirm payment and check out?\n\nBill #{_bill.BillId:D5}\nGuest: {_bill.GuestName}\nRoom: {_bill.RoomNumber}\nTotal (incl. tax): ₹{_bill.GrandTotal:F2}",
                "Confirm Payment & Check Out",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes) return;

            // Capture exact payment time BEFORE invoking
            _bill.CheckOut = DateTime.Now;

            // Compute the actual collected/returned amounts for THIS live
            // checkout, based on the real advance vs. grand total, and store
            // them on the bill object so PopulateUI/RefreshBalanceBox and
            // the DB write (OnConfirmPayment) both see consistent values.
            _bill.BalanceCollected = _bill.BalanceDue > 0 ? _bill.BalanceDue : 0;
            _bill.AmountReturned = _bill.BalanceDue < 0 ? Math.Abs(_bill.BalanceDue) : 0;

            // Execute the pending DB writes (bill update + vacate allocation)
            // OnConfirmPayment already writes: total_amount, extra_person_charges,
            // room_service_charges, balance_collected, amount_returned, check_out.
            // DO NOT run any additional UPDATE here — it would overwrite those values.
            try
            {
                OnConfirmPayment?.Invoke();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Check-out failed:\n{ex.Message}",
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Refresh checkout time on bill UI before printing
            TxtCheckOutDate.Text = _bill.CheckOut.ToString("dd MMM yyyy");
            TxtCheckOutFull.Text = _bill.CheckOut.ToString("dd MMM yyyy  hh:mm tt");

            ApplyPaidState();
            DoPrint();
        }

        // ── A4 Print (single page, scaled to fit) ─────────────────────────
        private void DoPrint()
        {
            var dlg = new PrintDialog();
            if (dlg.ShowDialog() != true) return;

            try
            {
                const double A4W = 793.7;
                const double A4H = 1122.5;
                const double margin = 36.0;
                double contentW = A4W - margin * 2;
                double contentH = A4H - margin * 2;

                dlg.PrintTicket.PageMediaSize =
                    new System.Printing.PageMediaSize(
                        System.Printing.PageMediaSizeName.ISOA4, A4W, A4H);

                var scrollViewer = (ScrollViewer)PrintArea.Parent;
                scrollViewer.Content = null;

                var wrapper = new Border
                {
                    Width = contentW,
                    Background = Brushes.White,
                    Child = PrintArea
                };

                wrapper.Measure(new Size(contentW, double.PositiveInfinity));
                wrapper.Arrange(new Rect(0, 0, contentW, wrapper.DesiredSize.Height));
                wrapper.UpdateLayout();

                double totalH = wrapper.ActualHeight;
                double scale = Math.Min(1.0, contentH / totalH);
                double renderW = contentW * scale;
                double renderH = totalH * scale;

                var rtb = new RenderTargetBitmap(
                    (int)Math.Ceiling(contentW),
                    (int)Math.Ceiling(totalH),
                    96, 96,
                    PixelFormats.Pbgra32);

                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawRectangle(new VisualBrush(wrapper)
                    {
                        Stretch = Stretch.None,
                        AlignmentX = AlignmentX.Left,
                        AlignmentY = AlignmentY.Top
                    }, null, new Rect(0, 0, contentW, totalH));
                }
                rtb.Render(dv);

                var img = new System.Windows.Controls.Image
                {
                    Source = rtb,
                    Width = renderW,
                    Height = renderH
                };

                var fp = new FixedPage
                {
                    Width = A4W,
                    Height = A4H,
                    Background = Brushes.White
                };

                double left = margin + (contentW - renderW) / 2;
                FixedPage.SetLeft(img, left);
                FixedPage.SetTop(img, margin);
                fp.Children.Add(img);

                fp.Measure(new Size(A4W, A4H));
                fp.Arrange(new Rect(0, 0, A4W, A4H));
                fp.UpdateLayout();

                var fd = new FixedDocument();
                fd.DocumentPaginator.PageSize = new Size(A4W, A4H);

                var pc = new PageContent();
                ((System.Windows.Markup.IAddChild)pc).AddChild(fp);
                fd.Pages.Add(pc);

                wrapper.Child = null;
                scrollViewer.Content = PrintArea;
                PrintArea.Width = double.NaN;
                PrintArea.InvalidateMeasure();
                PrintArea.UpdateLayout();

                dlg.PrintDocument(
                    fd.DocumentPaginator,
                    $"Bill-{_bill.BillId:D5}  {_bill.GuestName}  Room {_bill.RoomNumber}");

                MessageBox.Show(
                    $"✅ Bill #{_bill.BillId:D5} sent to printer.",
                    "Print Queued",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                try
                {
                    if (PrintArea.Parent == null)
                    {
                        var sv = (ScrollViewer)((Grid)((Grid)Content).Children[0]).Children[0];
                        sv.Content = PrintArea;
                    }
                }
                catch { }
                MessageBox.Show($"Print failed:\n{ex.Message}",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void PrintOnly_Click(object sender, RoutedEventArgs e) => DoPrint();
        private void Print_Click(object sender, RoutedEventArgs e) => DoPrint();

        private void ApplyPaidState()
        {
            _isPaid = true;
            WasPaid = true;
            PaidStamp.Visibility = Visibility.Visible;
            TxtPaidOn.Text = $"Paid on {DateTime.Now:dd MMM yyyy  hh:mm tt}";
            BtnMarkPaid.IsEnabled = false;
            BtnMarkPaid.Opacity = 0.45;

            TxtBalanceCollected.Text = $"₹{_bill.BalanceCollected:F2}";

            TxtAmountReturned.Text = $"₹{_bill.AmountReturned:F2}";
            AmountReturnedRow.Visibility = Visibility.Visible;

            RefreshBalanceBox();
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
} 