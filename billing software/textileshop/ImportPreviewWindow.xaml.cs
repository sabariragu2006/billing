#nullable enable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using Microsoft.Win32;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    public partial class ImportPreviewWindow : Window
    {
        private readonly ImportHistory _import;
        private string _tempHtmlPath = string.Empty;

        public ImportPreviewWindow(ImportHistory import)
        {
            _import = import;
            InitializeComponent();
            Loaded += async (_, _) => await LoadPreviewAsync();
            Closed += (_, _) => CleanupTempFile();
        }

        // ── Build and load HTML into WebView2 ────────────────────────────
        private async System.Threading.Tasks.Task LoadPreviewAsync()
        {
            try
            {
                TxtStatus.Text = "Loading preview...";

                string html = BuildHtml(_import);

                _tempHtmlPath = Path.Combine(Path.GetTempPath(),
                    $"ImportPreview_{Guid.NewGuid():N}.html");
                File.WriteAllText(_tempHtmlPath, html, Encoding.UTF8);

                await PdfViewer.EnsureCoreWebView2Async(null);
                PdfViewer.CoreWebView2.Navigate(new Uri(_tempHtmlPath).AbsoluteUri);

                Title = $"Import Record — {_import.FileName}";
                TxtStatus.Text =
                    $"Import: {_import.FileName}  •  {_import.ImportDate:dd MMM yyyy, hh:mm tt}";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ERROR] ImportPreviewWindow.LoadPreviewAsync: {ex.Message}");
                TxtStatus.Text = "Preview failed to load.";
            }
        }

        // ── Build HTML document ───────────────────────────────────────────
        private string BuildHtml(ImportHistory record)
        {
            var sb = new StringBuilder();

            var products = record.Products ?? new List<ImportedProductSnapshot>();
            double totalValue = products.Sum(i => i.Qty * i.PurchasePrice);
            double totalQty = products.Sum(i => i.Qty);

            sb.Append(@"
<!DOCTYPE html>
<html>
<head>
<meta charset='utf-8'/>
<style>
    * { margin: 0; padding: 0; box-sizing: border-box; }

    body {
        font-family: 'Segoe UI', Arial, sans-serif;
        font-size: 13px;
        color: #000;
        background: #fff;
        padding: 40px 48px;
    }

    /* ── TOP HEADER ── */
    .doc-header {
        display: flex;
        justify-content: space-between;
        align-items: flex-start;
        padding-bottom: 20px;
        border-bottom: 2px solid #000;
        margin-bottom: 24px;
    }
    .doc-title {
        font-size: 26px;
        font-weight: 700;
        letter-spacing: -0.5px;
        color: #000;
    }
    .doc-subtitle {
        font-size: 12px;
        color: #555;
        margin-top: 4px;
    }
    .doc-meta {
        text-align: right;
        font-size: 12px;
        color: #333;
        line-height: 1.8;
    }
    .doc-meta strong {
        color: #000;
    }

    /* ── INFO GRID ── */
    .info-grid {
        display: grid;
        grid-template-columns: 1fr 1fr;
        gap: 0;
        margin-bottom: 28px;
        border: 1px solid #000;
    }
    .info-cell {
        padding: 12px 16px;
        border-right: 1px solid #000;
        border-bottom: 1px solid #ccc;
    }
    .info-cell:nth-child(even) {
        border-right: none;
    }
    .info-cell:nth-last-child(-n+2) {
        border-bottom: none;
    }
    .info-label {
        font-size: 10px;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 0.8px;
        color: #666;
        margin-bottom: 4px;
    }
    .info-value {
        font-size: 13px;
        font-weight: 600;
        color: #000;
    }

    /* ── PRODUCTS TABLE ── */
    .section-title {
        font-size: 11px;
        font-weight: 700;
        text-transform: uppercase;
        letter-spacing: 1px;
        color: #000;
        margin-bottom: 10px;
        padding-bottom: 6px;
        border-bottom: 1px solid #000;
    }

    table {
        width: 100%;
        border-collapse: collapse;
        margin-bottom: 24px;
    }
    thead tr {
        background: #000;
        color: #fff;
    }
    thead th {
        padding: 10px 12px;
        text-align: left;
        font-size: 11px;
        font-weight: 600;
        letter-spacing: 0.5px;
        text-transform: uppercase;
    }
    thead th.right { text-align: right; }

    tbody tr {
        border-bottom: 1px solid #e5e5e5;
    }
    tbody tr:last-child {
        border-bottom: 1px solid #000;
    }
    tbody tr:nth-child(even) {
        background: #f9f9f9;
    }
    tbody td {
        padding: 10px 12px;
        font-size: 12.5px;
        color: #111;
        vertical-align: middle;
    }
    tbody td.right  { text-align: right; }
    tbody td.center { text-align: center; }
    tbody td.mono   { font-family: 'Courier New', monospace; }

    /* ── EMPTY STATE ── */
    .empty-row td {
        text-align: center;
        padding: 24px;
        color: #888;
        font-style: italic;
    }

    /* ── TOTALS ── */
    .totals-block {
        display: flex;
        justify-content: flex-end;
        margin-bottom: 32px;
    }
    .totals-table {
        width: 320px;
        border: 1px solid #000;
    }
    .totals-row {
        display: flex;
        justify-content: space-between;
        padding: 9px 14px;
        border-bottom: 1px solid #ddd;
        font-size: 13px;
    }
    .totals-row:last-child {
        border-bottom: none;
        background: #000;
        color: #fff;
        font-weight: 700;
        font-size: 14px;
    }
    .totals-row .lbl { color: inherit; }
    .totals-row .val { font-weight: 600; }

    /* ── FOOTER ── */
    .doc-footer {
        border-top: 1px solid #ccc;
        padding-top: 14px;
        display: flex;
        justify-content: space-between;
        font-size: 11px;
        color: #888;
    }

    /* ── PRINT ── */
    @media print {
        body { padding: 20px 28px; }
        .doc-footer { position: fixed; bottom: 0; width: 100%; }
    }
</style>
</head>
<body>");

            // ── DOCUMENT HEADER ──
            sb.Append($@"
<div class='doc-header'>
    <div>
        <div class='doc-title'>PURCHASE RECORD</div>
        <div class='doc-subtitle'>Import History — Textile Shop Inventory</div>
    </div>
    <div class='doc-meta'>
        <div><strong>Record ID</strong> &nbsp; IMP-{record.ImportDate:yyyyMMdd}-{record.ItemCount:000}</div>
        <div><strong>Date</strong> &nbsp; {record.ImportDate:dd MMM yyyy}</div>
        <div><strong>Time</strong> &nbsp; {record.ImportDate:hh:mm tt}</div>
    </div>
</div>");

            // ── INFO GRID ──
            sb.Append($@"
<div class='info-grid'>
    <div class='info-cell'>
        <div class='info-label'>Supplier Name</div>
        <div class='info-value'>{record.SupplierName ?? "—"}</div>
    </div>
    <div class='info-cell'>
        <div class='info-label'>GST / Contact</div>
        <div class='info-value'>{record.SupplierContact ?? "—"}</div>
    </div>
    <div class='info-cell'>
        <div class='info-label'>File Name</div>
        <div class='info-value'>{record.FileName}</div>
    </div>
    <div class='info-cell'>
        <div class='info-label'>Total Items Imported</div>
        <div class='info-value'>{record.ItemCount:N0} products</div>
    </div>
</div>");

            // ── PRODUCTS TABLE ──
            sb.Append("<div class='section-title'>Imported Products</div>");
            sb.Append(@"
<table>
    <thead>
        <tr>
            <th>#</th>
            <th>Product Name</th>
            <th class='right'>Qty</th>
            <th class='right'>Purchase Price (₹)</th>
            <th class='right'>CGST %</th>
            <th class='right'>SGST %</th>
            <th class='right'>Sales Rate (₹)</th>
            <th class='right'>Total Value (₹)</th>
        </tr>
    </thead>
    <tbody>");

            if (products.Count == 0)
            {
                sb.Append(@"
        <tr class='empty-row'>
            <td colspan='8'>No product snapshot available for this import record.</td>
        </tr>");
            }
            else
            {
                int idx = 1;
                foreach (var item in products)
                {
                    double lineTotal = item.Qty * item.PurchasePrice;   // ← total based on purchase price
                    sb.Append($@"
        <tr>
            <td class='center'>{idx++}</td>
            <td>{item.ProductName}</td>
            <td class='right'>{item.Qty}</td>
            <td class='right mono'>{item.PurchasePrice:N2}</td>
            <td class='right'>{item.CGSTPct:N1}</td>
            <td class='right'>{item.SGSTPct:N1}</td>
            <td class='right mono'>{item.Rate:N2}</td>
            <td class='right mono'>{lineTotal:N2}</td>
        </tr>");
                
            }
            }

            sb.Append("</tbody></table>");

            // ── TOTALS ──
            sb.Append($@"
<div class='totals-block'>
    <div class='totals-table'>
        <div class='totals-row'>
            <span class='lbl'>Total Products</span>
            <span class='val'>{record.ItemCount:N0}</span>
        </div>
        <div class='totals-row'>
            <span class='lbl'>Total Units (Qty)</span>
            <span class='val'>{totalQty:N0}</span>
        </div>
        <div class='totals-row'>
           <span class='lbl'>Total Purchase Value</span>
<span class='val'>₹ {totalValue:N2}</span>
        </div>
    </div>
</div>");

            // ── FOOTER ──
            sb.Append($@"
<div class='doc-footer'>
    <span>Generated by Textile Shop Inventory System</span>
    <span>Printed on {DateTime.Now:dd MMM yyyy, hh:mm tt}</span>
</div>

</body></html>");

            return sb.ToString();
        }

        // ── Print via WebView2 ────────────────────────────────────────────
        private async void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await PdfViewer.CoreWebView2.ExecuteScriptAsync("window.print()");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Print failed: {ex.Message}");
                MessageBox.Show("Print failed. Try Save As and print from the saved file.",
                    "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Save As PDF ───────────────────────────────────────────────────
        private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dlg = new SaveFileDialog
                {
                    Filter = "PDF Files (*.pdf)|*.pdf",
                    FileName = $"ImportRecord_{_import.ImportDate:yyyyMMdd}.pdf",
                    Title = "Save Import Record as PDF",
                    InitialDirectory =
                        Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
                };

                if (dlg.ShowDialog() != true) return;

                PdfGenerator.Initialize();
                bool success = PdfGenerator.GenerateImportHistoryPdf(_import, dlg.FileName);

                if (success)
                {
                    var result = MessageBox.Show(
                        $"✅ Saved to:\n{dlg.FileName}\n\nOpen now?",
                        "PDF Saved", MessageBoxButton.YesNo, MessageBoxImage.Information);

                    if (result == MessageBoxResult.Yes)
                    {
                        System.Diagnostics.Process.Start(
                            new System.Diagnostics.ProcessStartInfo
                            {
                                FileName = dlg.FileName,
                                UseShellExecute = true
                            });
                    }
                }
                else
                {
                    MessageBox.Show("Failed to generate PDF.", "Save Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SaveAs failed: {ex.Message}");
                MessageBox.Show($"Error: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Close ─────────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        // ── Cleanup temp file on window close ─────────────────────────────
        private void CleanupTempFile()
        {
            try
            {
                if (!string.IsNullOrEmpty(_tempHtmlPath) && File.Exists(_tempHtmlPath))
                    File.Delete(_tempHtmlPath);
            }
            catch { /* ignore */ }
        }
    }
}