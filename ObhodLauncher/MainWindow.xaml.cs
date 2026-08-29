using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using Forms = System.Windows.Forms;

namespace ZapretWPF
{
    public partial class MainWindow : Window
    {
        private Forms.NotifyIcon? _trayIcon;
        private bool _allowRealClose = false;

        private Icon? _trayOnIcon;
        private Icon? _trayOffIcon;
        private DispatcherTimer? _trayStatusTimer;

        private ZapretEngine _engine;
        private KvnEngine _kvnEngine;
        private KvnVpnEngine _kvnVpnEngine;
        private System.Collections.Generic.List<KvnServerConfig> _kvnServers = new();
        private string _kvnRawSubscription = "";


        public MainWindow()
        {
            InitializeComponent();

            InitializeTrayIcon();

            this.Closing += MainWindow_Closing;

            _engine = new ZapretEngine();
            _kvnEngine = new KvnEngine();
            _kvnVpnEngine = new KvnVpnEngine();

            _engine.OnLog = (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtLogs.AppendText(
                        message + Environment.NewLine);

                    txtLogs.ScrollToEnd();
                });
            };

            _kvnEngine.OnLog = (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtLogs.AppendText(
                        message + Environment.NewLine);

                    txtLogs.ScrollToEnd();
                });
            };

            _kvnVpnEngine.OnLog = (message) =>
            {
                Dispatcher.Invoke(() =>
                {
                    txtLogs.AppendText(
                        message + Environment.NewLine);

                    txtLogs.ScrollToEnd();
                });
            };

            this.Loaded += MainWindow_Loaded;
        }

        private void InitializeTrayIcon()
        {
            string baseDirectory =
                AppDomain.CurrentDomain.BaseDirectory;

            string trayOnPath =
                System.IO.Path.Combine(
                    baseDirectory,
                    "ObhodIconTrayOn.ico");

            string trayOffPath =
                System.IO.Path.Combine(
                    baseDirectory,
                    "ObhodIconTrayOff.ico");

            /*
             * Загружаем иконки из папки рядом с exe.
             */
            if (System.IO.File.Exists(trayOnPath))
            {
                _trayOnIcon = new Icon(trayOnPath);
            }

            if (System.IO.File.Exists(trayOffPath))
            {
                _trayOffIcon = new Icon(trayOffPath);
            }

            /*
             * Если иконки не найдены, используем стандартную,
             * чтобы приложение всё равно запустилось.
             */
            Icon defaultIcon =
                _trayOffIcon ??
                SystemIcons.Application;

            _trayIcon = new Forms.NotifyIcon
            {
                Icon = defaultIcon,
                Text = "ObhodLauncher",
                Visible = false
            };

            var trayMenu =
                new Forms.ContextMenuStrip();

            var showItem =
                new Forms.ToolStripMenuItem(
                    "Открыть ObhodLauncher");

            showItem.Click += (sender, e) =>
            {
                ShowFromTray();
            };

            var exitItem =
                new Forms.ToolStripMenuItem(
                    "Закрыть полностью");

            exitItem.Click += (sender, e) =>
            {
                _allowRealClose = true;

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                }

                Close();
            };

            trayMenu.Items.Add(showItem);
            trayMenu.Items.Add(new Forms.ToolStripSeparator());
            trayMenu.Items.Add(exitItem);

            _trayIcon.ContextMenuStrip = trayMenu;

            _trayIcon.DoubleClick += (sender, e) =>
            {
                ShowFromTray();
            };

            /*
             * Проверяем состояние winws.exe раз в секунду.
             */
            _trayStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };

            _trayStatusTimer.Tick += (sender, e) =>
            {
                UpdateTrayIcon();
            };

            _trayStatusTimer.Start();

            UpdateTrayIcon();
        }

        private bool IsWinwsRunning()
        {
            try
            {
                Process[] processes =
                    Process.GetProcessesByName("winws");

                foreach (Process process in processes)
                {
                    try
                    {
                        if (!process.HasExited)
                        {
                            return true;
                        }
                    }
                    catch
                    {
                        // Процесс мог завершиться во время проверки
                    }
                }
            }
            catch
            {
                // Не удалось проверить список процессов
            }

            return false;
        }
        private void UpdateTrayIcon()
        {
            if (_trayIcon == null)
            {
                return;
            }

            bool winwsRunning =
                IsWinwsRunning();

            if (winwsRunning)
            {
                if (_trayOnIcon != null)
                {
                    _trayIcon.Icon = _trayOnIcon;
                }

                _trayIcon.Text =
                    "ObhodLauncher — обход работает";
            }
            else
            {
                if (_trayOffIcon != null)
                {
                    _trayIcon.Icon = _trayOffIcon;
                }

                _trayIcon.Text =
                    "ObhodLauncher — обход выключен";
            }
        }
        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;

            if (_trayIcon != null)
            {
                _trayIcon.Visible = true;
                _trayIcon.ShowBalloonTip(
                    1500,
                    "ObhodLauncher",
                    "Программа продолжает работать в трее.",
                    Forms.ToolTipIcon.Info);
            }
        }

        private void MainWindow_Closing(
    object? sender,
    CancelEventArgs e)
        {
            /*
             * Если закрытие было вызвано пунктом
             * «Закрыть полностью» в меню трея,
             * окно выбора не показываем.
             */
            if (_allowRealClose)
            {
                return;
            }

            /*
             * Отменяем первоначальное закрытие,
             * показываем собственное окно выбора.
             */
            e.Cancel = true;

            var dialog = new CloseChoiceWindow
            {
                Owner = this
            };

            bool? result = dialog.ShowDialog();

            if (result == true)
            {
                /*
                 * Кнопка «Да»:
                 * полностью закрываем программу.
                 */
                _allowRealClose = true;

                if (_trayIcon != null)
                {
                    _trayIcon.Visible = false;
                }

                Close();
            }
            else
            {
                /*
                 * Кнопка «Оставить в трее»:
                 * окно скрывается, а приложение продолжает работу.
                 */
                HideToTray();
            }
        }

        private void ShowFromTray()
        {
            Show();
            ShowInTaskbar = true;
            WindowState = WindowState.Normal;
            Activate();
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            KvnUpdateCoreInfo();
            UpdateInstalledStrategyInfo();
        }

        private void SaveSettings()
        {
            try
            {
                string configPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.txt");
                string config = $"Discord={(chkDiscord.IsChecked ?? false)}\n" +
                                $"YouTube={(chkYouTube.IsChecked ?? false)}\n" +
                                $"Telegram={(chkTelegram.IsChecked ?? false)}\n" +
                                $"Strategy={cmbStrategy.SelectedIndex}\n" +
                                $"KvnUrl={txtKvnSubscriptionUrl.Text}";
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
                        if (line.StartsWith("KvnUrl=")) txtKvnSubscriptionUrl.Text = line.Substring("KvnUrl=".Length);
                    }
                }
                else
                {
                    chkDiscord.IsChecked = true;
                    chkYouTube.IsChecked = true;
                    chkTelegram.IsChecked = false;
                    cmbStrategy.SelectedIndex = 5;
                }
            }
            catch { }
        }

        private void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;
            bool telegram = chkTelegram.IsChecked ?? false;

            int strategy = cmbStrategy.SelectedIndex;

            _engine.Start(discord, youtube, telegram, strategy);
            System.Threading.Thread.Sleep(500);
        }

        private void BtnStop_Click(object sender, RoutedEventArgs e)
        {
            _engine.Stop();
        }

        private void BtnInstallService_Click(object sender, RoutedEventArgs e)
        {
            bool discord = chkDiscord.IsChecked ?? false;
            bool youtube = chkYouTube.IsChecked ?? false;
            bool telegram = chkTelegram.IsChecked ?? false;

            int strategy = cmbStrategy.SelectedIndex;

            _engine.InstallService(discord, youtube, telegram, strategy);
            System.Threading.Thread.Sleep(1000);
        }

        private void BtnRemoveService_Click(object sender, RoutedEventArgs e)
        {
            _engine.RemoveService();
            System.Threading.Thread.Sleep(500);
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
            _trayStatusTimer?.Stop();
            _trayStatusTimer = null;

            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
                _trayIcon = null;
            }

            _trayOnIcon?.Dispose();
            _trayOnIcon = null;

            _trayOffIcon?.Dispose();
            _trayOffIcon = null;

            _engine.Stop();
            KvnDisconnectOnClose();

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
                case 0:
                    _engine.SetCustomDNS("Cloudflare", "1.1.1.1", "1.0.0.1");
                    break;
                case 1:
                    _engine.SetCustomDNS("Google DNS", "8.8.8.8", "8.8.4.4");
                    break;
                case 2:
                    _engine.SetCustomDNS("XBOX DNS", "111.88.96.50", "111.88.96.51");
                    break;
                case 3:
                    _engine.SetCustomDNS("По умолчанию", "", "");
                    break;
            }
        }

        private void BtnTelegramBypass_Click(object sender, RoutedEventArgs e)
        {
            bool telegram =
                chkTelegram.IsChecked ?? false;

            if (!telegram)
            {
                _engine.RemoveService();

                txtInstalledStrategy.Text =
                    "Обход Telegram отключён";

                return;
            }

            /*
             * Запускаем Telegram вместе с теми ресурсами,
             * которые выбраны на первой вкладке.
             */
            bool discord =
                chkDiscord.IsChecked ?? false;

            bool youtube =
                chkYouTube.IsChecked ?? false;

            int strategy =
                cmbStrategy.SelectedIndex;

            _engine.InstallService(
                discord,
                youtube,
                true,
                strategy);

            UpdateInstalledStrategyInfo();
        }

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
            /*
             * Не вызываем _engine.Stop().
             * Сначала показывается окно выбора.
             */
            Close();
        }

        // --- МЕТОДЫ ВКЛАДКИ КВН ---

        private void KvnUpdateCoreInfo()
        {
            var coreType = _kvnVpnEngine.DetectCore();
            switch (coreType)
            {
                case VpnCoreType.SingBox:
                    txtKvnCoreInfo.Text = "Ядро: sing-box (TUN-туннель)";
                    txtKvnCoreInfo.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4ade80");
                    txtKvnHint.Text = "sing-box найден. Запускайте лаунчер от имени администратора.";
                    txtKvnHint.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4ade80");
                    break;
                case VpnCoreType.Xray:
                    txtKvnCoreInfo.Text = "Ядро: xray (системный прокси)";
                    txtKvnCoreInfo.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#facc15");
                    txtKvnHint.Text = "xray найден. Будет использоваться системный прокси.";
                    txtKvnHint.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#facc15");
                    break;
                default:
                    txtKvnCoreInfo.Text = "Ядро не найдено";
                    txtKvnCoreInfo.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#ef4444");
                    txtKvnHint.Text = "Для работы впн поместите sing-box.exe или xray.exe в папку vpn-core рядом с .exe";
                    txtKvnHint.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#ef4444");
                    break;
            }
        }

        private void KvnSetConnected(bool connected)
        {
            var green = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#4ade80");
            var red = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString("#ef4444");
            var greenColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#4ade80");
            var redColor = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#ef4444");

            if (connected)
            {
                kvnStatusIndicator.Fill = green;
                ((System.Windows.Media.Effects.DropShadowEffect)kvnStatusIndicator.Effect).Color = greenColor;
                txtKvnConnectionStatus.Text = "VPN подключён";
                btnKvnToggle.Content = "⏹ Отключить";
                btnKvnToggle.Style = (System.Windows.Style)FindResource("DangerButton");
            }
            else
            {
                kvnStatusIndicator.Fill = red;
                ((System.Windows.Media.Effects.DropShadowEffect)kvnStatusIndicator.Effect).Color = redColor;
                txtKvnConnectionStatus.Text = "VPN отключён";
                btnKvnToggle.Content = "🔌 Подключить";
                btnKvnToggle.Style = (System.Windows.Style)FindResource("AccentButton");
            }
        }

        private async void BtnKvnToggle_Click(object sender, RoutedEventArgs e)
        {
            if (_kvnVpnEngine.IsConnected)
            {
                _kvnVpnEngine.Disconnect();
                KvnSetConnected(false);
                return;
            }

            if (lstKvnServers.SelectedItem is not KvnServerConfig cfg)
            {
                txtKvnConnectionStatus.Text = "❌ Выберите сервер";
                return;
            }

            btnKvnToggle.IsEnabled = false;
            txtKvnConnectionStatus.Text = "⏳ Подключение...";

            bool ok = await _kvnVpnEngine.ConnectAsync(cfg);
            KvnSetConnected(ok);

            btnKvnToggle.IsEnabled = true;
        }

        private async void BtnKvnLoad_Click(object sender, RoutedEventArgs e)
        {
            string url = txtKvnSubscriptionUrl.Text.Trim();
            if (string.IsNullOrWhiteSpace(url))
            {
                txtKvnConnectionStatus.Text = "❌ Введите ссылку на подписку";
                return;
            }

            btnKvnLoad.IsEnabled = false;
            txtKvnConnectionStatus.Text = "⏳ Загрузка подписки...";

            _kvnServers = await _kvnEngine.FetchSubscriptionAsync(url);
            _kvnRawSubscription = _kvnEngine.LastRawSubscription;
            lstKvnServers.ItemsSource = _kvnServers;

            if (_kvnServers.Count > 0)
            {
                txtKvnConnectionStatus.Text = $"✅ Загружено {_kvnServers.Count} серверов";
                lstKvnServers.SelectedIndex = 0;
                SaveSettings();
            }
            else
            {
                txtKvnConnectionStatus.Text = "⚠️ Не удалось распарсить подписку";
            }

            btnKvnLoad.IsEnabled = true;
        }

        private void BtnKvnCopySelected_Click(object sender, RoutedEventArgs e)
        {
            if (lstKvnServers.SelectedItem is KvnServerConfig cfg)
            {
                System.Windows.Clipboard.SetText(cfg.RawLink);
                txtKvnConnectionStatus.Text = $"📋 Скопирован: {cfg.Remark}";
            }
            else
            {
                txtKvnConnectionStatus.Text = "❌ Выберите сервер из списка";
            }
        }

        private void BtnKvnCopyAll_Click(object sender, RoutedEventArgs e)
        {
            if (_kvnServers.Count == 0)
            {
                txtKvnConnectionStatus.Text = "❌ Список серверов пуст";
                return;
            }

            string all = string.Join(Environment.NewLine, _kvnServers.Select(s => s.RawLink));
            System.Windows.Clipboard.SetText(all);
            txtKvnConnectionStatus.Text = $"📄 Скопировано {_kvnServers.Count} конфигов";
        }

        private async void BtnKvnSaveSubscription_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(_kvnRawSubscription))
            {
                string url = txtKvnSubscriptionUrl.Text.Trim();
                if (string.IsNullOrWhiteSpace(url))
                {
                    txtKvnConnectionStatus.Text = "❌ Нет данных для сохранения";
                    return;
                }

                _kvnRawSubscription = await _kvnEngine.GetRawSubscriptionAsync(url);
                if (string.IsNullOrWhiteSpace(_kvnRawSubscription))
                {
                    txtKvnConnectionStatus.Text = "❌ Не удалось загрузить подписку";
                    return;
                }
            }

            string path = _kvnEngine.SaveSubscriptionToFile(_kvnRawSubscription);
            if (!string.IsNullOrEmpty(path))
            {
                txtKvnConnectionStatus.Text = $"💾 Сохранено: {System.IO.Path.GetFileName(path)}";
            }
        }

        private void TxtKvnSubscriptionUrl_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            SaveSettings();
        }

        private void KvnDisconnectOnClose()
        {
            if (_kvnVpnEngine.IsConnected)
            {
                _kvnVpnEngine.Disconnect();
            }
        }

        private void UpdateInstalledStrategyInfo()
        {
            try
            {
                string info = _engine.GetInstalledStrategyInfo();

                txtInstalledStrategy.Text = info;

                if (info.Contains("не установлена",
                    StringComparison.OrdinalIgnoreCase))
                {
                    txtInstalledStrategy.Foreground =
                        new System.Windows.Media.BrushConverter()
                            .ConvertFromString("#a1a1aa")
                            as System.Windows.Media.Brush;
                }
                else if (info.Contains("Running",
                    StringComparison.OrdinalIgnoreCase))
                {
                    txtInstalledStrategy.Foreground =
                        new System.Windows.Media.BrushConverter()
                            .ConvertFromString("#4ade80")
                            as System.Windows.Media.Brush;
                }
                else
                {
                    txtInstalledStrategy.Foreground =
                        new System.Windows.Media.BrushConverter()
                            .ConvertFromString("#facc15")
                            as System.Windows.Media.Brush;
                }
            }
            catch (Exception ex)
            {
                txtInstalledStrategy.Text =
                    $"Ошибка проверки стратегии: {ex.Message}";
            }
        }
    }
}