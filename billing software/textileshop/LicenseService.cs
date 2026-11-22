#nullable enable
using System;
using System.Text;

namespace textileshop.Services
{
    public static class LicenseService
    {
        private static readonly int _licenseDurationDays = 365;

        // Lazily compute expiry from DB on first access
        private static DateTime? _expiryDate = null;

        public static DateTime ExpiryDate
        {
            get
            {
                if (_expiryDate == null)
                {
                    DateTime firstRun = DatabaseHelper.GetFirstRunDate();
                    _expiryDate = firstRun.AddDays(_licenseDurationDays);
                }
                return _expiryDate.Value;
            }
        }

        public static DateTime FirstRunDate =>
            DatabaseHelper.GetFirstRunDate();

        public static bool IsExpired =>
            DateTime.Now.Date > ExpiryDate.Date;

        public static int DaysRemaining =>
            Math.Max(0, (ExpiryDate.Date - DateTime.Now.Date).Days);

        public static bool IsNearExpiry =>
            !IsExpired &&
            DaysRemaining <= 30 &&
            DaysRemaining > 0;

        public static string GetDbExportInfo()
        {
            string dbPath = DatabaseHelper.GetDatabasePath();
            var sb = new StringBuilder();
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine("   TEXTILE SHOP — DATA HANDOFF INFO");
            sb.AppendLine("═══════════════════════════════════════════════════");
            sb.AppendLine($"  First activated : {FirstRunDate:dd MMM yyyy}");
            sb.AppendLine($"  License expired : {ExpiryDate:dd MMM yyyy}");
            sb.AppendLine($"  Generated on    : {DateTime.Now:dd MMM yyyy HH:mm}");
            sb.AppendLine();
            sb.AppendLine("  Your database file is located at:");
            sb.AppendLine($"  {dbPath}");
            sb.AppendLine();
            sb.AppendLine("  Copy this file and send it to us with your");
            sb.AppendLine("  new application package. All your products,");
            sb.AppendLine("  bills, customers, suppliers and import history");
            sb.AppendLine("  will be automatically loaded into the new app.");
            sb.AppendLine("═══════════════════════════════════════════════════");
            return sb.ToString();
        }
    }
}