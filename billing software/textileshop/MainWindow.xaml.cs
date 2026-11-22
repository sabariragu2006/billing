#nullable enable
using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using textileshop.Models;

namespace textileshop
{
    public partial class MainWindow : Window
    {
        private readonly DispatcherTimer _clock = new();
        private string _currentPage = "dashboard";
        private Button? _activeNavButton;

        private static readonly NavigationConfig[] NavConfig = new[]
        {
            new NavigationConfig("dashboard",     "Dashboard",        "🏠", "Home",             typeof(DashboardPage)),
            new NavigationConfig("products",      "Products List",    "📦", "Inventory",         typeof(ProductsListPage)),
            new NavigationConfig("import",        "Add Products",     "📂", "Inventory",         typeof(Page2)),
            new NavigationConfig("billing",       "Billing",          "🧾", "Transactions",      typeof(BillingPage)),
            new NavigationConfig("history",       "Bill History",     "📋", "Transactions",      typeof(BillHistoryPage)),
            new NavigationConfig("customers",     "Customers",        "👥", "People",            typeof(CustomersPage)),
            new NavigationConfig("suppliers",     "Suppliers",        "🏭", "People",            typeof(SuppliersPage)),
            new NavigationConfig("return",     "Returns",        "🏭", "People",            typeof(ReturnsPage)),
            new NavigationConfig("sales",         "Sales Report",     "📊", "Reports",           typeof(SalesReportPage)),
            new NavigationConfig("importhistory", "Purchase History", "📥", "Inventory",         typeof(ImportHistoryPage)),
            new NavigationConfig("settings",      "Settings",         "⚙️", "Configuration",    typeof(SettingsPage))
        };

        public MainWindow()
        {
            try
            {
                InitializeComponent();
                LoadShopBranding();
                InitializeClock();
                InitializeNavigation();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Application failed to start:\n{ex.Message}", "Startup Error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Branding ──────────────────────────────────────────────────────
        private void LoadShopBranding()
        {
            try
            {
                var settings = AppSettings.Load();

                if (TxtShopName != null)
                    TxtShopName.Text = settings.ShopName;

                if (TxtShopSubtitle != null)
                    TxtShopSubtitle.Text = string.IsNullOrWhiteSpace(settings.Subtitle)
                        ? "Inventory Manager"
                        : settings.Subtitle;

                LoadShopLogo(settings.LogoPath);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ERROR] LoadShopBranding: {ex.Message}");
            }
        }

        private void LoadShopLogo(string? logoPath)
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(logoPath) && File.Exists(logoPath))
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.UriSource = new Uri(logoPath, UriKind.Absolute);
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    // ← No size restriction — accept any image
                    bitmap.EndInit();
                    bitmap.Freeze();

