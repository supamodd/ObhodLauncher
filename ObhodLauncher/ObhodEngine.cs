using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.Linq;

namespace ZapretWPF
{
    public class ZapretEngine
    {
        private Process _winwsProcess;
        private bool _enableMediaBypass = false;
        public Action<string> OnLog { get; set; }

        public bool IsServiceRunning()
        {
            try
            {
                ServiceController sc = new ServiceController("ObhodService");
                if (sc.Status == ServiceControllerStatus.Running)
                    return true;
            }
            catch
            {
                Process[] processes = Process.GetProcessesByName("winws");
                if (processes.Length > 0)
                    return true;
            }
            return false;
        }

        public void Start(bool enableDiscord, bool enableYouTube, bool enableTelegram, int strategyIndex)
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                OnLog?.Invoke("Обход уже запущен в режиме консоли!");
                return;
            }

            CreateDummyListsIfMissing();
            string args = GetArguments(enableDiscord, enableYouTube, enableTelegram, strategyIndex, false);
            OnLog?.Invoke($"[Запуск winws.exe] Стратегия #{strategyIndex + 1}\nАргументы: {args}\n");

            try
            {
                _winwsProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "winws.exe"),
                        WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin"),
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                _winwsProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke(e.Data); };
                _winwsProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke("ОШИБКА WINWS: " + e.Data); };

                _winwsProcess.Start();
                _winwsProcess.BeginOutputReadLine();
                _winwsProcess.BeginErrorReadLine();

                if (_winwsProcess.WaitForExit(500))
                {
                    OnLog?.Invoke($"[КРИТИЧЕСКАЯ ОШИБКА] winws.exe мгновенно закрылся. Код: {_winwsProcess.ExitCode}.");
                    _winwsProcess = null;
                }
                else
                {
                    OnLog?.Invoke("=== Обход успешно запущен (Тест) ===");
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка запуска: {ex.Message}");
            }
        }

        public void Stop()
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                _winwsProcess.Kill();
                _winwsProcess.Dispose();
                _winwsProcess = null;
                OnLog?.Invoke("=== Тестовый процесс остановлен ===");
            }
        }

        public void InstallService(bool enableDiscord, bool enableYouTube, bool enableTelegram, int strategyIndex)
        {
            CreateDummyListsIfMissing();
            string args = GetArguments(enableDiscord, enableYouTube, enableTelegram, strategyIndex, true);
            string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "winws.exe");

            string scArgs = $"create \"ObhodService\" binPath= \"\\\"{binPath}\\\" {args.Replace("\"", "\\\\\"")}\" start= auto displayname= \"ObhodLauncher Background Service\"";

            RunAsAdmin("sc.exe", "stop ObhodService");
            RunAsAdmin("sc.exe", "delete ObhodService");
            RunAsAdmin("sc.exe", scArgs);
            RunAsAdmin("sc.exe", "start ObhodService");

            OnLog?.Invoke($"=== Служба установлена (Стратегия #{strategyIndex + 1}) ===");
            OnLog?.Invoke("Программу можно закрывать, обход работает в фоне.");
        }

        public void RemoveService()
        {
            RunAsAdmin("sc.exe", "stop ObhodService");
            RunAsAdmin("sc.exe", "delete ObhodService");
            OnLog?.Invoke("=== Фоновая служба удалена ===");
        }

        private void CreateDummyListsIfMissing()
        {
            string listsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists");
            if (!Directory.Exists(listsPath)) Directory.CreateDirectory(listsPath);

            string[] dummyFiles = { "ipset-exclude-user.txt", "list-general-user.txt", "list-exclude-user.txt", "list-general.txt", "list-google.txt", "ipset-all.txt" };
            string[] emptyFiles = { "ipset-exclude.txt", "list-exclude.txt" };

            foreach (var file in dummyFiles)
            {
                string path = Path.Combine(listsPath, file);
                if (!File.Exists(path)) File.WriteAllText(path, "domain.example.abc");
            }
            foreach (var file in emptyFiles)
            {
                string path = Path.Combine(listsPath, file);
                if (!File.Exists(path)) File.WriteAllText(path, "");
            }

            string tgIpsetPath = Path.Combine(listsPath, "ipset-telegram.txt");
            if (!File.Exists(tgIpsetPath))
            {
                string tgSubnets =
                    "91.108.4.0/22\n91.108.8.0/22\n91.108.12.0/22\n91.108.16.0/22\n91.108.20.0/22\n" +
                    "91.108.56.0/22\n91.108.192.0/22\n149.154.160.0/20\n149.154.164.0/22\n149.154.168.0/22\n" +
                    "149.154.172.0/22\n185.76.151.0/24\n95.161.76.0/23\n" +
                    "104.244.72.0/24\n104.244.73.0/24\n104.244.74.0/24";
                File.WriteAllText(tgIpsetPath, tgSubnets);
            }

            string userListPath = Path.Combine(listsPath, "list-general-user.txt");
            if (File.Exists(userListPath))
            {
                string currentUserList = File.ReadAllText(userListPath);
                if (!currentUserList.Contains("telegram.org"))
                {
                    string tgDomains = Environment.NewLine + "telegram.org" + Environment.NewLine + "desktop.telegram.org" + Environment.NewLine + "web.telegram.org" + Environment.NewLine + "t.me";
                    File.AppendAllText(userListPath, tgDomains);
                }
            }

            string mediaIpsetPath = Path.Combine(listsPath, "ipset-media.txt");
            if (!File.Exists(mediaIpsetPath))
            {
                string mediaSubnets =
                    "31.13.24.0/21\n31.13.64.0/18\n69.63.176.0/20\n69.171.224.0/19\n" +
                    "74.119.76.0/22\n103.4.96.0/22\n129.236.0.0/16\n157.240.0.0/16\n" +
                    "173.252.64.0/18\n179.60.192.0/22\n185.60.216.0/22\n204.15.20.0/22\n" +
                    "66.254.114.0/24\n188.114.96.0/20\n104.18.0.0/15\n104.16.0.0/12";
                File.WriteAllText(mediaIpsetPath, mediaSubnets);
            }
        }

        private string GetArguments(bool discord, bool youtube, bool telegram, int strategyIndex, bool forService)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string strategiesDir = Path.Combine(baseDir, "strategies");
            string strategyFile = GetStrategyFileName(strategyIndex);
            string strategyPath = Path.Combine(strategiesDir, strategyFile);

            if (File.Exists(strategyPath))
            {
                OnLog?.Invoke($"[Стратегия] {strategyFile}");
                string args = BuildArgsFromStrategyFile(strategyPath, baseDir);
                if (!string.IsNullOrWhiteSpace(args))
                    return args;
            }

            OnLog?.Invoke($"[Предупреждение] Файл стратегии не найден: {strategyPath}. Используется базовая стратегия.");
            return BuildDefaultArguments(baseDir);
        }

        private string GetStrategyFileName(int strategyIndex)
        {
            return strategyIndex switch
            {
                0 => "general.bat",
                1 => "general (ALT).bat",
                2 => "general (ALT2).bat",
                3 => "general (ALT3).bat",
                4 => "general (ALT4).bat",
                5 => "general (ALT5).bat",
                6 => "general (ALT6).bat",
                7 => "general (ALT7).bat",
                8 => "general (ALT8).bat",
                9 => "general (ALT9).bat",
                10 => "general (ALT10).bat",
                11 => "general (ALT11).bat",
                12 => "general (FAKE TLS AUTO).bat",
                13 => "general (FAKE TLS AUTO ALT).bat",
                14 => "general (FAKE TLS AUTO ALT2).bat",
                15 => "general (FAKE TLS AUTO ALT3).bat",
                16 => "general (SIMPLE FAKE).bat",
                17 => "general (SIMPLE FAKE ALT).bat",
                18 => "general (SIMPLE FAKE ALT2).bat",
                _ => "general.bat"
            };
        }

        private string BuildArgsFromStrategyFile(string batPath, string baseDir)
        {
            try
            {
                string content = File.ReadAllText(batPath);

                content = content.Replace("^\r\n", " ")
                                 .Replace("^\n", " ")
                                 .Replace("^\r", " ");

                int winwsIndex = content.IndexOf("winws.exe\"");
                if (winwsIndex < 0)
                {
                    OnLog?.Invoke("[Ошибка] Не найдена командная строка winws в .bat файле.");
                    return "";
                }

                int startIndex = winwsIndex + "winws.exe\"".Length;
                int endIndex = content.IndexOf('\n', startIndex);
                if (endIndex < 0) endIndex = content.Length;

                string args = content.Substring(startIndex, endIndex - startIndex).Trim();

                string binPath = Path.Combine(baseDir, "bin") + "\\";
                string listsPath = Path.Combine(baseDir, "lists") + "\\";

                args = args.Replace("%BIN%", binPath);
                args = args.Replace("%LISTS%", listsPath);
                args = args.Replace("%GameFilterTCP%", "");
                args = args.Replace("%GameFilterUDP%", "");
                args = args.Replace("^!", "!");

                while (args.Contains("  "))
                    args = args.Replace("  ", " ");

                return args;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[Ошибка] Не удалось распарсить стратегию: {ex.Message}");
                return "";
            }
        }

        private string BuildDefaultArguments(string baseDir)
        {
            string binPrefix = Path.Combine(baseDir, "bin") + "\\";
            string listsPrefix = Path.Combine(baseDir, "lists") + "\\";

            return $"--wf-tcp=80,443 --wf-udp=443 " +
                   $"--filter-udp=443 --hostlist=\"{listsPrefix}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{binPrefix}quic_initial_www_google_com.bin\" --new " +
                   $"--filter-tcp=80,443 --hostlist=\"{listsPrefix}list-general.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{binPrefix}tls_clienthello_4pda_to.bin\"";
        }

        public async Task TestConnectionAsync()
        {
            OnLog?.Invoke("=== Запуск проверки соединения ===");

            await CheckUrlAsync("Discord API", "https://discord.com/api/v9/gateway");
            await CheckUrlAsync("Discord Status", "https://discordstatus.com");

            await CheckUrlAsync("YouTube", "https://www.youtube.com");
            await CheckUrlAsync("YouTube Player", "https://www.youtube.com/s/player/img/favicon_32.png");

            OnLog?.Invoke("=== Проверка завершена ===");
        }

        private async Task CheckUrlAsync(string name, string url)
        {
            try
            {
                var handler = new HttpClientHandler
                {
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };

                using (var client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(4);
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    Stopwatch sw = Stopwatch.StartNew();
                    HttpResponseMessage response = await client.GetAsync(url);
                    sw.Stop();

                    if ((int)response.StatusCode >= 200 && (int)response.StatusCode < 500)
                    {
                        OnLog?.Invoke($"[🟢 УСПЕХ] {name} работает! (Пинг: {sw.ElapsedMilliseconds} мс)");
                    }
                    else
                    {
                        OnLog?.Invoke($"[🟡 ПРЕДУПРЕЖДЕНИЕ] {name} ответил с кодом {(int)response.StatusCode}");
                    }
                }
            }
            catch (TaskCanceledException)
            {
                OnLog?.Invoke($"[🔴 ЗАБЛОКИРОВАН] {name} не ответил (Таймаут).");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[🔴 ЗАБЛОКИРОВАН] {name} недоступен: {ex.Message.Split('\n')[0]}");
            }
        }

        public void FlushDNS()
        {
            try
            {
                OnLog?.Invoke("=== Выполнение сброса сети ===");

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = "/c ipconfig /flushdns",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                });

                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit();

                OnLog?.Invoke("Кэш DNS успешно очищен!");
                OnLog?.Invoke("Рекомендуется перезапустить браузер или клиент Discord.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка при сбросе сети: {ex.Message}");
            }
        }

        public async Task UpdateListsAsync()
        {
            OnLog?.Invoke("=== Начало обновления списков с GitHub ===");
            string listsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists");

            if (!Directory.Exists(listsPath))
            {
                Directory.CreateDirectory(listsPath);
            }

            string baseUrl = "https://raw.githubusercontent.com/Flowseal/zapret-discord-youtube/main/lists/";

            string[] filesToDownload = {
                "list-general.txt",
                "list-google.txt",
                "list-exclude.txt",
                "ipset-all.txt",
                "ipset-exclude.txt"
            };

            using (var client = new HttpClient())
            {
                client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
                client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) SupaModd/1.0");
                System.Net.ServicePointManager.SecurityProtocol = System.Net.SecurityProtocolType.Tls12;

                foreach (string file in filesToDownload)
                {
                    try
                    {
                        OnLog?.Invoke($"Скачивание {file}...");
                        string fileUrl = baseUrl + file;
                        string savePath = Path.Combine(listsPath, file);

                        string content = await client.GetStringAsync(fileUrl);
                        File.WriteAllText(savePath, content);
                        OnLog?.Invoke($"[✓] {file} успешно обновлен!");
                    }
                    catch (Exception ex)
                    {
                        OnLog?.Invoke($"[✗] Ошибка при скачивании {file}: {ex.Message}");
                    }
                }
            }

            OnLog?.Invoke("=== Обновление списков завершено ===");
            OnLog?.Invoke("Внимание: Изменения вступят в силу после перезапуска обхода.");
        }

        public void SetCustomDNS(string dnsName, string primaryDNS, string secondaryDNS)
        {
            try
            {
                string psCommand;

                if (dnsName == "По умолчанию")
                {
                    OnLog?.Invoke("=== Сброс DNS-серверов к значениям провайдера ===");
                    psCommand = "Get-NetAdapter | Where-Object { $_.Status -eq 'Up' -and $_.Name -notmatch 'vEthernet|Virtual|Pseudo|Loopback' } | Set-DnsClientServerAddress -ResetServerAddresses";
                }
                else
                {
                    OnLog?.Invoke($"=== Смена DNS-серверов на {dnsName} ===");
                    OnLog?.Invoke($"Установка адресов: {primaryDNS}, {secondaryDNS}");
                    psCommand = $"Get-NetAdapter | Where-Object {{ $_.Status -eq 'Up' -and $_.Name -notmatch 'vEthernet|Virtual|Pseudo|Loopback' }} | Set-DnsClientServerAddress -ServerAddresses '{primaryDNS}', '{secondaryDNS}'";
                }

                var process = Process.Start(new ProcessStartInfo
                {
                    FileName = "powershell.exe",
                    Arguments = $"-NoProfile -ExecutionPolicy Bypass -Command \"{psCommand}\"",
                    UseShellExecute = true,
                    Verb = "runas",
                    WindowStyle = ProcessWindowStyle.Hidden
                });

                process?.WaitForExit();

                if (dnsName == "По умолчанию")
                {
                    OnLog?.Invoke("[✓] Настройки DNS сброшены! Теперь они получаются автоматически.");
                }
                else
                {
                    OnLog?.Invoke($"[✓] Настройки сетевого адаптера успешно обновлены на {dnsName}!");

                    if (dnsName.Contains("XBOX"))
                    {
                        OnLog?.Invoke("\nВНИМАНИЕ: Для полной работы XBOX DNS через шифрование (DoH) ");
                        OnLog?.Invoke("добавьте в браузере безопасный DNS: https://xbox-dns.ru/dns-query");
                    }
                }
                OnLog?.Invoke("Рекомендуется нажать 'Очистить' (Сброс сети) для применения изменений.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[✗] Ошибка при смене DNS: {ex.Message}");
            }
        }

        public void PatchInstagramHosts()
        {
            _enableMediaBypass = true;
            OnLog?.Invoke("[✓] Обход Instagram активирован!");
            OnLog?.Invoke("Нажмите 'Применить' на карточке Telegram (это запустит наш параллельный движок).");
        }

        public void AddMediaBypass()
        {
            _enableMediaBypass = true;
            OnLog?.Invoke("[✓] Обход Медиа-ресурсов активирован!");
            OnLog?.Invoke("Нажмите 'Применить' на карточке Telegram (это запустит наш параллельный движок).");
        }

        private void RunAsAdmin(string fileName, string args)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = true,
                Verb = "runas",
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit();
        }
    }
}