#nullable enable
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using textileshop.Services;

namespace textileshop
{
    public enum LicenseWindowMode { Expired, Warning }

    public partial class LicenseExpiredWindow : Window
    {
        private readonly LicenseWindowMode _mode;
        public bool UserChoseToContinue { get; private set; } = false;

        public LicenseExpiredWindow()
            : this(LicenseWindowMode.Expired) { }

        public LicenseExpiredWindow(LicenseWindowMode mode)
        {
            InitializeComponent();
            _mode = mode;

            TxtDbPath.Text = DatabaseHelper.GetDatabasePath();
            TxtActivatedDate.Text = LicenseService.FirstRunDate.ToString("dd MMM yyyy");
            TxtExpiryDate.Text = LicenseService.ExpiryDate.ToString("dd MMM yyyy");

            if (mode == LicenseWindowMode.Warning)
                ApplyWarningMode();
            else
                ApplyExpiredMode();

            this.Loaded += (_, _) => StartPulse();

            if (mode == LicenseWindowMode.Expired)
                this.Closing += BlockClose;
        }

        // ── Expired ───────────────────────────────────────────────────────
        private void ApplyExpiredMode()
        {
            HeroBanner.Background = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));

            TxtIcon.Text = "🔒";
            TxtTitle.Text = "License Expired";

            StatusChip.Background = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            TxtStatusChip.Text = "● ACCESS BLOCKED";
            StatusChip.Visibility = Visibility.Visible;
            BadgePanel.Visibility = Visibility.Collapsed;

            TxtDescription.Text =
                $"Your Textile Shop license expired on {LicenseService.ExpiryDate:dd MMM yyyy}.\n" +
                "Please contact us to get a new version and restore access.";

            // Timeline — red expired state
            ExpiryIcon.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xE2, 0xE2));
            TxtExpiryIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26));
            TxtExpiryIcon.Text = "✕";
            TxtExpiryLabel.Text = "EXPIRED";

            // Info box — purple
            InfoBox.Background = new SolidColorBrush(Color.FromRgb(0xF5, 0xF3, 0xFF));
            InfoBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xDD, 0xD6, 0xFE));
            TxtInfoTitle.Foreground = new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED));
            InfoIconBadge.Background = new SolidColorBrush(Color.FromRgb(0xED, 0xE9, 0xFE));
            TxtInfoSub.Text =
                "Send this database file with your new app package.\n" +
                "All products, bills, suppliers and history will carry over automatically.";

            // Buttons — close only
            BtnContinue.Visibility = Visibility.Collapsed;
            SpacerCol.Width = new GridLength(0);
            ContinueCol.Width = new GridLength(0);
        }

        // ── Warning ───────────────────────────────────────────────────────
        private void ApplyWarningMode()
        {
            HeroBanner.Background = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));

            TxtIcon.Text = "⚠️";
            TxtTitle.Text = "License Expiring Soon";

            int days = LicenseService.DaysRemaining;
            BadgePanel.Visibility = Visibility.Visible;
            TxtDaysLeft.Text =
                $"⏳  {days} day{(days == 1 ? "" : "s")} remaining";
            StatusChip.Visibility = Visibility.Collapsed;

            TxtDescription.Text =
                $"Your license expires on {LicenseService.ExpiryDate:dd MMM yyyy}.\n" +
                "Renew now to avoid losing access to Textile Shop.";

            // Timeline — amber warning state
            ExpiryIcon.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
            TxtExpiryIcon.Foreground = new SolidColorBrush(Color.FromRgb(0xD9, 0x77, 0x06));
            TxtExpiryIcon.Text = "!";
            TxtExpiryLabel.Text = "EXPIRES";

            // Info box — amber
            InfoBox.Background = new SolidColorBrush(Color.FromRgb(0xFF, 0xFB, 0xEB));
            InfoBox.BorderBrush = new SolidColorBrush(Color.FromRgb(0xFD, 0xE6, 0x8A));
            TxtInfoTitle.Foreground = new SolidColorBrush(Color.FromRgb(0x92, 0x40, 0x0E));
            InfoIconBadge.Background = new SolidColorBrush(Color.FromRgb(0xFE, 0xF3, 0xC7));
            TxtInfoSub.Text =
                "Contact us with this database path when renewing.\n" +
                "All your data will be carried over automatically.";

            // Buttons — both visible
            BtnContinue.Visibility = Visibility.Visible;
            SpacerCol.Width = new GridLength(12);
            ContinueCol.Width = new GridLength(1, GridUnitType.Star);
        }

        // ── Pulse animation on the outer ring ────────────────────────────
        private void StartPulse()
        {
            var scaleX = new ScaleTransform(1, 1);
            OuterRing.RenderTransform = scaleX;
            OuterRing.RenderTransformOrigin = new Point(0.5, 0.5);

            var grow = new DoubleAnimation(1.0, 1.25,
                TimeSpan.FromSeconds(1.1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            var fade = new DoubleAnimation(0.25, 0.0,
                TimeSpan.FromSeconds(1.1))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            scaleX.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            scaleX.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
            OuterRing.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // ── Copy DB path ─────────────────────────────────────────────────
        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtDbPath.Text);
                if (sender is System.Windows.Controls.Button btn)
                    btn.Content = "✓ Copied";

                var timer = new System.Windows.Threading.DispatcherTimer
                {
                    Interval = TimeSpan.FromSeconds(2)
                };
                timer.Tick += (_, _) =>
                {
                    if (sender is System.Windows.Controls.Button b)
                        b.Content = "Copy";
                    timer.Stop();
                };
                timer.Start();
            }
            catch { /* clipboard unavailable */ }
        }

        // ── Close handlers ────────────────────────────────────────────────
        private void BlockClose(object? sender, CancelEventArgs e) => e.Cancel = true;

        private void ForceClose()
        {
            this.Closing -= BlockClose;
            Application.Current.Shutdown();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => ForceClose();
        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            UserChoseToContinue = true;
            this.Close();
        }
    }
} 