#nullable enable
using System;
using System.IO;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    /// <summary>
    /// Modal window to preview a bill as a professional PDF using WebView2
    /// </summary>
    public partial class BillPreviewWindow : Window
    {
        private readonly Bill _bill;
        private string? _tempPdfPath;

        public BillPreviewWindow(Bill bill)
        {
            InitializeComponent(); // ✅ This will work once XAML is fixed
            _bill = bill;
            Loaded += BillPreviewWindow_Loaded;
            Closed += BillPreviewWindow_Closed;
        }

        // In BillPreviewWindow.xaml.cs, Loaded event:
        private async void BillPreviewWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                TxtStatus.Text = "Generating preview...";

                // Generate temporary PDF for preview
                _tempPdfPath = Path.Combine(Path.GetTempPath(), $"Preview_{_bill.BillNumberFormatted}.pdf");

                // Clean up existing temp file
                if (File.Exists(_tempPdfPath))
                {
                    try { File.Delete(_tempPdfPath); }
                    catch { /* Ignore if locked */ }
                }

                // Generate professional PDF using QuestPDF
                bool success = PdfGenerator.GenerateBillPdf(_bill, _tempPdfPath);

                if (success && File.Exists(_tempPdfPath))
                {
                    // Initialize WebView2
                    await PdfViewer.EnsureCoreWebView2Async(null); // ✅ Pass null for default options

                    // Load the PDF in the viewer
                    PdfViewer.Source = new Uri(_tempPdfPath);
                    TxtStatus.Text = "Preview loaded";
                }
                else
                {
                    TxtStatus.Text = "Failed to generate preview";
                    MessageBox.Show("Could not generate bill preview.", "Preview Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    Close();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Preview load failed: {ex.Message}");
                TxtStatus.Text = "Error loading preview";

                // Fallback: Open PDF in default viewer
                if (!string.IsNullOrEmpty(_tempPdfPath) && File.Exists(_tempPdfPath))
                {
                    try
                    {
                        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                        {
                            FileName = _tempPdfPath,
                            UseShellExecute = true
                        });
                        Close();
                        return;
                    }
                    catch { /* Ignore */ }
                }

                MessageBox.Show($"Preview error: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Close();
            }
        }
        private void BillPreviewWindow_Closed(object? sender, EventArgs e)
        {
            // Clean up temp PDF file
            if (!string.IsNullOrEmpty(_tempPdfPath) && File.Exists(_tempPdfPath))
            {
                try { File.Delete(_tempPdfPath); }
                catch { /* Ignore if locked */ }
            }
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e) => Close();

        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            // Trigger print from WebView2
            _ = PdfViewer.ExecuteScriptAsync("window.print();");
        }

        private void BtnSaveAs_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.SaveFileDialog
            {
                Filter = "PDF Files (*.pdf)|*.pdf",
                FileName = $"Bill_{_bill.BillNumberFormatted}.pdf",
                Title = "Save Bill as PDF",
                InitialDirectory = Directory.Exists(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bills", "pdf"))
                    ? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bills", "pdf")
                    : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    // Copy the temp PDF to the chosen location
                    if (!string.IsNullOrEmpty(_tempPdfPath))
                    {
                        File.Copy(_tempPdfPath, dlg.FileName, overwrite: true);
                        MessageBox.Show($"✅ Bill saved to:\n{dlg.FileName}", "Saved",
                            MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Save failed: {ex.Message}", "Error",

                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}