using System;
using System.Windows;
using System.Windows.Controls;

namespace room_booking
{
    public partial class NavBar : UserControl
    {
        public event Action<string>? PageChanged;
        private Button? _activeButton;

        public NavBar()
        {
            InitializeComponent();
            SetActiveButton(BtnDashboard);
            PageChanged?.Invoke("Dashboard");
        }

        private void BtnDashboard_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnDashboard);
            PageChanged?.Invoke("Dashboard");
        }
        private void BtnSales_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnSales);
            PageChanged?.Invoke("Sales");
        }
        private void BtnOrders_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnOrders);
            PageChanged?.Invoke("Orders");
        }

        private void BtnRoomAllocation_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnRoomAllocation);
            PageChanged?.Invoke("Room Allocation Page");
        }

        private void BtnGuests_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnGuests);
            PageChanged?.Invoke("Guests Page");
        }

        private void BtnBills_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnBills);
            PageChanged?.Invoke("Bills Page");
        }


        private void BtnReservation_Click(object sender, RoutedEventArgs e)
        {
            SetActiveButton(BtnReservation);
            PageChanged?.Invoke("Reservation Page");
        }

        private void SetActiveButton(Button activeBtn)
        {
            // Added BtnSales to the array so it properly resets the other buttons
            Button[] allButtons = { BtnDashboard, BtnRoomAllocation,BtnOrders, BtnGuests, BtnBills, BtnReservation, BtnSales };

            foreach (var btn in allButtons)
                btn.Style = (Style)Resources["NavBtnIdle"];

            activeBtn.Style = (Style)Resources["NavBtnActive"];
            _activeButton = activeBtn;
        }
    }
}