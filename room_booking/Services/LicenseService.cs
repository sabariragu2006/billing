#nullable enable
using System;
using Microsoft.Data.Sqlite;

namespace room_booking.Services
{
    public static class LicenseService
    {
        private static readonly int _licenseDurationDays = 400;
        private const string SettingKey = "license_first_run";

        private static DateTime? _firstRunDate = null;

        public static DateTime FirstRunDate
        {
            get
            {
                if (_firstRunDate == null)
                    _firstRunDate = GetOrCreateFirstRunDate();
                return _firstRunDate.Value;
            }
        }

        public static DateTime ExpiryDate => FirstRunDate.AddDays(_licenseDurationDays);
        public static bool IsExpired => DateTime.Now.Date > ExpiryDate.Date;
        public static int DaysRemaining => Math.Max(0, (ExpiryDate.Date - DateTime.Now.Date).Days);
        public static bool IsNearExpiry => !IsExpired && DaysRemaining <= 30 && DaysRemaining > 0;

        public static string GetDatabasePath() => DatabaseInitializer.PrimaryConnStr
            .Replace("Data Source=", "");

        private static DateTime GetOrCreateFirstRunDate()
        {
            try
            {
                // 1. Try to read from PRIMARY db first
                var fromPrimary = TryRead(DatabaseInitializer.PrimaryConnStr);
                var fromBackup = TryRead(DatabaseInitializer.BackupConnStr);

                // 2. If either DB has a value, use the EARLIEST one
                //    (prevents resetting by deleting/replacing one copy)
                DateTime? earliest = null;
                if (fromPrimary.HasValue) earliest = fromPrimary;
                if (fromBackup.HasValue && (earliest == null || fromBackup < earliest))
                    earliest = fromBackup;

                if (earliest.HasValue)
                {
                    // Make sure BOTH databases agree on this date
                    WriteToDb(DatabaseInitializer.PrimaryConnStr, earliest.Value);
                    WriteToDb(DatabaseInitializer.BackupConnStr, earliest.Value);
                    return earliest.Value;
                }

                // 3. Neither DB has a record — first run. Write to both.
                var now = DateTime.Now;
                WriteToDb(DatabaseInitializer.PrimaryConnStr, now);
                WriteToDb(DatabaseInitializer.BackupConnStr, now);
                return now;
            }
            catch
            {
                return DateTime.Now;
            }
        }

        private static DateTime? TryRead(string connStr)
        {
            try
            {
                using var conn = new SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT value FROM app_settings WHERE key = $k";
                cmd.Parameters.AddWithValue("$k", SettingKey);
                var result = cmd.ExecuteScalar();
                if (result != null && DateTime.TryParse(result.ToString(), out var dt))
                    return dt;
            }
            catch { }
            return null;
        }

        private static void WriteToDb(string connStr, DateTime date)
        {
            try
            {
                using var conn = new SqliteConnection(connStr);
                conn.Open();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    INSERT INTO app_settings (key, value) VALUES ($k, $v)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value
                        WHERE app_settings.value > excluded.value;";
                // ↑ Only overwrite if the new value is EARLIER (smaller ISO string),
                //   so a later "first run" on one DB can't override an earlier date.
                cmd.Parameters.AddWithValue("$k", SettingKey);
                cmd.Parameters.AddWithValue("$v", date.ToString("o"));
                cmd.ExecuteNonQuery();
            }
            catch { }
        }
    }
}