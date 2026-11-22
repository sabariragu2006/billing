#nullable enable
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace textileshop
{
    /// <summary>
    /// Print preview window for billing - shows formatted bill and handles printing
    /// </summary>
    public partial class PrintPreviewWindow : Window
    {
        private readonly string _billText;
        private readonly string _billNumber;

        /// <summary>
        /// Set to true to hide the Print button (preview-only mode)
        /// </summary>
        public bool IsPreviewOnly { get; set; } = false;

        public PrintPreviewWindow(string billText, string billNumber = "")
        {
            InitializeComponent();
            _billText = billText;
            _billNumber = billNumber;

            // Set preview text
            TxtBillPreview.Text = _billText;
            TxtPreviewInfo.Text = string.IsNullOrEmpty(_billNumber)
                ? "Preview mode"
                : $"Preview: {_billNumber}";

            // Hide print button if preview-only mode
            if (IsPreviewOnly && BtnPrint != null)
            {
                BtnPrint.Visibility = Visibility.Collapsed;
            }
        }

        // ── Close Button ─────────────────────────────────────────────────────
        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ── Copy to Clipboard ────────────────────────────────────────────────
        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(_billText);

                // Show temporary success feedback
                var btn = sender as Button;
                if (btn != null)
                {
                    var originalContent = btn.Content;
                    btn.Content = "✅ Copied!";
                    btn.IsEnabled = false;

                    // Restore after 2 seconds
                    Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        btn.Content = originalContent;
                        btn.IsEnabled = true;
                    }), System.Windows.Threading.DispatcherPriority.Background, TimeSpan.FromSeconds(2));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not copy to clipboard: {ex.Message}",
                    "Copy Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        // ── Print Button ─────────────────────────────────────────────────────
        private void BtnPrint_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var printDialog = new PrintDialog();

                if (printDialog.ShowDialog() == true)
                {
                    // Create simple printable TextBox
                    var textBox = new TextBox
                    {
                        Text = _billText,
                        FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                        FontSize = 10,
                        TextWrapping = TextWrapping.Wrap,
                        Width = printDialog.PrintableAreaWidth - 60,
                        Background = System.Windows.Media.Brushes.Transparent,
                        BorderThickness = new Thickness(0)
                    };

                    // Measure and arrange for proper printing
                    textBox.Measure(new Size(printDialog.PrintableAreaWidth, double.PositiveInfinity));
                    textBox.Arrange(new Rect(0, 0, textBox.DesiredSize.Width, textBox.DesiredSize.Height));

                    // Print
                    printDialog.PrintVisual(textBox,
                        string.IsNullOrEmpty(_billNumber) ? "TextileShop Bill" : _billNumber);

                    MessageBox.Show("Bill sent to printer!", "Printed",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                // Fallback: Copy to clipboard if printing fails
                try
                {
                    Clipboard.SetText(_billText);
                    MessageBox.Show(
                        $"Print failed: {ex.Message}\n\n✅ Bill copied to clipboard instead.\nPaste into Notepad to print.",
                        "Print Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                catch
                {
                    MessageBox.Show($"Error: {ex.Message}", "Error",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ── Keyboard Shortcuts ───────────────────────────────────────────────
        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            base.OnKeyDown(e);

            // Esc to close
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                Close();
                e.Handled = true;
            }
            // Ctrl+P to print
            else if (e.Key == System.Windows.Input.Key.P &&
                     (Keyboard.IsKeyDown(System.Windows.Input.Key.LeftCtrl) ||
                      Keyboard.IsKeyDown(System.Windows.Input.Key.RightCtrl)))
            {
                BtnPrint_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
            // Ctrl+C to copy
            else if (e.Key == System.Windows.Input.Key.C &&
                     (Keyboard.IsKeyDown(System.Windows.Input.Key.LeftCtrl) ||
                      Keyboard.IsKeyDown(System.Windows.Input.Key.RightCtrl)))
            {
                BtnCopy_Click(this, new RoutedEventArgs());
                e.Handled = true;
            }
        }
    }
}