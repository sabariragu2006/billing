#nullable enable

using System;
using System.Windows;
using System.Windows.Threading;
using room_booking.Services;

namespace room_booking
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            DatabaseInitializer.Initialize();
            DatabaseInitializer.MigrateSchema();


            // Prevent WPF from shutting down when the warning window closes
            ShutdownMode = ShutdownMode.OnExplicitShutdown;

            DispatcherUnhandledException += App_DispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

            // License validation
            if (!CheckLicense())
            {
                Shutdown();
                return;
            }

            try
            {
                MainWindow = new MainWindow();
                MainWindow.Show();

                // Restore normal behavior
                ShutdownMode = ShutdownMode.OnMainWindowClose;
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Failed to open Main Window.\n\n{ex}",
                    "Startup Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                Shutdown();
            }
        }

        private bool CheckLicense()
        {
            // License expired
            if (LicenseService.IsExpired)
            {
                MessageBox.Show(
                    "Your license for Room Manager has expired.\n\n" +
                    "Please contact support to renew your license.",
                    "License Expired",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                return false;
            }

            // Warning period
            if (LicenseService.IsNearExpiry)
            {
                var warningWindow = new LicenseWarningWindow();

                warningWindow.ShowDialog();

                return warningWindow.UserChoseToContinue;
            }

            return true;
        }

        private void App_DispatcherUnhandledException(
            object sender,
            DispatcherUnhandledExceptionEventArgs e)
        {
            MessageBox.Show(
                $"UI Exception\n\n{e.Exception}",
                "Application Error",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            e.Handled = true;
        }

        private void CurrentDomain_UnhandledException(
            object? sender,
            UnhandledExceptionEventArgs e)
        {
            if (e.ExceptionObject is Exception ex)
            {
                MessageBox.Show(
                    $"Unhandled Exception\n\n{ex}",
                    "Application Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}