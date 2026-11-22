#nullable enable
using System;
using System.Windows;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    /// <summary>
    /// Interaction logic for ProductEditDialog.xaml
    /// </summary>
    public partial class ProductEditDialog : Window
    {
        private readonly ProductRow _row;

        // ── Constructor ───────────────────────────────────────────────────────
        public ProductEditDialog(ProductRow row)
        {
            InitializeComponent();
            _row = row;

            // ✅ Populate fields AFTER InitializeComponent() ensures controls are ready
            PopulateFields();

            // ✅ Focus first field for better UX
            TxtProductName.Focus();
            TxtProductName.SelectAll();
        }

        // ── Populate fields from the ProductRow ───────────────────────────────
        private void PopulateFields()
        {
            // Set product ID display
            TxtProductId.Text = $"ID: {_row.Id}  ·  Imported: {_row.ImportedAt}";

            // ✅ Populate all TextBoxes with proper formatting
            TxtProductName.Text = _row.ProductName ?? string.Empty;
            TxtQty.Text = _row.Qty.ToString();
            TxtRate.Text = _row.Rate.ToString("N2");
            TxtCostTotal.Text = _row.CostTotal.ToString("N2");
            TxtCGSTPct.Text = _row.CGSTPct.ToString("N1");
            TxtCGSTAmt.Text = _row.CGSTAmt.ToString("N2");
            TxtSGSTPct.Text = _row.SGSTPct.ToString("N1");
            TxtSGSTAmt.Text = _row.SGSTAmt.ToString("N2");
            TxtNetRate.Text = _row.NetRate.ToString("N2");
        }

        // ── Save ──────────────────────────────────────────────────────────────
        private void BtnSaveChanges_Click(object sender, RoutedEventArgs e)
        {
            TxtValidation.Visibility = Visibility.Collapsed;

            // Validate and parse inputs
            string name = TxtProductName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            { ShowValidation("Product Name cannot be empty."); return; }

            if (!int.TryParse(TxtQty.Text, out int qty) || qty < 0)
            { ShowValidation("Qty must be a non-negative whole number."); return; }

            if (!double.TryParse(TxtRate.Text, out double rate) || rate < 0)
            { ShowValidation("Rate must be a valid non-negative number."); return; }

            if (!double.TryParse(TxtCostTotal.Text, out double costTotal))
            { ShowValidation("Cost Total must be a valid number."); return; }

            if (!double.TryParse(TxtCGSTPct.Text, out double cgstPct) || cgstPct < 0)
            { ShowValidation("CGST % must be a valid non-negative number."); return; }

            if (!double.TryParse(TxtCGSTAmt.Text, out double cgstAmt))
            { ShowValidation("CGST Amt must be a valid number."); return; }

            if (!double.TryParse(TxtSGSTPct.Text, out double sgstPct) || sgstPct < 0)
            { ShowValidation("SGST % must be a valid non-negative number."); return; }

            if (!double.TryParse(TxtSGSTAmt.Text, out double sgstAmt))
            { ShowValidation("SGST Amt must be a valid number."); return; }

            if (!double.TryParse(TxtNetRate.Text, out double netRate) || netRate < 0)
            { ShowValidation("Net Rate must be a valid non-negative number."); return; }

            // Write validated values back into the shared ProductRow object
            _row.ProductName = name;
            _row.Qty = qty;
            _row.Rate = rate;
            _row.CostTotal = costTotal;
            _row.CGSTPct = cgstPct;
            _row.CGSTAmt = cgstAmt;
            _row.SGSTPct = sgstPct;
            _row.SGSTAmt = sgstAmt;
            _row.NetRate = netRate;

            // ✅ Persist to SQLite database
            try
            {
                bool success = InventoryService.UpdateProduct(_row.ToProductItem());
                if (!success)
                {
                    ShowValidation("Failed to save changes to database.");
                    return;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Save failed: {ex.Message}");
                ShowValidation($"Database error: {ex.Message}");
                return;
            }

            // Close with success
            DialogResult = true;
            Close();
        }

        // ── Cancel ────────────────────────────────────────────────────────────
        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── Helpers ───────────────────────────────────────────────────────────
        private void ShowValidation(string message)
        {
            TxtValidation.Text = "⚠  " + message;
            TxtValidation.Visibility = Visibility.Visible;
        }
    }
} 