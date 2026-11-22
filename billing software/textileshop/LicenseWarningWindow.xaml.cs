#nullable enable
using System;
using System.Windows;
using System.Windows.Media.Animation;
using textileshop.Services;

namespace textileshop
{
    public partial class LicenseWarningWindow : Window
    {
        public bool UserChoseToContinue { get; private set; } = false;

        public LicenseWarningWindow()
        {
            InitializeComponent();

            int days = LicenseService.DaysRemaining;
            int totalDays = 365;
            int usedDays = totalDays - days;
            double usedPct = Math.Clamp((double)usedDays / totalDays * 100, 0, 100);

            TxtCountdown.Text = $"{days} day{(days == 1 ? "" : "s")} remaining until expiry";
            TxtDescription.Text =
                $"Your Textile Shop license expires on {LicenseService.ExpiryDate:dd MMM yyyy}.\n" +
                "Renew now to avoid losing access to all features.";
            TxtActivatedDate.Text = LicenseService.FirstRunDate.ToString("dd MMM yyyy");
            TxtExpiryDate.Text = LicenseService.ExpiryDate.ToString("dd MMM yyyy");
            TxtUsagePct.Text = $"{usedPct:F0}% used";
            TxtStartLabel.Text = LicenseService.FirstRunDate.ToString("dd MMM yyyy");
            TxtEndLabel.Text = LicenseService.ExpiryDate.ToString("dd MMM yyyy");
            TxtDbPath.Text = DatabaseHelper.GetDatabasePath();

            this.Loaded += OnLoaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            StartPulse();
            AnimateProgressBar();
        }

        private void StartPulse()
        {
            var scale = new System.Windows.Media.ScaleTransform(1, 1);
            OuterRing.RenderTransform = scale;
            OuterRing.RenderTransformOrigin = new Point(0.5, 0.5);

            var grow = new DoubleAnimation(1.0, 1.28, TimeSpan.FromSeconds(1.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };
            var fade = new DoubleAnimation(0.20, 0.0, TimeSpan.FromSeconds(1.2))
            {
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            };

            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, grow);
            scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, grow);
            OuterRing.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void AnimateProgressBar()
        {
            ProgressFill.Loaded += (_, _) =>
            {
                double trackWidth =
                    ((System.Windows.Controls.Border)ProgressFill.Parent).ActualWidth;

                int days = LicenseService.DaysRemaining;
                int totalDays = 365;
                double pct = Math.Clamp((double)(totalDays - days) / totalDays, 0, 1);
                double target = trackWidth * pct;

                var anim = new DoubleAnimation(0, target, TimeSpan.FromSeconds(1.0))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                ProgressFill.BeginAnimation(
                    System.Windows.FrameworkElement.WidthProperty, anim);
            };
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(TxtDbPath.Text);
                if (sender is System.Windows.Controls.Button btn)
                    btn.Content = "✓ Done";

                var timer = new System.Windows.Threading.DispatcherTimer
                { Interval = TimeSpan.FromSeconds(2) };
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

        private void BtnContinue_Click(object sender, RoutedEventArgs e)
        {
            UserChoseToContinue = true;
            this.Close();
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            UserChoseToContinue = false;
            this.Close();
        }
    }
} 