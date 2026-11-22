using System;
using System.Windows;
namespace room_booking
{
    public partial class MainWindow : Window
    {
        private string _currentPage = "";
        public MainWindow()
        {
            InitializeComponent();
            MainNavBar.PageChanged += NavBar_PageChanged;
            NavBar_PageChanged("Dashboard");
        }
        private void NavBar_PageChanged(string pageName)
        {
            if (_currentPage == pageName) return;
            _currentPage = pageName;
            try
            {
                System.Windows.Controls.Page page = pageName switch
                {
                    "Dashboard" => new DashboardPage(),
                    "Room Allocation Page" => new RoomAllocationPage(),
                    "Bills Page" => new BillsPage(),
                    "Guests Page" => new GuestsPage(),
                    "Reservation Page" => new ReservationPage(),
                    "Sales" => new SalesReportPage(),
                    "Orders" => new OrdersPage(),
                    _ => new RoomsPage()
                };
                MainFrame.Navigate(page);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    $"Page: {pageName}\n\nError: {ex.Message}\n\nInner: {ex.InnerException?.Message}\n\nStack:\n{ex.StackTrace}",
                    "Navigation Error",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }
}