                    if (ImgShopLogo != null)
                    {
                        ImgShopLogo.Source = bitmap;
                        ImgShopLogo.Visibility = Visibility.Visible;
                    }
                    if (TxtLogoFallbackBox != null)
                        TxtLogoFallbackBox.Visibility = Visibility.Collapsed;
                }
                else
                {
                    if (ImgShopLogo != null)
                        ImgShopLogo.Visibility = Visibility.Collapsed;
                    if (TxtLogoFallbackBox != null)
                        TxtLogoFallbackBox.Visibility = Visibility.Visible;
                }
            }
            catch
            {
                if (ImgShopLogo != null)
                    ImgShopLogo.Visibility = Visibility.Collapsed;
                if (TxtLogoFallbackBox != null)
                    TxtLogoFallbackBox.Visibility = Visibility.Visible;
            }
        }

        public void RefreshBranding() => LoadShopBranding();

        // ── Clock ─────────────────────────────────────────────────────────
        private void InitializeClock()
        {
            _clock.Interval = TimeSpan.FromSeconds(1);
            _clock.Tick += (s, e) =>
            {
                try { TxtClock.Text = DateTime.Now.ToString("ddd dd MMM   HH:mm:ss"); }
                catch { }
            };
            _clock.Start();
            TxtClock.Text = DateTime.Now.ToString("ddd dd MMM   HH:mm:ss");
        }

        // ── Navigation Init ───────────────────────────────────────────────
        private void InitializeNavigation()
        {
            try
            {
                var dashboard = new DashboardPage();
                MainFrame.Content = dashboard;
                UpdatePageHeader("Dashboard", "🏠", "Home");
                _activeNavButton = BtnDashboard;
                _currentPage = "dashboard";
            }
            catch (Exception ex)
            {
                MainFrame.Content = new TextBlock
                {
                    Text = $"Error loading Dashboard: {ex.Message}",
                    Foreground = System.Windows.Media.Brushes.Red,
                    Margin = new Thickness(24),
                    TextWrapping = TextWrapping.Wrap
                };
            }
        }

        // ── Nav Click ─────────────────────────────────────────────────────
        private void NavButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string pageKey)
                NavigateTo(pageKey);
        }

        private void NavigateTo(string pageKey)
        {
            if (_currentPage == pageKey) return;

            var config = Array.Find(NavConfig, c => c.Key == pageKey);
            if (config == null) return;

            // Deactivate old button
            if (_activeNavButton != null)
            {
                var idle = FindResource("NavBtnIdle") as Style;
                if (idle != null) _activeNavButton.Style = idle;
            }

            // Load page with fade animation
            LoadPageWithAnimation(config);

            // Update header
            UpdatePageHeader(config.Title, config.Icon, config.Breadcrumb);

            // Activate new button
            var newBtn = FindNavButton(pageKey);
            if (newBtn != null)
            {
                var active = FindResource("NavBtnActive") as Style;
                if (active != null)
                {
                    newBtn.Style = active;
                    _activeNavButton = newBtn;
                }
            }

            _currentPage = pageKey;
        }

        private void LoadPageWithAnimation(NavigationConfig config)
        {
            try
            {
                var page = (Page)Activator.CreateInstance(config.PageType)!;

                // Fade out current content
                var fadeOut = new DoubleAnimation(1, 0,
                    TimeSpan.FromMilliseconds(80));

                fadeOut.Completed += (s, e) =>
                {
                    MainFrame.Navigate(page);

                    // Fade in new content
                    var fadeIn = new DoubleAnimation(0, 1,
                        TimeSpan.FromMilliseconds(180));
                    MainFrame.BeginAnimation(OpacityProperty, fadeIn);
                };

                MainFrame.BeginAnimation(OpacityProperty, fadeOut);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error loading {config.Title}:\n{ex.Message}",
                    "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdatePageHeader(string title, string icon, string breadcrumb)
        {
            PageTitle.Text = title;
            TxtPageIcon.Text = icon;
            TxtBreadcrumb.Text = breadcrumb;
        }

        private Button? FindNavButton(string pageKey) => pageKey switch
        {
            "dashboard" => BtnDashboard,
            "products" => BtnProducts,
            "import" => BtnImport,
            "billing" => BtnBilling,
            "history" => BtnHistory,
            "customers" => BtnCustomers,
            "return" => BtnReturn,
            "sales" => BtnSalesReport,
            "importhistory" => BtnImportHistory,
            "settings" => BtnSettings,
            _ => null
        };

        private void MainFrame_Navigated(object sender,
            System.Windows.Navigation.NavigationEventArgs e)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[NAV] Navigated to: {e.Content?.GetType().Name}");
        }

        public void NavigateToPage(string pageKey) => NavigateTo(pageKey);

        public void RefreshCurrentPage() => NavigateTo(_currentPage);

        protected override void OnClosed(EventArgs e)
        {
            _clock.Stop();
            base.OnClosed(e);
        }
    }

    internal class NavigationConfig
    {
        public string Key { get; }
        public string Title { get; }
        public string Icon { get; }
        public string Breadcrumb { get; }
        public Type PageType { get; }

        public NavigationConfig(string key, string title, string icon,
                                string breadcrumb, Type pageType)
        {
            Key = key;
            Title = title;
            Icon = icon;
            Breadcrumb = breadcrumb;
            PageType = pageType;
        }
    }
} 