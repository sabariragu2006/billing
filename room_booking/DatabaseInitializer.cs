#nullable enable
using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace room_booking
{
    public static class DatabaseInitializer
    {
        private static readonly string AppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "KMV", "room_booking.db");

        private static readonly string BackupPath = Path.Combine(
            "D:\\KMV", "room_booking.db");

        public static string PrimaryConnStr => $"Data Source={AppDataPath}";
        public static string BackupConnStr => $"Data Source={BackupPath}";

        public static void Initialize()
        {
            InitializeSingle(AppDataPath, "PRIMARY (AppData)");
            InitializeSingle(BackupPath, "BACKUP (D: Drive)");
        }

        public static void MigrateSchema()
        {
            Run(AppDataPath, "PRIMARY");
            Run(BackupPath, "BACKUP");

            static void Run(string dbPath, string label)
            {
                try
                {
                    using var conn = new SqliteConnection($"Data Source={dbPath}");
                    conn.Open();

                    var migrations = new[]
                    {
                        ("guests",           "persons",              "INTEGER NOT NULL DEFAULT 1"),
                        ("guests",           "price_per_day",        "REAL NOT NULL DEFAULT 0"),
                        ("guests",           "id_type",              "TEXT NOT NULL DEFAULT ''"),
                        ("guests",           "extra_person_price",   "REAL NOT NULL DEFAULT 0"),
                        ("room_allocations", "advance_paid",         "REAL NOT NULL DEFAULT 0"),
                        ("room_allocations", "check_out",            "TEXT"),
                        ("rooms",            "room_type",            "TEXT NOT NULL DEFAULT 'Standard'"),
                        ("rooms",            "floor",                "INTEGER NOT NULL DEFAULT 1"),
                        ("bills",            "alloc_id",             "INTEGER"),
                        ("bills",            "advance_paid",         "REAL NOT NULL DEFAULT 0"),
                        ("bills",            "room_service_charges", "REAL NOT NULL DEFAULT 0"),
                        ("bills",            "extra_person_charges", "REAL NOT NULL DEFAULT 0"), // ← new
                        ("bills",            "balance_collected",    "REAL NOT NULL DEFAULT 0"),
                        ("bills",            "amount_returned",      "REAL NOT NULL DEFAULT 0"),
                        ("reservations",     "id_type",              "TEXT NOT NULL DEFAULT ''"),
                        ("bills", "checkout_time", "TEXT"),
                    };

                    foreach (var (table, column, definition) in migrations)
                    {
                        using (var check = conn.CreateCommand())
                        {
                            check.CommandText =
                                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t";
                            check.Parameters.AddWithValue("$t", table);
                            if (Convert.ToInt32(check.ExecuteScalar()) == 0) continue;
                        }

                        if (!ColumnExists(conn, table, column))
                        {
                            using var alter = conn.CreateCommand();
                            alter.CommandText =
                                $"ALTER TABLE {table} ADD COLUMN {column} {definition}";
                            alter.ExecuteNonQuery();
                            System.Diagnostics.Debug.WriteLine(
                                $"[MIGRATE:{label}] Added {table}.{column}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[MIGRATE ERROR:{label}] {dbPath}: {ex.Message}");
                }
            }
        }

        private static void InitializeSingle(string dbPath, string label)
        {
            try
            {
                var dir = Path.GetDirectoryName(dbPath)!;
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                using var conn = new SqliteConnection($"Data Source={dbPath}");
                conn.Open();
                ApplySchema(conn);
                System.Diagnostics.Debug.WriteLine($"[DB] {label} ready → {dbPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] {label}: {ex.Message}");
            }
        }

        private static void ApplySchema(SqliteConnection conn)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                PRAGMA journal_mode = WAL;
                PRAGMA foreign_keys = ON;

                CREATE TABLE IF NOT EXISTS rooms (
                    id          INTEGER PRIMARY KEY AUTOINCREMENT,
                    room_number TEXT    NOT NULL UNIQUE,
                    floor       INTEGER NOT NULL DEFAULT 1,
                    capacity    INTEGER NOT NULL DEFAULT 2,
                    room_type   TEXT    NOT NULL DEFAULT 'Standard',
                    status      TEXT    NOT NULL DEFAULT 'available',
                    created_at  TEXT    DEFAULT (datetime('now'))
                );

                CREATE TABLE IF NOT EXISTS guests (
                    id                 INTEGER PRIMARY KEY AUTOINCREMENT,
                    full_name          TEXT    NOT NULL,
                    aadhar             TEXT,
                    mobile             TEXT,
                    address            TEXT,
                    persons            INTEGER NOT NULL DEFAULT 1,
                    price_per_day      REAL    NOT NULL DEFAULT 0,
                    id_type            TEXT    NOT NULL DEFAULT '',
                    extra_person_price REAL    NOT NULL DEFAULT 0,
                    created_at         TEXT    DEFAULT (datetime('now'))
                );

                CREATE TABLE IF NOT EXISTS room_allocations (
                    id           INTEGER PRIMARY KEY AUTOINCREMENT,
                    room_id      INTEGER NOT NULL,
                    guest_id     INTEGER NOT NULL,
                    check_in     TEXT    NOT NULL,
                    check_out    TEXT,
                    status       TEXT    NOT NULL DEFAULT 'active',
                    advance_paid REAL    NOT NULL DEFAULT 0,
                    allocated_at TEXT    DEFAULT (datetime('now')),
                    FOREIGN KEY (room_id)  REFERENCES rooms(id),
                    FOREIGN KEY (guest_id) REFERENCES guests(id)
                );

                CREATE TABLE IF NOT EXISTS bills (
                    id                   INTEGER PRIMARY KEY AUTOINCREMENT,
                    room_id              INTEGER,
                    guest_id             INTEGER,
                    alloc_id             INTEGER,
                    check_in             TEXT,
                    check_out            TEXT,
                    days                 INTEGER,
                    price_per_day        REAL,
                    total_amount         REAL,
                    advance_paid         REAL    NOT NULL DEFAULT 0,
                    balance_collected    REAL    NOT NULL DEFAULT 0,
                    amount_returned      REAL    NOT NULL DEFAULT 0,
                    room_service_charges REAL    NOT NULL DEFAULT 0,
                    extra_person_charges REAL    NOT NULL DEFAULT 0,
                    created_at           TEXT    DEFAULT (datetime('now')),
checkout_time       TEXT,
                    FOREIGN KEY (room_id)  REFERENCES rooms(id),
                    FOREIGN KEY (guest_id) REFERENCES guests(id)
                );

                CREATE TABLE IF NOT EXISTS reservations (
                    id            INTEGER PRIMARY KEY AUTOINCREMENT,
                    room_id       INTEGER NOT NULL,
                    guest_name    TEXT    NOT NULL,
                    id_type       TEXT    NOT NULL DEFAULT '',
                    aadhar        TEXT,
                    mobile        TEXT,
                    address       TEXT,
                    persons       INTEGER NOT NULL DEFAULT 1,
                    check_in      TEXT    NOT NULL,
                    check_out     TEXT    NOT NULL,
                    price_per_day REAL    NOT NULL DEFAULT 0,
                    advance_paid  REAL    NOT NULL DEFAULT 0,
                    status        TEXT    NOT NULL DEFAULT 'reserved',
                    created_at    TEXT    DEFAULT (datetime('now')),
                    FOREIGN KEY (room_id) REFERENCES rooms(id)
                );

                CREATE TABLE IF NOT EXISTS app_settings (
                    key   TEXT PRIMARY KEY,
                    value TEXT NOT NULL
                );

                CREATE INDEX IF NOT EXISTS idx_alloc_room   ON room_allocations(room_id);
                CREATE INDEX IF NOT EXISTS idx_alloc_guest  ON room_allocations(guest_id);
                CREATE INDEX IF NOT EXISTS idx_alloc_status ON room_allocations(status);
                CREATE INDEX IF NOT EXISTS idx_bills_guest  ON bills(guest_id);
                CREATE INDEX IF NOT EXISTS idx_res_room     ON reservations(room_id);
                CREATE INDEX IF NOT EXISTS idx_res_status   ON reservations(status);
            ";
            cmd.ExecuteNonQuery();
            MigrateIfNeeded(conn);
        }

        private static void MigrateIfNeeded(SqliteConnection conn)
        {
            var columnMigrations = new[]
            {
                ("guests",           "persons",              "ALTER TABLE guests ADD COLUMN persons INTEGER NOT NULL DEFAULT 1"),
                ("guests",           "price_per_day",        "ALTER TABLE guests ADD COLUMN price_per_day REAL NOT NULL DEFAULT 0"),
                ("guests",           "id_type",              "ALTER TABLE guests ADD COLUMN id_type TEXT NOT NULL DEFAULT ''"),
                ("guests",           "extra_person_price",   "ALTER TABLE guests ADD COLUMN extra_person_price REAL NOT NULL DEFAULT 0"),
                ("room_allocations", "advance_paid",         "ALTER TABLE room_allocations ADD COLUMN advance_paid REAL NOT NULL DEFAULT 0"),
                ("room_allocations", "check_out",            "ALTER TABLE room_allocations ADD COLUMN check_out TEXT"),
                ("bills",            "alloc_id",             "ALTER TABLE bills ADD COLUMN alloc_id INTEGER"),
                ("bills",            "advance_paid",         "ALTER TABLE bills ADD COLUMN advance_paid REAL NOT NULL DEFAULT 0"),
                ("bills",            "balance_collected",    "ALTER TABLE bills ADD COLUMN balance_collected REAL NOT NULL DEFAULT 0"),
                ("bills",            "amount_returned",      "ALTER TABLE bills ADD COLUMN amount_returned REAL NOT NULL DEFAULT 0"),
                ("bills",            "room_service_charges", "ALTER TABLE bills ADD COLUMN room_service_charges REAL NOT NULL DEFAULT 0"),
                ("bills",            "extra_person_charges", "ALTER TABLE bills ADD COLUMN extra_person_charges REAL NOT NULL DEFAULT 0"), // ← new
                ("reservations",     "id_type",              "ALTER TABLE reservations ADD COLUMN id_type TEXT NOT NULL DEFAULT ''"),
                ("bills",            "checkout_time",        "ALTER TABLE bills ADD COLUMN checkout_time TEXT"),
            };

            foreach (var (table, column, sql) in columnMigrations)
            {
                if (!TableExists(conn, table)) continue;
                if (!ColumnExists(conn, table, column))
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                    System.Diagnostics.Debug.WriteLine($"[DB MIGRATE] Added {table}.{column}");
                }
            }
        }

        private static bool TableExists(SqliteConnection conn, string table)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText =
                "SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name=$t";
            cmd.Parameters.AddWithValue("$t", table);
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }

        private static bool ColumnExists(SqliteConnection conn, string table, string column)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({table});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                if (reader.GetString(1).Equals(column, StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
} 