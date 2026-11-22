#nullable enable
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using Microsoft.Data.Sqlite;
using Newtonsoft.Json;
using textileshop.Models;

namespace textileshop.Services
{
    /// <summary>
    /// Helper class for SQLite database operations — fully offline, no server needed
    /// </summary>
    public static class DatabaseHelper
    {
        private static string _dbPath = string.Empty;

        // ✅ Get database path (creates file if not exists)
        private static string GetDbPath()
        {
            if (!string.IsNullOrEmpty(_dbPath)) return _dbPath;

            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string dbFolder = Path.Combine(appData, "TextileShop");
            Directory.CreateDirectory(dbFolder);

            _dbPath = Path.Combine(dbFolder, "textile_shop.db");
            return _dbPath;
        }

        // ✅ Initialize database schema (call once at app startup)
        public static void InitializeDatabase()
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            // Products table (inside InitializeDatabase())
            var productsCmd = conn.CreateCommand();
            productsCmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS Products (
        Id TEXT PRIMARY KEY,
        ProductName TEXT NOT NULL,
        Qty INTEGER NOT NULL,
        Rate REAL NOT NULL,
        CD TEXT,
        CGSTPct REAL,
        SGSTPct REAL,
        NetRate REAL,
        ImportedAt TEXT
    );";
            productsCmd.ExecuteNonQuery();

            // ── AppMeta table (first run date, etc.) ──────────────────────────────
            var metaCmd = conn.CreateCommand();
            metaCmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS AppMeta (
        Key   TEXT PRIMARY KEY,
        Value TEXT NOT NULL
    );";
            metaCmd.ExecuteNonQuery();

            // Record first-run date ONCE — never overwrite if already set
            var firstRunCmd = conn.CreateCommand();
            firstRunCmd.CommandText = @"
    INSERT OR IGNORE INTO AppMeta (Key, Value)
    VALUES ('FirstRunDate', @Today);";
            firstRunCmd.Parameters.AddWithValue("@Today",
                DateTime.Now.ToString("yyyy-MM-dd"));
            firstRunCmd.ExecuteNonQuery();

            // ── Add new columns if they don't exist yet (safe migration) ──
            var migrateCmd = conn.CreateCommand();
            migrateCmd.CommandText = @"
    ALTER TABLE Products ADD COLUMN PurchasePrice REAL DEFAULT 0;
";
            try { migrateCmd.ExecuteNonQuery(); } catch { /* column already exists */ }

            migrateCmd.CommandText = @"
    ALTER TABLE Products ADD COLUMN ProfitAmount REAL DEFAULT 0;
";
            try { migrateCmd.ExecuteNonQuery(); } catch { /* column already exists */ }

            migrateCmd.CommandText = @"
    ALTER TABLE Products ADD COLUMN ProfitPct REAL DEFAULT 0;
";
            try { migrateCmd.ExecuteNonQuery(); } catch { /* column already exists */ }

            // Suppliers table
            var suppliersCmd = conn.CreateCommand();
            suppliersCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Suppliers (
                    Id TEXT PRIMARY KEY,
                    Name TEXT NOT NULL,
                    Contact TEXT NOT NULL,
                    Address TEXT,
                    Email TEXT,
                    Phone TEXT,
                    Notes TEXT,
                    CreatedAt TEXT,
                    UpdatedAt TEXT
                );";
            suppliersCmd.ExecuteNonQuery();

            // Bills table
            var billsCmd = conn.CreateCommand();
            billsCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Bills (
                    BillNumber TEXT PRIMARY KEY,
                    BillDate TEXT NOT NULL,
                    CustomerName TEXT NOT NULL,
                    CustomerMobile TEXT NOT NULL,
                    SubTotal REAL,
                    TotalCGST REAL,
                    TotalSGST REAL,
                    GrandTotal REAL
                );";
            billsCmd.ExecuteNonQuery();

            // BillItems table (linked to Bills)
            var billItemsCmd = conn.CreateCommand();
            billItemsCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS BillItems (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    BillNumber TEXT NOT NULL,
                    ProductId TEXT NOT NULL,
                    ProductName TEXT NOT NULL,
                    Rate REAL NOT NULL,
                    Quantity INTEGER NOT NULL,
                    CGSTPct REAL,
                    SGSTPct REAL,
                    CD TEXT,
                    SubTotal REAL,
                    CGSTAmt REAL,
                    SGSTAmt REAL,
                    Total REAL,
                    FOREIGN KEY (BillNumber) REFERENCES Bills (BillNumber) ON DELETE CASCADE
                );";
            billItemsCmd.ExecuteNonQuery();

