#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    public partial class SuppliersPage : Page
    {
        private readonly ObservableCollection<SupplierSummary> _suppliers = new();
        private List<SupplierSummary> _allSuppliers = new();
        private bool _isInitialized = false;
        private Supplier? _editingSupplier = null;

        // ── Max lengths ────────────────────────────────────────────────────
        private const int MaxNameLength = 100;
        private const int MaxGstLength = 15;   // exactly 15
        private const int MaxAddressLength = 200;
        private const int MaxEmailLength = 100;
        private const int MaxPhoneLength = 10;   // exactly 10 digits
        private const int MaxNotesLength = 500;

        public SuppliersPage()
        {
            try
            {
                InitializeComponent();
                _isInitialized = true;

                SuppliersService.SuppliersChanged += (s, e) => Dispatcher.Invoke(LoadSuppliers);

                LoadSuppliers();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"❌ SuppliersPage constructor failed: {ex.Message}");
                MessageBox.Show($"Page failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Load Data ──────────────────────────────────────────────────────
        private void LoadSuppliers()
        {
            if (!_isInitialized) return;

            try
            {
                _allSuppliers.Clear();

                var manualSuppliers = SuppliersService.LoadSuppliers();
                var importHistory = DatabaseHelper.LoadImportHistory();

                foreach (var manual in manualSuppliers)
                {
                    var imports = importHistory.Where(i =>
                        string.Equals(i.SupplierName, manual.Name, StringComparison.OrdinalIgnoreCase) &&
                        i.SupplierContact == manual.Contact).ToList();

                    _allSuppliers.Add(new SupplierSummary
                    {
                        Id = manual.Id,
                        Name = manual.Name,
                        Contact = manual.Contact,
                        FirstImportDate = imports.Any() ? imports.Min(i => i.ImportDate) : manual.CreatedAt,
                        LastImportDate = imports.Any() ? imports.Max(i => i.ImportDate) : manual.CreatedAt,
                        ImportCount = imports.Count,
                        TotalItemsSupplied = imports.Sum(i => i.ItemCount),
                        IsManual = true,
                        ManualSupplier = manual
                    });
                }

                var importedOnly = importHistory
                    .Where(i => !string.IsNullOrWhiteSpace(i.SupplierName) &&
                                !string.IsNullOrWhiteSpace(i.SupplierContact))
                    .GroupBy(i => $"{i.SupplierName}|{i.SupplierContact}")
                    .Select(g => g.First())
                    .Where(h => !manualSuppliers.Any(m =>
                        string.Equals(m.Name, h.SupplierName, StringComparison.OrdinalIgnoreCase) &&
                        m.Contact == h.SupplierContact));

                foreach (var import in importedOnly)
                {
                    var imports = importHistory.Where(i =>
                        string.Equals(i.SupplierName, import.SupplierName, StringComparison.OrdinalIgnoreCase) &&
                        i.SupplierContact == import.SupplierContact).ToList();

                    _allSuppliers.Add(new SupplierSummary
                    {
                        Name = import.SupplierName!.Trim(),
                        Contact = import.SupplierContact!.Trim(),
                        FirstImportDate = imports.Min(i => i.ImportDate),
                        LastImportDate = imports.Max(i => i.ImportDate),
                        ImportCount = imports.Count,
                        TotalItemsSupplied = imports.Sum(i => i.ItemCount),
                        IsManual = false
                    });
                }

                _allSuppliers = _allSuppliers.OrderByDescending(s => s.LastImportDate).ToList();
                ApplyFilters();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadSuppliers failed: {ex.Message}");
                TxtStatus.Text = $"Error loading suppliers: {ex.Message}";
            }
        }

        // ── Filters ────────────────────────────────────────────────────────
        private void ApplyFilters()
        {
            if (!_isInitialized) return;

            var search = TxtSearch.Text?.Trim().ToLower() ?? "";
            var sortOption = (CmbSort.SelectedItem as ComboBoxItem)?.Content?.ToString();

            var filtered = _allSuppliers.Where(s =>
            {
                if (string.IsNullOrEmpty(search)) return true;
                return s.Name.ToLower().Contains(search) || s.Contact.Contains(search);
            }).ToList();

            filtered = sortOption switch
            {
                "Name Z→A" => filtered.OrderByDescending(s => s.Name).ToList(),
                "Last Import (Newest)" => filtered.OrderByDescending(s => s.LastImportDate).ToList(),
                "Last Import (Oldest)" => filtered.OrderBy(s => s.LastImportDate).ToList(),
                "Most Imports" => filtered.OrderByDescending(s => s.ImportCount).ToList(),
                "Total Items (High→Low)" => filtered.OrderByDescending(s => s.TotalItemsSupplied).ToList(),
                _ => filtered.OrderBy(s => s.Name).ToList()
            };

            _suppliers.Clear();
            foreach (var s in filtered)
                _suppliers.Add(s);

            UpdateGrid();
        }

        private void UpdateGrid()
        {
            if (!_isInitialized || DgSuppliers == null) return;

            DgSuppliers.ItemsSource = null;
            DgSuppliers.ItemsSource = _suppliers;

            int total = _allSuppliers.Count;
            int showing = _suppliers.Count;

            TxtSupplierCount.Text = showing == total
                ? $"Showing {total} supplier(s)"
                : $"Showing {showing} of {total} supplier(s)";

            TxtStatus.Text = showing == 0
                ? "No suppliers found. Click '➕ Add Supplier' or import products with supplier info."
                : $"Loaded {showing} supplier(s)";
        }

        // ── Event Handlers ─────────────────────────────────────────────────

        private void TxtSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilters();
        }

        private void CmbSort_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isInitialized) return;
            ApplyFilters();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized) return;
            TxtSearch.Text = string.Empty;
            CmbSort.SelectedIndex = 0;
            LoadSuppliers();
        }

        private void DgSuppliers_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (!_isInitialized || DgSuppliers.SelectedItem is not SupplierSummary supplier) return;
            ViewSupplierHistory(supplier);
        }

        private void BtnViewHistory_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not SupplierSummary supplier) return;
            ViewSupplierHistory(supplier);
        }

        private void ViewSupplierHistory(SupplierSummary supplier)
        {
            try
            {
                var allImports = DatabaseHelper.LoadImportHistory();

                var supplierImports = allImports
                    .Where(i =>
                        string.Equals(i.SupplierName, supplier.Name, StringComparison.OrdinalIgnoreCase) &&
                        i.SupplierContact == supplier.Contact)
                    .OrderByDescending(i => i.ImportDate)
                    .ToList();

                var sb = new StringBuilder();
                sb.AppendLine($"📋 Import History: {supplier.Name} ({supplier.Contact})");
                sb.AppendLine(new string('─', 60));
                sb.AppendLine($"Supplier ID:          {supplier.Id}");
                sb.AppendLine($"Total Imports:        {supplier.ImportCount}");
                sb.AppendLine($"Total Items Supplied: {supplier.TotalItemsSupplied:N0}");
                sb.AppendLine($"First Import:         {supplier.FirstImportDate:dd MMM yyyy}");
                sb.AppendLine($"Last Import:          {supplier.LastImportDate:dd MMM yyyy}");
                if (supplier.IsManual)
                    sb.AppendLine($"⭐ Manually Added:    {supplier.ManualSupplier?.CreatedAt:dd MMM yyyy}");
                sb.AppendLine(new string('─', 60));
                sb.AppendLine();

                foreach (var import in supplierImports)
                {
                    sb.AppendLine($"📅 {import.ImportDate:dd MMM yyyy, hh:mm tt}");
                    sb.AppendLine($"   File:  {import.FileName}");
                    sb.AppendLine($"   Items: {import.ItemCount:N0}");
                    sb.AppendLine();
                }

                MessageBox.Show(sb.ToString(),
                    $"Supplier History: {supplier.Name}",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading history: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || _suppliers.Count == 0)
            {
                MessageBox.Show("No suppliers to export.", "Export",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = $"suppliers_{DateTime.Now:yyyyMMdd}.csv",
                Title = "Export Suppliers to CSV"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("Supplier ID,Supplier Name,Contact,First Import,Last Import,Import Count,Total Items Supplied,Type");

                foreach (var s in _suppliers)
                {
                    string escapedName = s.Name.Replace("\"", "\"\"");
                    string type = s.IsManual ? "Manual" : "Imported";
                    sb.AppendLine($"\"{s.Id}\",\"{escapedName}\",{s.Contact},{s.FirstImportDate:yyyy-MM-dd},{s.LastImportDate:yyyy-MM-dd},{s.ImportCount},{s.TotalItemsSupplied},{type}");
                }

                File.WriteAllText(dlg.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"✅ Exported {_suppliers.Count} supplier(s) to:\n{dlg.FileName}",
                    "Export Successful", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Export failed: {ex.Message}", "Export Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Modal: Open ────────────────────────────────────────────────────

        private void BtnAddSupplier_Click(object sender, RoutedEventArgs e)
            => OpenSupplierModal(null);

        private void BtnEditSupplier_Click(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized || sender is not Button btn || btn.Tag is not SupplierSummary supplier) return;

            if (!supplier.IsManual)
            {
                MessageBox.Show("Only manually added suppliers can be edited.\n\nSuppliers from import history are read-only.",
                    "Edit Not Allowed", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OpenSupplierModal(supplier.ManualSupplier);
        }

        private void OpenSupplierModal(Supplier? supplier)
        {
            _editingSupplier = supplier;
            ModalTitle.Text = supplier == null ? "Add New Supplier" : "Edit Supplier";
            ModalIcon.Text = supplier == null ? "🏭" : "✏️";

            TxtModal_Name.Text = supplier?.Name ?? "";
            TxtModal_Contact.Text = supplier?.Contact ?? "";
            TxtModal_Address.Text = supplier?.Address ?? "";
            TxtModal_Email.Text = supplier?.Email ?? "";
            TxtModal_Phone.Text = supplier?.Phone ?? "";
            TxtModal_Notes.Text = supplier?.Notes ?? "";

            ModalOverlay.Visibility = Visibility.Visible;
            TxtModal_Name.Focus();
        }

        // ── Validation Helpers ─────────────────────────────────────────────

        /// <summary>
        /// Returns true when <paramref name="name"/> contains only letters, spaces,
        /// dots, ampersands, hyphens and apostrophes — no digits, no special symbols.
        /// </summary>
        private static bool IsValidSupplierName(string name)
            => !string.IsNullOrWhiteSpace(name) &&
               Regex.IsMatch(name, @"^[A-Za-z\s.\-&']+$");

        /// <summary>
        /// Validates Indian GST number format: 2-digit state code + 10-char PAN
        /// + 1 entity number + Z + 1 checksum = exactly 15 alphanumeric chars.
        /// Pattern: 99AAAAA9999A9Z9
        /// </summary>
        private static bool IsValidGst(string gst)
            => !string.IsNullOrWhiteSpace(gst) &&
               Regex.IsMatch(gst, @"^[0-9]{2}[A-Z]{5}[0-9]{4}[A-Z]{1}[1-9A-Z]{1}Z[0-9A-Z]{1}$");

        /// <summary>
        /// Phone must be exactly 10 digits (no spaces, dashes or country code).
        /// </summary>
        private static bool IsValidPhone(string phone)
            => !string.IsNullOrWhiteSpace(phone) &&
               Regex.IsMatch(phone, @"^\d{10}$");

        /// <summary>
        /// Basic e-mail format check.
        /// </summary>
        private static bool IsValidEmail(string email)
            => string.IsNullOrWhiteSpace(email) ||          // email is optional
               Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$");

        // ── Modal: Save ────────────────────────────────────────────────────

        private void BtnModalSave_Click(object sender, RoutedEventArgs e)
            => TrySaveModal();

        /// <summary>
        /// Validates all fields, saves the supplier, refreshes the grid and closes the modal.
        /// </summary>
        private void TrySaveModal()
        {
            string name = TxtModal_Name.Text.Trim();
            string gst = TxtModal_Contact.Text.Trim().ToUpper();
            string address = TxtModal_Address.Text.Trim();
            string email = TxtModal_Email.Text.Trim();
            string phone = TxtModal_Phone.Text.Trim();
            string notes = TxtModal_Notes.Text.Trim();

            // ── 1. Supplier Name ───────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(name))
            {
                ShowValidationError("Supplier Name is required.", TxtModal_Name);
                return;
            }
            if (!IsValidSupplierName(name))
            {
                ShowValidationError(
                    "Supplier Name must contain only letters, spaces, hyphens, dots, ampersands or apostrophes.\n" +
                    "Numbers and special characters are not allowed.",
                    TxtModal_Name);
                return;
            }
            if (name.Length > MaxNameLength)
            {
                ShowValidationError($"Supplier Name must not exceed {MaxNameLength} characters.", TxtModal_Name);
                return;
            }

            // ── 2. GST Number ──────────────────────────────────────────────
            if (string.IsNullOrWhiteSpace(gst))
            {
                ShowValidationError("GST Number is required.", TxtModal_Contact);
                return;
            }
            if (gst.Length != MaxGstLength)
            {
                ShowValidationError(
                    $"GST Number must be exactly {MaxGstLength} characters long.\n" +
                    $"You entered {gst.Length} character(s).",
                    TxtModal_Contact);
                return;
            }
            if (!IsValidGst(gst))
            {
                ShowValidationError(
                    "GST Number format is invalid.\n\n" +
                    "Expected format: 29ABCDE1234F1Z5\n" +
                    "  • 2-digit state code\n" +
                    "  • 5 letters (PAN)\n" +
                    "  • 4 digits (PAN)\n" +
                    "  • 1 letter (PAN)\n" +
                    "  • 1 entity number\n" +
                    "  • Z\n" +
                    "  • 1 checksum digit",
                    TxtModal_Contact);
                return;
            }

            // ── 3. Address (optional, max length) ─────────────────────────
            if (address.Length > MaxAddressLength)
            {
                ShowValidationError(
                    $"Address must not exceed {MaxAddressLength} characters.\n" +
                    $"You entered {address.Length} character(s).",
                    TxtModal_Address);
                return;
            }

            // ── 4. Email (optional, format + max length) ───────────────────
            if (email.Length > MaxEmailLength)
            {
                ShowValidationError(
                    $"Email must not exceed {MaxEmailLength} characters.\n" +
                    $"You entered {email.Length} character(s).",
                    TxtModal_Email);
                return;
            }
            if (!IsValidEmail(email))
            {
                ShowValidationError(
                    "Email address format is invalid.\nExpected format: name@domain.com",
                    TxtModal_Email);
                return;
            }

            // ── 5. Phone (optional — but if entered must be exactly 10 digits) ──
            if (!string.IsNullOrWhiteSpace(phone))
            {
                if (!IsValidPhone(phone))
                {
                    ShowValidationError(
                        "Phone number must be exactly 10 digits with no spaces, dashes or country code.\n" +
                        $"You entered: \"{phone}\"",
                        TxtModal_Phone);
                    return;
                }
            }

            // ── 6. Notes (optional, max length) ───────────────────────────
            if (notes.Length > MaxNotesLength)
            {
                ShowValidationError(
                    $"Notes must not exceed {MaxNotesLength} characters.\n" +
                    $"You entered {notes.Length} character(s).",
                    TxtModal_Notes);
                return;
            }

            // ── Build / update model ───────────────────────────────────────
            var supplier = _editingSupplier ?? new Supplier();
            supplier.Name = name;
            supplier.Contact = gst;       // already upper-cased
            supplier.Address = address;
            supplier.Email = email;
            supplier.Phone = phone;
            supplier.Notes = notes;

            // ── Persist ───────────────────────────────────────────────────
            if (SuppliersService.AddSupplier(supplier))
            {
                string action = _editingSupplier == null ? "added" : "updated";
                MessageBox.Show($"✅ Supplier {action} successfully!\n\nSupplier ID: {supplier.Id}",
                    "Success", MessageBoxButton.OK, MessageBoxImage.Information);

                CloseModal();
                LoadSuppliers();
            }
            else
            {
                MessageBox.Show(
                    "Failed to save supplier. A supplier with this name and GST may already exist.",
                    "Save Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Shows a validation warning, then moves focus to the offending field.
        /// </summary>
        private static void ShowValidationError(string message, TextBox focusTarget)
        {
            MessageBox.Show(message, "Validation Error",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            focusTarget.Focus();
            focusTarget.SelectAll();
        }

        // ── Modal: Cancel / Close ──────────────────────────────────────────

        private void BtnModalCancel_Click(object sender, RoutedEventArgs e)
            => CloseModal();

        private void CloseModal()
        {
            ModalOverlay.Visibility = Visibility.Collapsed;
            _editingSupplier = null;

            TxtModal_Name.Text = "";
            TxtModal_Contact.Text = "";
            TxtModal_Address.Text = "";
            TxtModal_Email.Text = "";
            TxtModal_Phone.Text = "";
            TxtModal_Notes.Text = "";
        }

        // ── Keyboard Handling ──────────────────────────────────────────────

        private void ModalField_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseModal();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter && sender is TextBox tb)
            {
                if (tb.TabIndex >= 6)
                    TrySaveModal();
                else
                    tb.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));

                e.Handled = true;
            }
        }

        private void ModalOverlay_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                CloseModal();
                e.Handled = true;
            }
        }

        // ── Export Supplier Import Template ───────────────────────────────
        private void BtnExportTemplate_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                FileName = "Supplier_Import_Template.csv",
                Title = "Save Supplier Template"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                string content =
                    "Name,GSTNumber,Address,Email,Phone,Notes\n" +
                    "Example Textiles,29ABCDE1234F1Z5,123 Main St Chennai,supplier@example.com,9876543210,Sample note";

                File.WriteAllText(dlg.FileName, content, Encoding.UTF8);
                TxtStatus.Text = $"Template saved: {Path.GetFileName(dlg.FileName)}";

                MessageBox.Show($"Template saved to:\n{dlg.FileName}\n\n" +
                    "Columns: Name, GSTNumber, Address, Email, Phone, Notes\n" +
                    "Fill in your suppliers and use 'Import' to load them.",
                    "Template Exported", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save template:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Import Suppliers from CSV ──────────────────────────────────────
        private void BtnImportSuppliers_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV Files (*.csv)|*.csv",
                Title = "Import Suppliers from CSV"
            };
            if (dlg.ShowDialog() != true) return;

            try
            {
                var lines = File.ReadAllLines(dlg.FileName, Encoding.UTF8);
                if (lines.Length < 2)
                {
                    MessageBox.Show("The file appears to be empty or has no data rows.",
                        "No Data", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var headers = lines[0].Split(',')
                    .Select((h, i) => (h.Trim().ToLower().Replace(" ", ""), i))
                    .ToDictionary(x => x.Item1, x => x.i);

                string GetCol(string[] cols, string key) =>
                    headers.TryGetValue(key, out int idx) && idx < cols.Length
                        ? cols[idx].Trim().Trim('"')
                        : "";

                int added = 0, skipped = 0;
                var errors = new List<string>();

                for (int i = 1; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;

                    var cols = lines[i].Split(',');
                    string name = GetCol(cols, "name");
                    string gst = GetCol(cols, "gstnumber").ToUpper();
                    string address = GetCol(cols, "address");
                    string email = GetCol(cols, "email");
                    string phone = GetCol(cols, "phone");
                    string notes = GetCol(cols, "notes");

                    // Apply same validation rules as the modal
                    if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(gst))
                    {
                        errors.Add($"Row {i + 1}: Name and GST Number are required — skipped.");
                        skipped++; continue;
                    }
                    if (!IsValidSupplierName(name))
                    {
                        errors.Add($"Row {i + 1}: '{name}' — Name contains invalid characters — skipped.");
                        skipped++; continue;
                    }
                    if (name.Length > MaxNameLength)
                    {
                        errors.Add($"Row {i + 1}: Name exceeds {MaxNameLength} characters — skipped.");
                        skipped++; continue;
                    }
                    if (!IsValidGst(gst))
                    {
                        errors.Add($"Row {i + 1}: '{gst}' is not a valid GST number — skipped.");
                        skipped++; continue;
                    }
                    if (!string.IsNullOrWhiteSpace(phone) && !IsValidPhone(phone))
                    {
                        errors.Add($"Row {i + 1}: Phone '{phone}' must be exactly 10 digits — skipped.");
                        skipped++; continue;
                    }
                    if (!IsValidEmail(email))
                    {
                        errors.Add($"Row {i + 1}: Email '{email}' format is invalid — skipped.");
                        skipped++; continue;
                    }
                    if (address.Length > MaxAddressLength || email.Length > MaxEmailLength || notes.Length > MaxNotesLength)
                    {
                        errors.Add($"Row {i + 1}: One or more fields exceed the maximum allowed length — skipped.");
                        skipped++; continue;
                    }

                    var supplier = new Supplier
                    {
                        Name = name,
                        Contact = gst,
                        Address = address,
                        Email = email,
                        Phone = phone,
                        Notes = notes
                    };

                    if (SuppliersService.AddSupplier(supplier))
                        added++;
                    else
                    {
                        errors.Add($"Row {i + 1}: '{name}' already exists or failed to save.");
                        skipped++;
                    }
                }

                LoadSuppliers();

                string summary = $"Import complete.\n\n✅ Added: {added}\n⚠️ Skipped: {skipped}";
                if (errors.Count > 0)
                    summary += $"\n\nDetails:\n{string.Join("\n", errors.Take(10))}";
                if (errors.Count > 10)
                    summary += $"\n…and {errors.Count - 10} more.";

                TxtStatus.Text = $"Imported {added} supplier(s) from {Path.GetFileName(dlg.FileName)}";
                MessageBox.Show(summary, "Import Complete",
                    MessageBoxButton.OK,
                    skipped > 0 ? MessageBoxImage.Warning : MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Import failed:\n{ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // ── Real-time Input Filters ────────────────────────────────────────────────

        /// <summary>
        /// Blocks any character that isn't a letter, space, or allowed punctuation ( - . & ' )
        /// Fires on every keystroke in the Supplier Name field.
        /// </summary>
        private void TxtModal_Name_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            string filtered = Regex.Replace(tb.Text, @"[^A-Za-z\s.\-&']", "");
            if (filtered == tb.Text) return;          // nothing changed → skip to avoid loop

            int caret = tb.CaretIndex - (tb.Text.Length - filtered.Length);
            tb.Text = filtered;
            tb.CaretIndex = Math.Max(0, Math.Min(caret, filtered.Length));
        }

        /// <summary>
        /// Blocks any character that isn't a digit or uppercase letter in the GST field.
        /// Also auto-uppercases letters as they are typed.
        /// </summary>
        private void TxtModal_Gst_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            string upper = tb.Text.ToUpper();
            string filtered = Regex.Replace(upper, @"[^A-Z0-9]", "");
            if (filtered == tb.Text) return;

            int caret = tb.CaretIndex - (tb.Text.Length - filtered.Length);
            tb.Text = filtered;
            tb.CaretIndex = Math.Max(0, Math.Min(caret, filtered.Length));
        }

        /// <summary>
        /// Blocks any character that isn't a digit in the Phone field.
        /// </summary>
        private void TxtModal_Phone_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (sender is not TextBox tb) return;

            string filtered = Regex.Replace(tb.Text, @"[^0-9]", "");
            if (filtered == tb.Text) return;

            int caret = tb.CaretIndex - (tb.Text.Length - filtered.Length);
            tb.Text = filtered;
            tb.CaretIndex = Math.Max(0, Math.Min(caret, filtered.Length));
        }
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Key == Key.Escape && ModalOverlay.Visibility == Visibility.Visible)
            {
                CloseModal();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.F5 && _isInitialized)
            {
                BtnRefresh_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }

    // ── SupplierSummary ViewModel ──────────────────────────────────────────
    public class SupplierSummary
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string Contact { get; set; } = string.Empty;
        public DateTime FirstImportDate { get; set; }
        public DateTime LastImportDate { get; set; }
        public int ImportCount { get; set; }
        public int TotalItemsSupplied { get; set; }
        public bool IsManual { get; set; }
        public Supplier? ManualSupplier { get; set; }
    }
}