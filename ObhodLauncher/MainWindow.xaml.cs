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

            // Получаем индекс выбранной стратегии из ComboBox
            int strategy = cmbStrategy.SelectedIndex;

            // Обязательно вызываем метод с 3 аргументами!
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
            // Надежный способ переключить вкладку на первую (индекс 0) без использования x:Name
            if (this.Content is System.Windows.Controls.Grid mainGrid)
            {
                foreach (var child in mainGrid.Children)
                {
                    if (child is System.Windows.Controls.TabControl tabControl)
                    {
                        tabControl.SelectedIndex = 0;
                        break;
                    }
                }
            }

            _engine.FlushDNS();
        }

        private async void BtnTestConnection_Click(object sender, RoutedEventArgs e)
        {
            // Переключаем на вкладку с логами
            if (this.Content is System.Windows.Controls.Grid mainGrid)
            {
                foreach (var child in mainGrid.Children)
                {
                    if (child is System.Windows.Controls.TabControl tabControl)
                    {
                        tabControl.SelectedIndex = 0;
                        break;
                    }
                }
            }

            await _engine.TestConnectionAsync();
        }

        protected override void OnClosed(EventArgs e)
        {
            _engine.Stop();
            base.OnClosed(e);
        }
    }
}