#nullable enable
using System.Windows;
using textileshop.Services;

namespace textileshop
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Must be set FIRST before anything else opens
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DatabaseHelper.InitializeDatabase();

            // ── Fully expired — hard block ─────────────────────────────────
            if (LicenseService.IsExpired)
            {
                new LicenseExpiredWindow(LicenseWindowMode.Expired).ShowDialog();
                Shutdown();
                return;
            }

            // ── Expiring soon — warn then continue ─────────────────────────
            if (LicenseService.IsNearExpiry)
            {
                var warning = new LicenseWarningWindow();
                warning.ShowDialog();

                if (!warning.UserChoseToContinue)
                {
                    Shutdown();
                    return;
                }
                // UserChoseToContinue == true → falls through to MainWindow
            }

            // ── Open main window and hand control back to WPF ─────────────
            var main = new MainWindow();
            ShutdownMode = ShutdownMode.OnLastWindowClose;
            main.Show();
        }
    }
}