using System;
using System.Windows;

namespace ZapretWPF
{
    public partial class MainWindow : Window
    {
        private ZapretEngine _engine;

        public MainWindow()
        {
            InitializeComponent();
            _engine = new ZapretEngine();

            _engine.OnLog = (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtLogs.AppendText(message + Environment.NewLine);
                    txtLogs.ScrollToEnd();
                });
            };
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;

            int strategy = cmbStrategy.SelectedIndex;

            _engine.Start(discord, youtube, strategy);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
        }

        private void BtnInstallService_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;
            int strategy = cmbStrategy.SelectedIndex;

            _engine.InstallService(discord, youtube, strategy);
        }

        private void BtnRemoveService_Click(object sender, RoutedEventArgs e)
        {
            _engine.RemoveService();
        }

        private void BtnFlushDns_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 0;
            _engine.FlushDNS();
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            await _engine.TestConnectionAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine.Stop();
            base.OnClosed(e);
        }

        private async void BtnUpdateLists_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as System.Windows.Controls.Button;
            if (btn != null) btn.IsEnabled = false;

            await _engine.UpdateListsAsync();

            if (btn != null) btn.IsEnabled = true;
        }

        private void BtnSetDns_Click(object sender, RoutedEventArgs e)
        {
            // Переключаем на первую вкладку для показа логов
            if (MainTabControl != null) MainTabControl.SelectedIndex = 0;

            int selectedDns = cmbDnsSelection.SelectedIndex;

            switch (selectedDns)
            {
                case 0: // Cloudflare
                    _engine.SetCustomDNS("Cloudflare", "1.1.1.1", "1.0.0.1");
                    break;

                case 1: // Google
                    _engine.SetCustomDNS("Google DNS", "8.8.8.8", "8.8.4.4");
                    break;

                case 2: // XBOX DNS
                    _engine.SetCustomDNS("XBOX DNS", "111.88.96.50", "111.88.96.51");
                    break;
            }
        }

        // --- МЕТОДЫ КАСТОМНОЙ ПАНЕЛИ УПРАВЛЕНИЯ ОКНОМ ---

        private void Header_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
            Close();
        }
    }
}