#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using textileshop.Models;
using textileshop.Services;

namespace textileshop
{
    /// <summary>
    /// Settings page for shop branding: name, subtitle, and logo
    /// </summary>
    public partial class SettingsPage : Page
    {
        private AppSettings _settings = new();
        private string? _logoFilePath = null;
        private bool _hasChanges = false;

        public SettingsPage()
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[DEBUG] SettingsPage constructor starting...");

                InitializeComponent();
                System.Diagnostics.Debug.WriteLine("[DEBUG] InitializeComponent() completed");

                // ✅ Debug: Check if critical controls are initialized
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TxtShopName is null: {TxtShopName == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TxtSubtitle is null: {TxtSubtitle == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] ImgLogoPreview is null: {ImgLogoPreview == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TxtLogoPlaceholder is null: {TxtLogoPlaceholder == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] BtnRemoveLogo is null: {BtnRemoveLogo == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] ImgPreviewLogo is null: {ImgPreviewLogo == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TxtPreviewShopName is null: {TxtPreviewShopName == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TxtPreviewSubtitle is null: {TxtPreviewSubtitle == null}");
                System.Diagnostics.Debug.WriteLine($"[DEBUG] TxtStatus is null: {TxtStatus == null}");

                LoadSettings();
                System.Diagnostics.Debug.WriteLine("[DEBUG] SettingsPage constructor finished");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] SettingsPage constructor failed: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Settings page failed to load: {ex.Message}", "Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Load Current Settings ─────────────────────────────────────────
        private void LoadSettings()
        {
            try
            {
                _settings = AppSettings.Load();

                // ✅ Null-safe: Fill form fields only if controls exist
                if (TxtShopName != null)
                    TxtShopName.Text = _settings.ShopName;

                if (TxtSubtitle != null)
                    TxtSubtitle.Text = _settings.Subtitle ?? "Inventory Manager";

                // Load logo if exists
                if (!string.IsNullOrWhiteSpace(_settings.LogoPath) && File.Exists(_settings.LogoPath))
                {
                    LoadLogoPreview(_settings.LogoPath);
                    if (BtnRemoveLogo != null)
                        BtnRemoveLogo.Visibility = Visibility.Visible;
                }
                else
                {
                    if (TxtLogoPlaceholder != null)
                        TxtLogoPlaceholder.Visibility = Visibility.Visible;
                    if (ImgLogoPreview != null)
                        ImgLogoPreview.Source = null;
                    if (ImgPreviewLogo != null)
                        ImgPreviewLogo.Source = null;
                    if (BtnRemoveLogo != null)
                        BtnRemoveLogo.Visibility = Visibility.Collapsed;
                }

                // Update preview
                UpdatePreview();

                _hasChanges = false;
                if (TxtStatus != null)
                    TxtStatus.Text = "Settings loaded successfully";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadSettings failed: {ex.Message}");
                if (TxtStatus != null)
                    TxtStatus.Text = "Error loading settings";
            }
        }

        // ── Load Logo Preview ────────────────────────────────────────────
        private void LoadLogoPreview(string filePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(filePath, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                // ← Removed DecodePixelWidth/Height restriction entirely
                //   so any image size is accepted as-is
                bitmap.EndInit();
                bitmap.Freeze();

                if (ImgLogoPreview != null)
                    ImgLogoPreview.Source = bitmap;
                if (ImgPreviewLogo != null)
                    ImgPreviewLogo.Source = bitmap;
                if (TxtLogoPlaceholder != null)
                    TxtLogoPlaceholder.Visibility = Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadLogoPreview: {ex.Message}");
                if (TxtLogoPlaceholder != null)
                {
                    TxtLogoPlaceholder.Text = "⚠️ Error loading logo";
                    TxtLogoPlaceholder.Visibility = Visibility.Visible;
                }
            }
        }

        // ── Update Live Preview ──────────────────────────────────────────
        private void UpdatePreview()
        {
            if (TxtPreviewShopName != null)
            {
                TxtPreviewShopName.Text = string.IsNullOrWhiteSpace(TxtShopName?.Text)
                    ? "Tharun Textiles"
                    : TxtShopName.Text;
            }

            if (TxtPreviewSubtitle != null)
            {
                TxtPreviewSubtitle.Text = string.IsNullOrWhiteSpace(TxtSubtitle?.Text)
                    ? "Inventory Manager"
                    : TxtSubtitle.Text;
            }
        }

        // ── Event Handlers ───────────────────────────────────────────────

        private void TxtShopName_TextChanged(object sender, TextChangedEventArgs e)
        {
            _hasChanges = true;
            UpdatePreview();
            if (TxtStatus != null)
                TxtStatus.Text = "• Changes pending save";
        }

        private void TxtSubtitle_TextChanged(object sender, TextChangedEventArgs e)
        {
            _hasChanges = true;
            UpdatePreview();
            if (TxtStatus != null)
                TxtStatus.Text = "• Changes pending save";
        }

        private void BtnUploadLogo_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "Image Files (*.png;*.jpg;*.jpeg;*.gif)|*.png;*.jpg;*.jpeg;*.gif",
                Title = "Select Shop Logo"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                // Validate file size (< 500KB)
                var fileInfo = new FileInfo(dlg.FileName);
                if (fileInfo.Length > 500 * 1024)
                {
                    MessageBox.Show("Logo file is too large. Please select an image under 500KB.",
                        "File Too Large", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Copy logo to app data folder for persistence
                string logoFolder = Path.Combine(AppDataHelper.GetDataFolderPath(), "logos");
                Directory.CreateDirectory(logoFolder);

                string extension = Path.GetExtension(dlg.FileName);
                string newLogoPath = Path.Combine(logoFolder, $"shop_logo{extension}");

                File.Copy(dlg.FileName, newLogoPath, true);

                // Load preview
                LoadLogoPreview(newLogoPath);
                _logoFilePath = newLogoPath;
                if (BtnRemoveLogo != null)
                    BtnRemoveLogo.Visibility = Visibility.Visible;

                _hasChanges = true;
                if (TxtStatus != null)
                    TxtStatus.Text = "• Logo uploaded - changes pending save";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Upload logo failed: {ex.Message}");
                MessageBox.Show($"Failed to upload logo: {ex.Message}", "Upload Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRemoveLogo_Click(object sender, RoutedEventArgs e)
        {
            // Remove logo file if it exists
            if (!string.IsNullOrWhiteSpace(_logoFilePath) && File.Exists(_logoFilePath))
            {
                try
                {
                    File.Delete(_logoFilePath);
                }
                catch { /* Ignore delete errors */ }
            }

            // Clear preview
            if (ImgLogoPreview != null)
                ImgLogoPreview.Source = null;
            if (ImgPreviewLogo != null)
                ImgPreviewLogo.Source = null;
            if (TxtLogoPlaceholder != null)
            {
                TxtLogoPlaceholder.Visibility = Visibility.Visible;
                TxtLogoPlaceholder.Text = "🖼️ No logo selected";
            }
            if (BtnRemoveLogo != null)
                BtnRemoveLogo.Visibility = Visibility.Collapsed;
            _logoFilePath = null;

            _hasChanges = true;
            if (TxtStatus != null)
                TxtStatus.Text = "• Logo removed - changes pending save";
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            if (!_hasChanges)
            {
                if (TxtStatus != null) TxtStatus.Text = "No changes to save";
                return;
            }

            try
            {
                if (string.IsNullOrWhiteSpace(TxtShopName?.Text))
                {
                    MessageBox.Show("Shop name cannot be empty.", "Validation",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtShopName?.Focus();
                    return;
                }

                _settings.ShopName = TxtShopName?.Text?.Trim() ?? "Tharun Textiles";
                _settings.Subtitle = string.IsNullOrWhiteSpace(TxtSubtitle?.Text)
                    ? "Inventory Manager"
                    : TxtSubtitle.Text.Trim();
                _settings.LogoPath = _logoFilePath;

                bool success = _settings.Save();

                if (success)
                {
                    if (TxtStatus != null) TxtStatus.Text = "✅ Settings saved successfully!";
                    _hasChanges = false;

                    // ← Call RefreshBranding() which reloads name + subtitle + logo
                    if (Application.Current.Windows
                            .OfType<MainWindow>().FirstOrDefault() is MainWindow main)
                    {
                        main.RefreshBranding();
                    }

                    MessageBox.Show(
                        "✅ Settings updated successfully!\n\nChanges will appear throughout the app.",
                        "Saved", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else
                {
                    if (TxtStatus != null) TxtStatus.Text = "❌ Failed to save settings";
                    MessageBox.Show(
                        "Failed to save settings. Please try again or run as administrator.",
                        "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] Save settings: {ex.Message}");
                if (TxtStatus != null) TxtStatus.Text = "❌ Error saving settings";
                MessageBox.Show($"Error: {ex.Message}", "Save Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Reset all settings to default values?", "Confirm Reset",
                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
            {
                // Reset to defaults
                if (TxtShopName != null)
                    TxtShopName.Text = "Tharun Textiles";
                if (TxtSubtitle != null)
                    TxtSubtitle.Text = "Inventory Manager";

                // Remove logo
                if (!string.IsNullOrWhiteSpace(_logoFilePath) && File.Exists(_logoFilePath))
                {
                    try { File.Delete(_logoFilePath); } catch { }
                }
                if (ImgLogoPreview != null)
                    ImgLogoPreview.Source = null;
                if (ImgPreviewLogo != null)
                    ImgPreviewLogo.Source = null;
                if (TxtLogoPlaceholder != null)
                {
                    TxtLogoPlaceholder.Visibility = Visibility.Visible;
                    TxtLogoPlaceholder.Text = "🖼️ No logo selected";
                }
                if (BtnRemoveLogo != null)
                    BtnRemoveLogo.Visibility = Visibility.Collapsed;
                _logoFilePath = null;

                // Reload defaults
                _settings = new AppSettings(); // Default values
                UpdatePreview();
                _hasChanges = true;
                if (TxtStatus != null)
                    TxtStatus.Text = "• Reset to defaults - click Save to apply";
            }
        }
    }
} 