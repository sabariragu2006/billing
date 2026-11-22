// ── Services/ReturnsService.cs ─────────────────────────────────────────────
#nullable enable
using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using textileshop.Models;

namespace textileshop.Services
{
    public static class ReturnsService
    {
        private static string DbPath
        {
            get
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string dbFolder = System.IO.Path.Combine(appData, "TextileShop");
                return System.IO.Path.Combine(dbFolder, "textile_shop.db");
            }
        }

        private static SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection($"Data Source={DbPath}");
            conn.Open();
            return conn;
        }

        // ── Table bootstrap ────────────────────────────────────────────────
        public static void EnsureTable()
        {
            using var conn = CreateConnection();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE IF NOT EXISTS Returns (
                    Id            TEXT PRIMARY KEY,
                    ProductId     TEXT NOT NULL,
                    ProductName   TEXT NOT NULL,
                    ReturnQty     INTEGER NOT NULL,
                    SaleRate      REAL NOT NULL,
                    RefundAmount  REAL NOT NULL,
                    Reason        TEXT,
                    CustomerName  TEXT,
                    ReturnedBy    TEXT,
                    ReturnDate    TEXT NOT NULL,
                    Notes         TEXT
                );";
            cmd.ExecuteNonQuery();
        }

        public static bool SaveReturn(ReturnRecord r)
        {
            try
            {
                EnsureTable();
                r.Id = Guid.NewGuid().ToString("N")[..8].ToUpper();
                r.RefundAmount = r.ReturnQty * r.SaleRate;

                using var conn = CreateConnection();

                // 1. Insert return record
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                INSERT INTO Returns
                    (Id, ProductId, ProductName, ReturnQty, SaleRate,
                     RefundAmount, Reason, CustomerName, ReturnedBy, ReturnDate, Notes)
                VALUES
                    (@Id, @Pid, @Name, @Qty, @Rate,
                     @Refund, @Reason, @Cust, @RetBy, @Date, @Notes);";

                    cmd.Parameters.AddWithValue("@Id", r.Id);
                    cmd.Parameters.AddWithValue("@Pid", r.ProductId);
                    cmd.Parameters.AddWithValue("@Name", r.ProductName);
                    cmd.Parameters.AddWithValue("@Qty", r.ReturnQty);
                    cmd.Parameters.AddWithValue("@Rate", r.SaleRate);
                    cmd.Parameters.AddWithValue("@Refund", r.RefundAmount);
                    cmd.Parameters.AddWithValue("@Reason", r.Reason ?? "");
                    cmd.Parameters.AddWithValue("@Cust", r.CustomerName ?? "");
                    cmd.Parameters.AddWithValue("@RetBy", r.ReturnedBy ?? "");
                    cmd.Parameters.AddWithValue("@Date", r.ReturnDate.ToString("yyyy-MM-dd HH:mm:ss"));
                    cmd.Parameters.AddWithValue("@Notes", r.Notes ?? "");
                    cmd.ExecuteNonQuery();
                }

                // 2. Add qty back to stock
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                UPDATE Products
                SET    Qty = Qty + @Qty
                WHERE  Id  = @Pid;";
                    cmd.Parameters.AddWithValue("@Qty", r.ReturnQty);
                    cmd.Parameters.AddWithValue("@Pid", r.ProductId);
                    int rows = cmd.ExecuteNonQuery();

                    // Log if no product row was matched
                    System.Diagnostics.Debug.WriteLine(
                        rows == 0
                            ? $"[ReturnsService] ⚠️ No product found with Id='{r.ProductId}' — stock not updated"
                            : $"[ReturnsService] ✅ Stock updated for ProductId='{r.ProductId}', +{r.ReturnQty}");
                }

                InventoryService.NotifyDataChanged();
                return true;
            }
            catch (Exception ex)
            {
                // Show the REAL error in a MessageBox so you can see it
                System.Windows.Application.Current.Dispatcher.Invoke(() =>
                {
                    System.Windows.MessageBox.Show(
                        $"SaveReturn failed:\n\n{ex.GetType().Name}\n{ex.Message}\n\n{ex.StackTrace}",
                        "Debug Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                });
                System.Diagnostics.Debug.WriteLine($"[ReturnsService] SaveReturn failed: {ex}");
                return false;
            }
        }

        // ── Load all returns (newest first) ───────────────────────────────
        public static List<ReturnRecord> LoadReturns()
        {
            EnsureTable();
            var list = new List<ReturnRecord>();
            try
            {
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText =
                    "SELECT * FROM Returns ORDER BY ReturnDate DESC;";

                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    list.Add(new ReturnRecord
                    {
                        Id = reader["Id"].ToString()!,
                        ProductId = reader["ProductId"].ToString()!,
                        ProductName = reader["ProductName"].ToString()!,
                        ReturnQty = Convert.ToInt32(reader["ReturnQty"]),
                        SaleRate = Convert.ToDouble(reader["SaleRate"]),
                        RefundAmount = Convert.ToDouble(reader["RefundAmount"]),
                        Reason = reader["Reason"].ToString()!,
                        CustomerName = reader["CustomerName"].ToString()!,
                        ReturnedBy = reader["ReturnedBy"].ToString()!,
                        ReturnDate = DateTime.Parse(reader["ReturnDate"].ToString()!),
                        Notes = reader["Notes"].ToString()!
                    });
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ReturnsService] LoadReturns failed: {ex.Message}");
            }
            return list;
        }

        // ── Delete a return record (record only — stock not reversed) ─────
        public static bool DeleteReturn(string id)
        {
            try
            {
                EnsureTable();
                using var conn = CreateConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "DELETE FROM Returns WHERE Id = @Id;";
                cmd.Parameters.AddWithValue("@Id", id);
                return cmd.ExecuteNonQuery() > 0;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ReturnsService] DeleteReturn failed: {ex.Message}");
                return false;
            }
        }
    }
} 