            // ImportHistory table
            var importHistoryCmd = conn.CreateCommand();
            importHistoryCmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS ImportHistory (
                    Id INTEGER PRIMARY KEY AUTOINCREMENT,
                    ImportDate TEXT NOT NULL,
                    FileName TEXT NOT NULL,
                    ItemCount INTEGER NOT NULL,
                    SupplierName TEXT,
                    SupplierContact TEXT,
                    ProductsJson TEXT
                );";
            importHistoryCmd.ExecuteNonQuery();

            // ✅ Add indexes for faster queries
            var indexCmd = conn.CreateCommand();
            indexCmd.CommandText = @"
                CREATE INDEX IF NOT EXISTS idx_products_name ON Products(ProductName);
                CREATE INDEX IF NOT EXISTS idx_bills_customer ON Bills(CustomerName, CustomerMobile);
                CREATE INDEX IF NOT EXISTS idx_imports_date ON ImportHistory(ImportDate);
                CREATE INDEX IF NOT EXISTS idx_bills_date ON Bills(BillDate);
            ";
            indexCmd.ExecuteNonQuery();


            // Settings table
            var settingsCmd = conn.CreateCommand();
            settingsCmd.CommandText = @"
    CREATE TABLE IF NOT EXISTS Settings (
        Key TEXT PRIMARY KEY,
        Value TEXT
    );";
            settingsCmd.ExecuteNonQuery();
        }

        // ── Products CRUD ─────────────────────────────────────────────────

        public static List<ExcelProductItem> LoadProducts()
        {
            var products = new List<ExcelProductItem>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Products ORDER BY ProductName";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                products.Add(new ExcelProductItem
                {
                    Id = reader["Id"].ToString() ?? string.Empty,
                    ProductName = reader["ProductName"].ToString() ?? string.Empty,
                    Qty = Convert.ToInt32(reader["Qty"]),
                    Rate = Convert.ToDouble(reader["Rate"]),
                    PurchasePrice = reader["PurchasePrice"] != DBNull.Value ? Convert.ToDouble(reader["PurchasePrice"]) : 0,
                    ProfitAmount = reader["ProfitAmount"] != DBNull.Value ? Convert.ToDouble(reader["ProfitAmount"]) : 0,
                    ProfitPct = reader["ProfitPct"] != DBNull.Value ? Convert.ToDouble(reader["ProfitPct"]) : 0,
                    CGSTPct = reader["CGSTPct"] != DBNull.Value ? Convert.ToDouble(reader["CGSTPct"]) : 0,
                    SGSTPct = reader["SGSTPct"] != DBNull.Value ? Convert.ToDouble(reader["SGSTPct"]) : 0,
                    NetRate = reader["NetRate"] != DBNull.Value ? Convert.ToDouble(reader["NetRate"]) : 0,
                    ImportedAt = reader["ImportedAt"]?.ToString() ?? string.Empty
                });
            }

            return products;
        }
        public static bool SaveProducts(List<ExcelProductItem> products)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                // Clear existing products (simple approach for now)
                var clearCmd = conn.CreateCommand();
                clearCmd.CommandText = "DELETE FROM Products";
                clearCmd.ExecuteNonQuery();

                // Insert all products
                foreach (var p in products)
                {
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
INSERT INTO Products (
    Id, ProductName, Qty, Rate,
    PurchasePrice, ProfitAmount, ProfitPct,
    CGSTPct, SGSTPct, NetRate,
    ImportedAt
) VALUES (
    @Id, @ProductName, @Qty, @Rate,
    @PurchasePrice, @ProfitAmount, @ProfitPct,
    @CGSTPct, @SGSTPct, @NetRate,
    @ImportedAt
);";

                    cmd.Parameters.AddWithValue("@Id", p.Id ?? string.Empty);
                    cmd.Parameters.AddWithValue("@ProductName", p.ProductName ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Qty", p.Qty);
                    cmd.Parameters.AddWithValue("@Rate", p.Rate);
                    cmd.Parameters.AddWithValue("@PurchasePrice", p.PurchasePrice);
                    cmd.Parameters.AddWithValue("@ProfitAmount", p.ProfitAmount);
                    cmd.Parameters.AddWithValue("@ProfitPct", p.ProfitPct);
                    cmd.Parameters.AddWithValue("@CGSTPct", p.CGSTPct);
                    cmd.Parameters.AddWithValue("@SGSTPct", p.SGSTPct);
                    cmd.Parameters.AddWithValue("@NetRate", p.NetRate);
                    cmd.Parameters.AddWithValue("@ImportedAt", p.ImportedAt ?? string.Empty);

                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                return false;
            }
        }
        public static bool AddProducts(List<ExcelProductItem> newProducts)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var p in newProducts)
                {
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
        INSERT OR REPLACE INTO Products (
            Id, ProductName, Qty, Rate,
            PurchasePrice, ProfitAmount, ProfitPct,
            CGSTPct, SGSTPct, NetRate,
            ImportedAt
        ) VALUES (
            @Id, @ProductName, @Qty, @Rate,
            @PurchasePrice, @ProfitAmount, @ProfitPct,
            @CGSTPct, @SGSTPct, @NetRate,
            @ImportedAt
        );";

                    cmd.Parameters.AddWithValue("@Id", p.Id ?? string.Empty);
                    cmd.Parameters.AddWithValue("@ProductName", p.ProductName ?? string.Empty);
                    cmd.Parameters.AddWithValue("@Qty", p.Qty);
                    cmd.Parameters.AddWithValue("@Rate", p.Rate);
                    cmd.Parameters.AddWithValue("@PurchasePrice", p.PurchasePrice);
                    cmd.Parameters.AddWithValue("@ProfitAmount", p.ProfitAmount);
                    cmd.Parameters.AddWithValue("@ProfitPct", p.ProfitPct);
                    cmd.Parameters.AddWithValue("@CGSTPct", p.CGSTPct);
                    cmd.Parameters.AddWithValue("@SGSTPct", p.SGSTPct);
                    cmd.Parameters.AddWithValue("@NetRate", p.NetRate);
                    cmd.Parameters.AddWithValue("@ImportedAt", p.ImportedAt ?? string.Empty);

                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] AddProducts: {ex.Message}");
                transaction.Rollback();
                return false;
            }
        }
        public static DateTime GetFirstRunDate()
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Value FROM AppMeta WHERE Key = 'FirstRunDate'";
            var result = cmd.ExecuteScalar();

            if (result != null && DateTime.TryParse(result.ToString(), out DateTime firstRun))
                return firstRun;

            // Fallback: if somehow missing, insert today and return today
            var insertCmd = conn.CreateCommand();
            insertCmd.CommandText = @"
        INSERT OR IGNORE INTO AppMeta (Key, Value)
        VALUES ('FirstRunDate', @Today);";
            insertCmd.Parameters.AddWithValue("@Today", DateTime.Now.ToString("yyyy-MM-dd"));
            insertCmd.ExecuteNonQuery();

            return DateTime.Now.Date;
        }
        public static bool UpdateProduct(ExcelProductItem updated)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
