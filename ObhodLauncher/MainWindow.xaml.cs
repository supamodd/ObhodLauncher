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

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();

            CheckStatus();
        }

        private void SaveSettings()
        {
            try
            {
                string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                string config = $"Discord={(chkDiscord.IsChecked ?? false)}\n" +
                                $"YouTube={(chkYouTube.IsChecked ?? false)}\n" +
                                $"Telegram={(chkTelegram.IsChecked ?? false)}\n" +
                                $"Strategy={cmbStrategy.SelectedIndex}";
                System.IO.File.WriteAllText(configPath, config);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                if (System.IO.File.Exists(configPath))
                {
                    string[] lines = System.IO.File.ReadAllLines(configPath);
                    foreach (string line in lines)
                    {
                        if (line.StartsWith("Discord=")) chkDiscord.IsChecked = bool.Parse(line.Split('=')[1]);
                        if (line.StartsWith("YouTube=")) chkYouTube.IsChecked = bool.Parse(line.Split('=')[1]);
                        if (line.StartsWith("Telegram=")) chkTelegram.IsChecked = bool.Parse(line.Split('=')[1]);
                        if (line.StartsWith("Strategy=")) cmbStrategy.SelectedIndex = int.Parse(line.Split('=')[1]);
                    }
                }
                else
                {
                    // Если конфига нет (первый запуск), ставим дефолтные галочки
                    chkDiscord.IsChecked = true;
                    chkYouTube.IsChecked = true;
                    chkTelegram.IsChecked = false;
                    cmbStrategy.SelectedIndex = 5;
                }
            }
            catch { }
        }

        private void CheckStatus()
        {
            bool isRunning = _engine.IsServiceRunning();

            if (isRunning)
            {
                indicatorStatus.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4ade80");
                ((System.Windows.Media.Effects.DropShadowEffect)indicatorStatus.Effect).Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4ade80");
                txtStatus.Text = "СТАТУС: РАБОТАЕТ В ФОНЕ";
                txtStatus.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4ade80");
            }
            else
            {
                indicatorStatus.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#ef4444");
                ((System.Windows.Media.Effects.DropShadowEffect)indicatorStatus.Effect).Color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444");
                txtStatus.Text = "СТАТУС: ОСТАНОВЛЕН";
                txtStatus.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#a1a1aa");
            }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;
            bool telegram = chkTelegram.IsChecked ?? false;

            int strategy = cmbStrategy.SelectedIndex;

            _engine.Start(discord, youtube, telegram, strategy);
            System.Threading.Thread.Sleep(500);
            CheckStatus();
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
            CheckStatus();
        }

        private void BtnInstallService_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;
            bool telegram = chkTelegram.IsChecked ?? false;

            int strategy = cmbStrategy.SelectedIndex;

            _engine.InstallService(discord, youtube, telegram, strategy);
            System.Threading.Thread.Sleep(1000);
            CheckStatus();
        }

        private void BtnRemoveService_Click(object sender, RoutedEventArgs e)
        {
            _engine.RemoveService();
            System.Threading.Thread.Sleep(500);
            CheckStatus();
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
                case 3: // По умолчанию
                    _engine.SetCustomDNS("По умолчанию", "", "");
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

        private void BtnPatchInstagram_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 0;
            _engine.PatchInstagramHosts();
        }

        private void BtnMediaBypass_Click(object sender, RoutedEventArgs e)
        {
            if (MainTabControl != null) MainTabControl.SelectedIndex = 0;

            _engine.AddMediaBypass();
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