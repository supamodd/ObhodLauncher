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

            // Подписываемся на события логов от движка и выводим их в текстовое поле UI
            _engine.OnLog = (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtLogs.AppendText(message + Environment.NewLine);
                    txtLogs.ScrollToEnd(); // Автоскролл вниз
                });
            };
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;
            _engine.Start(discord, youtube);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
        }

        private void BtnInstallService_Click(object sender, RoutedEventArgs e)
        {
            MessageBoxResult result = MessageBox.Show(
                "Установить zapret как фоновую службу Windows? (Потребуются права администратора)",
                "Установка службы",
                MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                bool discord = chkDiscord.IsChecked ?? false;
                bool youtube = chkYouTube.IsChecked ?? false;
                _engine.InstallService(discord, youtube);
            }
        }

        // Останавливаем процесс, если пользователь закрывает окно программы
        protected override void OnClosed(EventArgs e)
        {
            _engine.Stop();
            base.OnClosed(e);
        }
    }
}