#nullable enable
using System;
using System.IO;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using textileshop.Models;

namespace textileshop.Services
{
    /// <summary>
    /// Generates PDF bills using QuestPDF
    /// Handles initialization safely for both debug and published apps
    /// </summary>
    public static class PdfGenerator
    {
        private static bool _isInitialized = false;
        private static Exception? _initError = null;

        /// <summary>
        /// Initialize QuestPDF safely (call once at app startup)
        /// </summary>
        public static void Initialize()
        {
            if (_isInitialized) return;

            try
            {
                // ✅ Set license BEFORE any QuestPDF usage (critical!)
                QuestPDF.Settings.License = LicenseType.Community;

                // ✅ Disable safety checks for production (faster, fewer font checks)
                QuestPDF.Settings.EnableCaching = true;
                QuestPDF.Settings.EnableDebugging = false;

                // ✅ Test with minimal doc to verify native libs are loaded
                var testDoc = Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A5);
                        page.Margin(1, Unit.Centimetre);
                        // ✅ Use fallback fonts for published apps
                        page.DefaultTextStyle(x => x.FontSize(10f).FontFamily("Segoe UI, Arial, sans-serif"));
                        page.Content().Text("Test").FontSize(10f);
                    });
                });

                // Generate to memory to verify everything works
                using var ms = new MemoryStream();
                testDoc.GeneratePdf(ms);

                _isInitialized = true;
                _initError = null;

                System.Diagnostics.Debug.WriteLine("[INFO] PdfGenerator initialized successfully");
            }
            catch (Exception ex)
            {
                _initError = ex;
                _isInitialized = false;

                System.Diagnostics.Debug.WriteLine($"[ERROR] PdfGenerator initialization failed: {ex.Message}\n{ex.StackTrace}");

                // Don't throw here - let GenerateBillPdf handle gracefully
            }
        }
        public static bool GenerateImportHistoryPdf(ImportHistory import, string outputPath)
        {
            try
            {
                var settings = AppSettings.Load();

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(2, Unit.Centimetre);
                        page.DefaultTextStyle(x => x.FontSize(10f).FontFamily("Segoe UI"));

                        page.Header().Column(column =>
                        {
                            column.Item().AlignCenter().Text(settings.ShopName ?? "Shop Name").FontSize(16f).Bold();
                            if (!string.IsNullOrWhiteSpace(settings.ShopAddress))
                                column.Item().AlignCenter().Text(settings.ShopAddress).FontSize(9f).FontColor(Colors.Grey.Medium);
                            if (!string.IsNullOrWhiteSpace(settings.ShopPhone))
                                column.Item().AlignCenter().Text($"📞 {settings.ShopPhone}").FontSize(9f).FontColor(Colors.Grey.Medium);
                            column.Item().PaddingTop(10).AlignCenter().Text("Import Record").FontSize(14f).Bold().FontColor(Colors.Blue.Medium);
                            column.Item().PaddingTop(5).LineHorizontal(1f).LineColor(Colors.Grey.Lighten2);
                        });

                        page.Content().PaddingVertical(1, Unit.Centimetre).Column(column =>
                        {
                            column.Item().Text($"📅 Date: {import.ImportDate:dd MMM yyyy, hh:mm tt}").FontSize(11f);
                            column.Item().Text($"📄 File: {import.FileName}").FontSize(11f);
                            column.Item().Text($"👤 Supplier: {import.SupplierName ?? "—"}").FontSize(11f);
                            column.Item().Text($"📞 Contact: {import.SupplierContact ?? "—"}").FontSize(11f);
                            column.Item().Text($"📦 Items: {import.ItemCount:N0}").FontSize(11f).Bold();

                            if (import.Products != null && import.Products.Any())
                            {
                                column.Item().PaddingTop(20).Text("Imported Products").FontSize(12f).Bold();
                                column.Item().PaddingTop(5).Row(row =>
                                {
                                    row.RelativeItem(3).Text("Product").Bold().FontSize(10f);
                                    row.RelativeItem(1).AlignRight().Text("Qty").Bold().FontSize(10f);
                                    row.RelativeItem(1).AlignRight().Text("Rate").Bold().FontSize(10f);
                                    row.RelativeItem(1).AlignRight().Text("Total").Bold().FontSize(10f);
                                });
                                column.Item().LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                                foreach (var p in import.Products)
                                {
                                    column.Item().Row(row =>
                                    {
                                        row.RelativeItem(3).Text(p.ProductName).FontSize(9f);
                                        row.RelativeItem(1).AlignRight().Text($"{p.Qty}").FontSize(9f);
                                        row.RelativeItem(1).AlignRight().Text($"₹{p.Rate:N2}").FontSize(9f);
                                        row.RelativeItem(1).AlignRight().Text($"₹{p.NetRate:N2}").FontSize(9f).Bold();
                                    });
                                }

                                double total = import.Products.Sum(p => p.NetRate);
                                int qty = import.Products.Sum(p => p.Qty);
                                column.Item().PaddingTop(8).LineHorizontal(1f).LineColor(Colors.Grey.Medium);
                                column.Item().Row(row =>
                                {
                                    row.RelativeItem(4).Text($"Import Total ({qty} items):").Bold().FontSize(11f);
                                    row.RelativeItem(1).AlignRight().Text($"₹{total:N2}").Bold().FontSize(11f).FontColor(Colors.Green.Medium);
                                });
                            }
                        });

                        page.Footer().AlignCenter().Text(x =>
                        {
                            x.Span("Generated: ").FontSize(9f).FontColor(Colors.Grey.Medium);
                            x.Span(DateTime.Now.ToString("dd MMM yyyy, hh:mm tt")).FontSize(9f);
                            x.Span("  •  Textile Inventory System").FontSize(9f).FontColor(Colors.Grey.Medium);
                        });
                    });
                }).GeneratePdf(outputPath);

                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] GenerateImportHistoryPdf: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Generate a PDF bill and save to file
        /// </summary>
        public static bool GenerateBillPdf(Bill bill, string filePath)
        {
            // ✅ Ensure initialization (lazy load)
            if (!_isInitialized)
            {
                Initialize();
            }

            // ✅ Check for initialization errors
            if (_initError != null)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Cannot generate PDF: {_initError.Message}");
                return false;
            }

            if (!_isInitialized)
            {
                System.Diagnostics.Debug.WriteLine("[ERROR] PdfGenerator not initialized");
                return false;
            }

            try
            {
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        // ✅ Use fallback fonts for published apps
                        page.DefaultTextStyle(x => x.FontSize(10f).FontFamily("Segoe UI, Arial, sans-serif"));

                        // Page setup
                        page.Size(PageSizes.A5);
                        page.Margin(2, Unit.Centimetre);

                        // Header
                        page.Header()
                            .Element(c => c.Column(column =>
                            {
                                var settings = AppSettings.Load();

                                // ✅ FIX: Alignment BEFORE .Text()
                                column.Item().AlignCenter().Text(settings.ShopName).FontSize(16f).Bold();

                                if (!string.IsNullOrWhiteSpace(settings.ShopAddress))
                                {
                                    column.Item().AlignCenter().Text(settings.ShopAddress).FontSize(9f).FontColor(Colors.Grey.Medium);
                                }
                                if (!string.IsNullOrWhiteSpace(settings.ShopPhone))
                                {
                                    column.Item().AlignCenter().Text($"📞 {settings.ShopPhone}").FontSize(9f).FontColor(Colors.Grey.Medium);
                                }

                                column.Item().AlignCenter().Text("Invoice").FontSize(12f).FontColor(Colors.Grey.Medium);
                                column.Item().PaddingTop(5).LineHorizontal(1f).LineColor(Colors.Grey.Lighten2);
                            }));

                        // Content
                        page.Content()
                            .PaddingVertical(1, Unit.Centimetre)
                            .Column(column =>
                            {
                                // Bill info
                                column.Item().Row(row =>
                                {
                                    row.RelativeItem().Column(col =>
                                    {
                                        col.Item().Text($"Bill No: {bill.BillNumberFormatted}").Bold();
                                        col.Item().Text($"Date: {bill.BillDate:dd MMM yyyy, hh:mm tt}");
                                        col.Item().Text($"Customer: {bill.CustomerName}");
                                        col.Item().Text($"Mobile: {bill.CustomerMobile}");
                                    });
                                });

                                // Items table header
                                column.Item().PaddingTop(10).Row(row =>
                                {
                                    // ✅ FIX: Alignment BEFORE .Text()
                                    row.RelativeItem(3).Text("Product").Bold();
                                    row.RelativeItem(1).AlignRight().Text("Rate").Bold();
                                    row.RelativeItem(1).AlignCenter().Text("Qty").Bold();
                                    row.RelativeItem(1).AlignRight().Text("Total").Bold();
                                });

                                column.Item().PaddingTop(2).LineHorizontal(0.5f).LineColor(Colors.Grey.Lighten1);

                                // Items
                                foreach (var item in bill.Items ?? System.Linq.Enumerable.Empty<BillItem>())
                                {
                                    column.Item().Row(row =>
                                    {
                                        // ✅ FIX: Alignment BEFORE .Text()
                                        row.RelativeItem(3).Text(item.ProductName);
                                        row.RelativeItem(1).AlignRight().Text($"{item.Rate:N2}");
                                        row.RelativeItem(1).AlignCenter().Text($"{item.Quantity}");
                                        row.RelativeItem(1).AlignRight().Text($"{item.Total:N2}");
                                    });
                                }
                                // Totals
                                column.Item().PaddingTop(10).LineHorizontal(1f).LineColor(Colors.Grey.Lighten2);

                                column.Item().Row(row =>
                                {
                                    row.RelativeItem(4).Text("Subtotal:");
                                    row.RelativeItem(1); // spacer
                                    row.RelativeItem(2).AlignRight().Text($"₹{bill.SubTotal:N2}");
                                });

                                column.Item().Row(row =>
                                {
                                    row.RelativeItem(4).Text("CGST:");
                                    row.RelativeItem(1);
                                    row.RelativeItem(2).AlignRight().Text($"₹{bill.TotalCGST:N2}");
                                });

                                column.Item().Row(row =>
                                {
                                    row.RelativeItem(4).Text("SGST:");
                                    row.RelativeItem(1);
                                    row.RelativeItem(2).AlignRight().Text($"₹{bill.TotalSGST:N2}");
                                });


                                column.Item().Row(row =>
                                {
                                    row.RelativeItem(4).Text("Grand Total:").Bold().FontSize(12f);
                                    row.RelativeItem(1); // spacer
                                    row.RelativeItem(2).AlignRight()
                                       .Text($"₹{bill.GrandTotal:N2}")
                                       .Bold().FontSize(12f).FontColor(Colors.Green.Medium);
                                });
                               

                             

                               
                            });

                        // Footer
                        page.Footer()
                            .AlignCenter()
                            .Text(x =>
                            {
                                x.Span("Thank you for your purchase!  •  ");
                                x.Span($"Generated: {DateTime.Now:dd MMM yyyy, hh:mm tt}").FontColor(Colors.Grey.Medium);
                            });
                    });
                })
                .GeneratePdf(filePath);

                System.Diagnostics.Debug.WriteLine($"[INFO] PDF generated: {filePath}");
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] PDF generation failed: {ex.Message}\n{ex.StackTrace}");
                return false;
            }
        }

        /// <summary>
        /// Generate PDF and open with default PDF viewer
        /// </summary>
        public static bool GenerateAndOpenPdf(Bill bill)
        {
            try
            {
                string tempPath = Path.Combine(Path.GetTempPath(), $"Bill_{bill.BillNumberFormatted}.pdf");

                if (GenerateBillPdf(bill, tempPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = tempPath,
                        UseShellExecute = true
                    });
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Open PDF failed: {ex.Message}");
                return false;
            }
        }
    }
}