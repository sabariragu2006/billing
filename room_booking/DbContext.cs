#nullable enable
using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace room_booking
{
    public static class DbContext
    {
        // ── Connection helpers ────────────────────────────────────────────────

        public static SqliteConnection OpenPrimary()
        {
            var conn = new SqliteConnection(DatabaseInitializer.PrimaryConnStr);
            conn.Open();
            return conn;
        }

        public static SqliteConnection OpenBackup()
        {
            var conn = new SqliteConnection(DatabaseInitializer.BackupConnStr);
            conn.Open();
            return conn;
        }

        // ── Read (primary → backup fallback) ─────────────────────────────────

        public static T Read<T>(Func<SqliteConnection, T> func)
        {
            try
            {
                using var conn = OpenPrimary();
                return func(conn);
            }
            catch (Exception primaryEx)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[PRIMARY READ FAILED] Falling back. {primaryEx.Message}");
                try
                {
                    using var conn = OpenBackup();
                    return func(conn);
                }
                catch (Exception backupEx)
                {
                    throw new Exception(
                        $"Database Read Failed!\n\n" +
                        $"PRIMARY ERROR: {primaryEx.Message}\n\n" +
                        $"BACKUP ERROR:  {backupEx.Message}",
                        backupEx);
                }
            }
        }

        // ── Write (primary + backup) ──────────────────────────────────────────

        public static void WriteBoth(Action<SqliteConnection> action)
        {
            using (var conn = OpenPrimary())
                action(conn);

            try
            {
                using var conn = OpenBackup();
                action(conn);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[BACKUP WRITE FAILED] {ex.Message}");
            }
        }

        // ── Conflict detection ────────────────────────────────────────────────

        /// <summary>
        /// Returns true if the room is already allocated or reserved for the
        /// given date range (excluding the specified reservation id if editing).
        /// </summary>
        public static bool HasConflict(
            int roomId,
            DateTime checkIn,
            DateTime checkOut,
            int incomingPersons = 1,
            int excludeReservationId = 0,
            bool skipReservationCheck = false,
            bool skipAllocationCheck = false)
        {
            return Read(conn =>
            {
                // ── 1. Active allocation overlap ──────────────────────────────
                if (!skipAllocationCheck)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT COUNT(*)
                        FROM room_allocations ra
                        WHERE ra.room_id = $rid
                          AND ra.status  = 'active'
                          AND ra.check_out IS NOT NULL
                          AND $checkIn  < ra.check_out
                          AND $checkOut > ra.check_in";
                    cmd.Parameters.AddWithValue("$rid", roomId);
                    cmd.Parameters.AddWithValue("$checkIn", checkIn.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$checkOut", checkOut.ToString("yyyy-MM-dd"));

                    if (Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0)
                        return true;
                }

                // ── 2. Reservation overlap ────────────────────────────────────
                if (!skipReservationCheck)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        SELECT COUNT(*) FROM reservations
                        WHERE room_id  = $rid
                          AND status   = 'reserved'
                          AND id      != $excludeId
                          AND $checkIn  < check_out
                          AND $checkOut > check_in";
                    cmd.Parameters.AddWithValue("$rid", roomId);
                    cmd.Parameters.AddWithValue("$excludeId", excludeReservationId);
                    cmd.Parameters.AddWithValue("$checkIn", checkIn.ToString("yyyy-MM-dd"));
                    cmd.Parameters.AddWithValue("$checkOut", checkOut.ToString("yyyy-MM-dd"));

                    if (Convert.ToInt32(cmd.ExecuteScalar() ?? 0) > 0)
                        return true;
                }

                return false;
            });
        }

        // ── Occupancy helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Total persons currently occupying the room via active allocations
        /// that overlap the given date range.
        /// </summary>
        public static int GetOccupiedPersons(int roomId, DateTime checkIn, DateTime checkOut)
        {
            return Read(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
                    SELECT COALESCE(SUM(g.persons), 0)
                    FROM room_allocations ra
                    JOIN guests g ON g.id = ra.guest_id
                    WHERE ra.room_id = $rid
                      AND ra.status  = 'active'
                      AND $checkIn   < COALESCE(ra.check_out, '9999-12-31')
                      AND $checkOut  > ra.check_in";
                cmd.Parameters.AddWithValue("$rid", roomId);
                cmd.Parameters.AddWithValue("$checkIn", checkIn.ToString("yyyy-MM-dd"));
                cmd.Parameters.AddWithValue("$checkOut", checkOut.ToString("yyyy-MM-dd"));
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            });
        }

        /// <summary>Returns the bed capacity of a room.</summary>
        public static int GetRoomCapacity(int roomId)
        {
            return Read(conn =>
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT capacity FROM rooms WHERE id = $rid";
                cmd.Parameters.AddWithValue("$rid", roomId);
                return Convert.ToInt32(cmd.ExecuteScalar() ?? 0);
            });
        }

        // ── Backup sync ───────────────────────────────────────────────────────

        /// <summary>
        /// File-copies the primary DB over the backup DB.
        /// Call after any operation that should be fully durable on the D: drive.
        /// </summary>
        public static void SyncBackup()
        {
            try
            {
                var src = DatabaseInitializer.PrimaryConnStr.Replace("Data Source=", "");
                var dest = DatabaseInitializer.BackupConnStr.Replace("Data Source=", "");

                var destDir = Path.GetDirectoryName(dest)!;
                if (!Directory.Exists(destDir))
                    Directory.CreateDirectory(destDir);

                if (File.Exists(src))
                {
                    File.Copy(src, dest, overwrite: true);
                    System.Diagnostics.Debug.WriteLine("[DB] Backup synced.");
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[SYNC ERROR] {ex.Message}");
            }
        }
    }
}