UPDATE Products SET
    ProductName     = @ProductName,
    Qty             = @Qty,
    Rate            = @Rate,
    PurchasePrice   = @PurchasePrice,
    ProfitAmount    = @ProfitAmount,
    ProfitPct       = @ProfitPct,
    CGSTPct         = @CGSTPct,
    SGSTPct         = @SGSTPct,
    NetRate         = @NetRate,
    ImportedAt      = @ImportedAt
WHERE Id = @Id;";

            cmd.Parameters.AddWithValue("@Id", updated.Id ?? string.Empty);
            cmd.Parameters.AddWithValue("@ProductName", updated.ProductName ?? string.Empty);
            cmd.Parameters.AddWithValue("@Qty", updated.Qty);
            cmd.Parameters.AddWithValue("@Rate", updated.Rate);
            cmd.Parameters.AddWithValue("@PurchasePrice", updated.PurchasePrice);
            cmd.Parameters.AddWithValue("@ProfitAmount", updated.ProfitAmount);
            cmd.Parameters.AddWithValue("@ProfitPct", updated.ProfitPct);
            cmd.Parameters.AddWithValue("@CGSTPct", updated.CGSTPct);
            cmd.Parameters.AddWithValue("@SGSTPct", updated.SGSTPct);
            cmd.Parameters.AddWithValue("@NetRate", updated.NetRate);
            cmd.Parameters.AddWithValue("@ImportedAt", updated.ImportedAt ?? string.Empty);

            return cmd.ExecuteNonQuery() > 0;
        }
        public static bool DeleteProduct(string productId)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Products WHERE Id = @Id";
            cmd.Parameters.AddWithValue("@Id", productId);

            return cmd.ExecuteNonQuery() > 0;
        }

        // ── Suppliers CRUD ────────────────────────────────────────────────

        public static List<Supplier> LoadSuppliers()
        {
            var suppliers = new List<Supplier>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Suppliers ORDER BY Name";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                suppliers.Add(new Supplier
                {
                    Id = reader["Id"].ToString() ?? string.Empty,
                    Name = reader["Name"].ToString() ?? string.Empty,
                    Contact = reader["Contact"].ToString() ?? string.Empty,
                    Address = reader["Address"]?.ToString(),
                    Email = reader["Email"]?.ToString(),
                    Phone = reader["Phone"]?.ToString(),
                    Notes = reader["Notes"]?.ToString(),
                    CreatedAt = reader["CreatedAt"] != DBNull.Value ?
                        DateTime.Parse(reader["CreatedAt"].ToString()) : DateTime.Now,
                    UpdatedAt = reader["UpdatedAt"] != DBNull.Value ?
                        DateTime.Parse(reader["UpdatedAt"].ToString()) : (DateTime?)null
                });
            }

            return suppliers;
        }

        public static bool SaveSuppliers(List<Supplier> suppliers)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                foreach (var s in suppliers)
                {
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                        INSERT OR REPLACE INTO Suppliers (
                            Id, Name, Contact, Address, Email, Phone, Notes, CreatedAt, UpdatedAt
                        ) VALUES (
                            @Id, @Name, @Contact, @Address, @Email, @Phone, @Notes, @CreatedAt, @UpdatedAt
                        );";

                    cmd.Parameters.AddWithValue("@Id", s.Id);
                    cmd.Parameters.AddWithValue("@Name", s.Name);
                    cmd.Parameters.AddWithValue("@Contact", s.Contact);
                    cmd.Parameters.AddWithValue("@Address", (object?)s.Address ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Email", (object?)s.Email ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Phone", (object?)s.Phone ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@Notes", (object?)s.Notes ?? DBNull.Value);
                    cmd.Parameters.AddWithValue("@CreatedAt", s.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@UpdatedAt", (object?)s.UpdatedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                return false;
            }
        }

        // ── Bills CRUD ────────────────────────────────────────────────────

        public static bool SaveBill(Bill bill)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                // Save bill header
                var billCmd = conn.CreateCommand();
                billCmd.CommandText = @"
                    INSERT OR REPLACE INTO Bills (
                        BillNumber, BillDate, CustomerName, CustomerMobile,
                        SubTotal, TotalCGST, TotalSGST, GrandTotal
                    ) VALUES (
                        @BillNumber, @BillDate, @CustomerName, @CustomerMobile,
                        @SubTotal, @TotalCGST, @TotalSGST, @GrandTotal
                    );";

                billCmd.Parameters.AddWithValue("@BillNumber", bill.BillNumber);
                billCmd.Parameters.AddWithValue("@BillDate", bill.BillDate.ToString("yyyy-MM-dd HH:mm:ss"));
                billCmd.Parameters.AddWithValue("@CustomerName", bill.CustomerName);
                billCmd.Parameters.AddWithValue("@CustomerMobile", bill.CustomerMobile);
                billCmd.Parameters.AddWithValue("@SubTotal", bill.SubTotal);
                billCmd.Parameters.AddWithValue("@TotalCGST", bill.TotalCGST);
                billCmd.Parameters.AddWithValue("@TotalSGST", bill.TotalSGST);
                billCmd.Parameters.AddWithValue("@GrandTotal", bill.GrandTotal);

                billCmd.ExecuteNonQuery();

                // Save bill items
                foreach (var item in bill.Items)
                {
                    var itemCmd = conn.CreateCommand();
                    itemCmd.CommandText = @"
                        INSERT INTO BillItems (
                            BillNumber, ProductId, ProductName, Rate, Quantity,
                            CGSTPct, SGSTPct, CD, SubTotal, CGSTAmt, SGSTAmt, Total
                        ) VALUES (
                            @BillNumber, @ProductId, @ProductName, @Rate, @Quantity,
                            @CGSTPct, @SGSTPct, @CD, @SubTotal, @CGSTAmt, @SGSTAmt, @Total
                        );";

                    itemCmd.Parameters.AddWithValue("@BillNumber", bill.BillNumber);
                    itemCmd.Parameters.AddWithValue("@ProductId", item.ProductId);
                    itemCmd.Parameters.AddWithValue("@ProductName", item.ProductName);
                    itemCmd.Parameters.AddWithValue("@Rate", item.Rate);
                    itemCmd.Parameters.AddWithValue("@Quantity", item.Quantity);
                    itemCmd.Parameters.AddWithValue("@CGSTPct", item.CGSTPct);
                    itemCmd.Parameters.AddWithValue("@SGSTPct", item.SGSTPct);
                    itemCmd.Parameters.AddWithValue("@CD", (object?)item.CD ?? DBNull.Value);

                    // ✅ Store computed values for reporting consistency
                    itemCmd.Parameters.AddWithValue("@SubTotal", item.SubTotal);
                    itemCmd.Parameters.AddWithValue("@CGSTAmt", item.CGSTAmt);
                    itemCmd.Parameters.AddWithValue("@SGSTAmt", item.SGSTAmt);
                    itemCmd.Parameters.AddWithValue("@Total", item.Total);

                    itemCmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DB ERROR] SaveBill: {ex.Message}");
                transaction.Rollback();
                return false;
            }
        }
        // ── Customers (derived from Bills, no separate table) ──────────────
        public static List<CustomerInfo> GetCustomers()
        {
            var customers = new List<CustomerInfo>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
        SELECT DISTINCT CustomerName, CustomerMobile
        FROM Bills
        WHERE CustomerName IS NOT NULL AND CustomerName != ''
          AND CustomerName != 'Walk-in Customer'
        ORDER BY CustomerName";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                customers.Add(new CustomerInfo
                {
                    CustomerName = reader["CustomerName"].ToString() ?? string.Empty,
                    CustomerMobile = reader["CustomerMobile"].ToString() ?? string.Empty
                });
            }

            return customers;
        }

        public static List<Bill> LoadBills()
        {
            var bills = new List<Bill>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            // Load bill headers
            var billCmd = conn.CreateCommand();
            billCmd.CommandText = "SELECT * FROM Bills ORDER BY BillDate DESC";

            using var billReader = billCmd.ExecuteReader();
            while (billReader.Read())
            {
                var bill = new Bill
                {
                    BillNumber = billReader["BillNumber"].ToString() ?? string.Empty,
                    BillDate = DateTime.Parse(billReader["BillDate"].ToString()),
                    CustomerName = billReader["CustomerName"].ToString() ?? string.Empty,
                    CustomerMobile = billReader["CustomerMobile"].ToString() ?? string.Empty,
                    Items = new List<BillItem>() // Will populate below
                };

                // Load items for this bill
                var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = "SELECT * FROM BillItems WHERE BillNumber = @BillNumber";
                itemCmd.Parameters.AddWithValue("@BillNumber", bill.BillNumber);

                using var itemReader = itemCmd.ExecuteReader();
                while (itemReader.Read())
                {
                    bill.Items.Add(new BillItem
                    {
                        ProductId = itemReader["ProductId"].ToString() ?? string.Empty,
                        ProductName = itemReader["ProductName"].ToString() ?? string.Empty,
                        Rate = Convert.ToDouble(itemReader["Rate"]),
                        Quantity = Convert.ToInt32(itemReader["Quantity"]),
                        CGSTPct = itemReader["CGSTPct"] != DBNull.Value ? Convert.ToDouble(itemReader["CGSTPct"]) : 0,
                        SGSTPct = itemReader["SGSTPct"] != DBNull.Value ? Convert.ToDouble(itemReader["SGSTPct"]) : 0,
                        CD = itemReader["CD"]?.ToString() ?? string.Empty
                        // ✅ Computed properties (SubTotal, Total, etc.) will auto-calculate from above values
                    });
                }

                bills.Add(bill);
            }

            return bills;
        }

        public static List<Bill> LoadBillsByDateRange(DateTime startDate, DateTime endDate)
        {
            var bills = new List<Bill>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var billCmd = conn.CreateCommand();
            billCmd.CommandText = @"
                SELECT * FROM Bills 
                WHERE DATE(BillDate) >= DATE(@StartDate) AND DATE(BillDate) <= DATE(@EndDate)
                ORDER BY BillDate DESC";

            billCmd.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
            billCmd.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));

            using var billReader = billCmd.ExecuteReader();
            while (billReader.Read())
            {
                var bill = new Bill
                {
                    BillNumber = billReader["BillNumber"].ToString() ?? string.Empty,
                    BillDate = DateTime.Parse(billReader["BillDate"].ToString()),
                    CustomerName = billReader["CustomerName"].ToString() ?? string.Empty,
                    CustomerMobile = billReader["CustomerMobile"].ToString() ?? string.Empty,
                    Items = new List<BillItem>()
                };

                var itemCmd = conn.CreateCommand();
                itemCmd.CommandText = "SELECT * FROM BillItems WHERE BillNumber = @BillNumber";
                itemCmd.Parameters.AddWithValue("@BillNumber", bill.BillNumber);

                using var itemReader = itemCmd.ExecuteReader();
                while (itemReader.Read())
                {
                    bill.Items.Add(new BillItem
                    {
                        ProductId = itemReader["ProductId"].ToString() ?? string.Empty,
                        ProductName = itemReader["ProductName"].ToString() ?? string.Empty,
                        Rate = Convert.ToDouble(itemReader["Rate"]),
                        Quantity = Convert.ToInt32(itemReader["Quantity"]),
                        CGSTPct = itemReader["CGSTPct"] != DBNull.Value ? Convert.ToDouble(itemReader["CGSTPct"]) : 0,
                        SGSTPct = itemReader["SGSTPct"] != DBNull.Value ? Convert.ToDouble(itemReader["SGSTPct"]) : 0,
                        CD = itemReader["CD"]?.ToString() ?? string.Empty
                    });
                }

                bills.Add(bill);
            }

            return bills;
        }

        public static Bill? LoadBillByNumber(string billNumber)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var billCmd = conn.CreateCommand();
            billCmd.CommandText = "SELECT * FROM Bills WHERE BillNumber = @BillNumber";
            billCmd.Parameters.AddWithValue("@BillNumber", billNumber);

            using var billReader = billCmd.ExecuteReader();
            if (!billReader.Read()) return null;

            var bill = new Bill
            {
                BillNumber = billReader["BillNumber"].ToString() ?? string.Empty,
                BillDate = DateTime.Parse(billReader["BillDate"].ToString()),
                CustomerName = billReader["CustomerName"].ToString() ?? string.Empty,
                CustomerMobile = billReader["CustomerMobile"].ToString() ?? string.Empty,
                Items = new List<BillItem>()
            };

            var itemCmd = conn.CreateCommand();
            itemCmd.CommandText = "SELECT * FROM BillItems WHERE BillNumber = @BillNumber";
            itemCmd.Parameters.AddWithValue("@BillNumber", billNumber);

            using var itemReader = itemCmd.ExecuteReader();
            while (itemReader.Read())
            {
                bill.Items.Add(new BillItem
                {
                    ProductId = itemReader["ProductId"].ToString() ?? string.Empty,
                    ProductName = itemReader["ProductName"].ToString() ?? string.Empty,
                    Rate = Convert.ToDouble(itemReader["Rate"]),
                    Quantity = Convert.ToInt32(itemReader["Quantity"]),
                    CGSTPct = itemReader["CGSTPct"] != DBNull.Value ? Convert.ToDouble(itemReader["CGSTPct"]) : 0,
                    SGSTPct = itemReader["SGSTPct"] != DBNull.Value ? Convert.ToDouble(itemReader["SGSTPct"]) : 0,
                    CD = itemReader["CD"]?.ToString() ?? string.Empty
                });
            }

            return bill;
        }

        public static bool DeleteBillByNumber(string billNumber)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            // ✅ ON DELETE CASCADE in schema handles BillItems automatically
            var cmd = conn.CreateCommand();
            cmd.CommandText = "DELETE FROM Bills WHERE BillNumber = @BillNumber";
            cmd.Parameters.AddWithValue("@BillNumber", billNumber);

            int rows = cmd.ExecuteNonQuery();
            return rows > 0;
        }

        // ── Customers Analytics ───────────────────────────────────────────

        /// <summary>
        /// Load all customers aggregated from bills (for analytics)
        /// </summary>
        public static List<CustomerSummary> LoadCustomerSummaries()
        {
            var customerMap = new Dictionary<string, CustomerSummary>(StringComparer.OrdinalIgnoreCase);
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            // Load all bills and aggregate by customer
            var billCmd = conn.CreateCommand();
            billCmd.CommandText = "SELECT * FROM Bills ORDER BY BillDate DESC";

            using var billReader = billCmd.ExecuteReader();
            while (billReader.Read())
            {
                var customerName = billReader["CustomerName"].ToString() ?? string.Empty;
                var customerMobile = billReader["CustomerMobile"].ToString() ?? string.Empty;
                var billDate = DateTime.Parse(billReader["BillDate"].ToString());
                var grandTotal = Convert.ToDouble(billReader["GrandTotal"]);

                if (string.IsNullOrWhiteSpace(customerName)) continue;

                string key = $"{customerName}|{customerMobile}";

                if (!customerMap.ContainsKey(key))
                {
                    customerMap[key] = new CustomerSummary
                    {
                        Name = customerName.Trim(),
                        Mobile = customerMobile.Trim(),
                        FirstPurchaseDate = billDate,
                        LastPurchaseDate = billDate,
                        PurchaseCount = 0,
                        TotalSpent = 0
                    };
                }

                var customer = customerMap[key];
                customer.PurchaseCount++;
                customer.TotalSpent += grandTotal;

                if (billDate > customer.LastPurchaseDate)
                    customer.LastPurchaseDate = billDate;
                if (billDate < customer.FirstPurchaseDate)
                    customer.FirstPurchaseDate = billDate;
            }

            return customerMap.Values.ToList();
        }

        /// <summary>
        /// Get total sales from all bills
        /// </summary>
        public static double GetTotalSales()
        {
            string connStr = $"Data Source={GetDbPath()}";
            double total = 0;

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT SUM(GrandTotal) FROM Bills";
            var result = cmd.ExecuteScalar();

            if (result != DBNull.Value)
                total = Convert.ToDouble(result);

            return total;
        }

        /// <summary>
        /// Get monthly sales data for the last 6 months
        /// </summary>
        public static Dictionary<string, double> GetMonthlySalesData()
        {
            var monthly = new Dictionary<string, double>();
            var now = DateTime.Now;

            // Initialize last 6 months
            for (int i = 5; i >= 0; i--)
            {
                var date = now.AddMonths(-i);
                string key = date.ToString("MMM yyyy");
                monthly[key] = 0;
            }

            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                SELECT BillDate, GrandTotal 
                FROM Bills 
                WHERE BillDate >= date('now', '-6 months')
                ORDER BY BillDate";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var billDate = DateTime.Parse(reader["BillDate"].ToString());
                var amount = Convert.ToDouble(reader["GrandTotal"]);
                string key = billDate.ToString("MMM yyyy");

                if (monthly.ContainsKey(key))
                    monthly[key] += amount;
            }

            return monthly;
        }

        // ── Dashboard & Activity Feed ─────────────────────────────────────

        /// <summary>
        /// Load recent activity: latest bills and imports
        /// </summary>
        public static List<ActivityItem> LoadRecentActivity(int limit = 20)
        {
            var activities = new List<ActivityItem>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            // Load recent bills
            var billCmd = conn.CreateCommand();
            billCmd.CommandText = @"
                SELECT BillNumber, BillDate, CustomerName, CustomerMobile, GrandTotal 
                FROM Bills 
                ORDER BY BillDate DESC 
                LIMIT @Limit";
            billCmd.Parameters.AddWithValue("@Limit", limit);

            using var billReader = billCmd.ExecuteReader();
            while (billReader.Read())
            {
                activities.Add(new ActivityItem
                {
                    Timestamp = DateTime.Parse(billReader["BillDate"].ToString()),
                    Type = "🧾 Bill",
                    Description = $"Bill #{billReader["BillNumber"]} - {billReader["CustomerName"]}",
                    Amount = $"₹{Convert.ToDouble(billReader["GrandTotal"]):N2}",
                    Data = null // Could load full Bill object if needed
                });
            }

            // Load recent imports
            var importCmd = conn.CreateCommand();
            importCmd.CommandText = @"
                SELECT ImportDate, FileName, ItemCount, SupplierName, SupplierContact 
                FROM ImportHistory 
                ORDER BY ImportDate DESC 
                LIMIT @Limit";
            importCmd.Parameters.AddWithValue("@Limit", limit);

            using var importReader = importCmd.ExecuteReader();
            while (importReader.Read())
            {
                activities.Add(new ActivityItem
                {
                    Timestamp = DateTime.Parse(importReader["ImportDate"].ToString()),
                    Type = "📥 Import",
                    Description = $"Imported {importReader["ItemCount"]} items from {importReader["FileName"]} ({importReader["SupplierName"]})",
                    Amount = $"{importReader["ItemCount"]} items",
                    Data = null // Could load full ImportHistory if needed
                });
            }

            // Sort by timestamp descending and limit
            return activities
                .OrderByDescending(a => a.Timestamp)
                .Take(limit)
                .ToList();
        }

        // ── Import History CRUD ───────────────────────────────────────────

        public static bool SaveImportHistory(ImportHistory import)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                INSERT INTO ImportHistory (
                    ImportDate, FileName, ItemCount, SupplierName, 
                    SupplierContact, ProductsJson
                ) VALUES (
                    @ImportDate, @FileName, @ItemCount, @SupplierName,
                    @SupplierContact, @ProductsJson
                );";

            cmd.Parameters.AddWithValue("@ImportDate", import.ImportDate.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.Parameters.AddWithValue("@FileName", import.FileName);
            cmd.Parameters.AddWithValue("@ItemCount", import.ItemCount);
            cmd.Parameters.AddWithValue("@SupplierName", (object?)import.SupplierName ?? DBNull.Value);
            cmd.Parameters.AddWithValue("@SupplierContact", (object?)import.SupplierContact ?? DBNull.Value);

            // Serialize products to JSON string for storage
            string productsJson = import.Products != null
                ? JsonConvert.SerializeObject(import.Products)
                : null;
            cmd.Parameters.AddWithValue("@ProductsJson", (object?)productsJson ?? DBNull.Value);

            return cmd.ExecuteNonQuery() > 0;
        }

        public static List<ImportHistory> LoadImportHistory()
        {
            var imports = new List<ImportHistory>();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM ImportHistory ORDER BY ImportDate DESC";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var import = new ImportHistory
                {
                    ImportDate = DateTime.Parse(reader["ImportDate"].ToString()),
                    FileName = reader["FileName"].ToString() ?? string.Empty,
                    ItemCount = Convert.ToInt32(reader["ItemCount"]),
                    SupplierName = reader["SupplierName"]?.ToString(),
                    SupplierContact = reader["SupplierContact"]?.ToString()
                };

                // Deserialize products from JSON string
                string productsJson = reader["ProductsJson"]?.ToString();
                if (!string.IsNullOrEmpty(productsJson))
                {
                    import.Products = JsonConvert.DeserializeObject<List<ImportedProductSnapshot>>(productsJson);
                }

                imports.Add(import);
            }

            return imports;
        }

        // ── Utility Methods ───────────────────────────────────────────────

        /// <summary>
        /// Create a backup of the database file
        /// </summary>
        public static void CreateBackup()
        {
            try
            {
                string source = GetDbPath();
                string backupDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "TextileShop", "Backups");

                Directory.CreateDirectory(backupDir);
                string backupPath = Path.Combine(backupDir,
                    $"textile_shop_backup_{DateTime.Now:yyyyMMdd_HHmmss}.db");

                File.Copy(source, backupPath, true);
                System.Diagnostics.Debug.WriteLine($"[INFO] Backup created: {backupPath}");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Backup failed: {ex.Message}");
            }
        }



        // ── Settings CRUD ─────────────────────────────────────────────────────

        public static AppSettings LoadSettings()
        {
            var settings = new AppSettings();
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            var cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Key, Value FROM Settings";

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string key = reader["Key"].ToString() ?? "";
                string value = reader["Value"].ToString() ?? "";

                switch (key)
                {
                    case "ShopName": settings.ShopName = value; break;
                    case "Subtitle": settings.Subtitle = value; break;
                    case "LogoPath": settings.LogoPath = value; break;
                    case "ShopAddress": settings.ShopAddress = value; break;
                    case "ShopPhone": settings.ShopPhone = value; break;
                }
            }

            return settings;
        }

        public static bool SaveSettings(AppSettings settings)
        {
            string connStr = $"Data Source={GetDbPath()}";

            using var conn = new SqliteConnection(connStr);
            conn.Open();

            using var transaction = conn.BeginTransaction();

            try
            {
                var pairs = new[]
                {
            ("ShopName",    settings.ShopName    ?? ""),
            ("Subtitle",    settings.Subtitle    ?? ""),
            ("LogoPath",    settings.LogoPath    ?? ""),
            ("ShopAddress", settings.ShopAddress ?? ""),
            ("ShopPhone",   settings.ShopPhone   ?? "")
        };

                foreach (var (key, value) in pairs)
                {
                    var cmd = conn.CreateCommand();
                    cmd.CommandText = @"
                INSERT INTO Settings (Key, Value) VALUES (@Key, @Value)
                ON CONFLICT(Key) DO UPDATE SET Value = excluded.Value;";
                    cmd.Parameters.AddWithValue("@Key", key);
                    cmd.Parameters.AddWithValue("@Value", value);
                    cmd.ExecuteNonQuery();
                }

                transaction.Commit();
                return true;
            }
            catch
            {
                transaction.Rollback();
                return false;
            }
        }
        /// <summary>
        /// Get the database file path (for debugging/backup)
        /// </summary>
        public static string GetDatabasePath()
        {
            return GetDbPath();
        }
    }
}
