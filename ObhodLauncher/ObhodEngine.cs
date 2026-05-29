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
                        WorkingDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin"), // ОЧЕНЬ ВАЖНО ДЛЯ ALT 11!
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
            string args = GetArguments(enableDiscord, enableYouTube, enableTelegram, strategyIndex, true); // true для полных путей!
            string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "winws.exe");

            string scArgs = $"create \"ObhodService\" binPath= \"\\\"{binPath}\\\" {args.Replace("\"", "\\\"")}\" start= auto displayname= \"ObhodLauncher Background Service\"";

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

            string[] dummyFiles = { "ipset-exclude-user.txt", "list-general-user.txt", "list-exclude-user.txt" };
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
                    // Meta (Instagram / Facebook) ASN
                    "31.13.24.0/21\n31.13.64.0/18\n69.63.176.0/20\n69.171.224.0/19\n" +
                    "74.119.76.0/22\n103.4.96.0/22\n129.236.0.0/16\n157.240.0.0/16\n" +
                    "173.252.64.0/18\n179.60.192.0/22\n185.60.216.0/22\n204.15.20.0/22\n" +
                    // Cloudflare / Fastly / Mindgeek (Pornhub / Models / Gamma)
                    "66.254.114.0/24\n188.114.96.0/20\n104.18.0.0/15\n104.16.0.0/12";
                File.WriteAllText(mediaIpsetPath, mediaSubnets);
            }
        }

        private string GetArguments(bool discord, bool youtube, bool telegram, int strategyIndex, bool forService)
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            // Для максимальной надежности всегда используем АБСОЛЮТНЫЕ пути к папкам!
            string binPrefix = Path.Combine(baseDir, "bin") + "\\";
            string listsPrefix = Path.Combine(baseDir, "lists") + "\\";

            string[] batFiles = {
                "general.bat", "general (ALT).bat", "general (ALT2).bat", "general (ALT3).bat",
                "general (ALT4).bat", "general (ALT5).bat", "general (ALT6).bat", "general (ALT7).bat",
                "general (ALT8).bat", "general (ALT9).bat", "general (ALT10).bat", "general (ALT11).bat",
                "general (FAKE TLS AUTO).bat", "general (FAKE TLS AUTO ALT).bat",
                "general (FAKE TLS AUTO ALT2).bat", "general (FAKE TLS AUTO ALT3).bat",
                "general (SIMPLE FAKE).bat", "general (SIMPLE FAKE ALT).bat", "general (SIMPLE FAKE ALT2).bat"
            };

            string batFilePath = Path.Combine(baseDir, "strategies", batFiles[strategyIndex]);
            if (!File.Exists(batFilePath)) throw new Exception($"Файл стратегии не найден: {batFiles[strategyIndex]}");

            string args = "";
            string[] lines = File.ReadAllLines(batFilePath);

            // Ищем строку, где запускается winws
            foreach (string line in lines)
            {
                if (line.Trim().StartsWith("start") && line.Contains("winws.exe"))
                {
                    int idx = line.IndexOf("winws.exe\"");
                    if (idx != -1)
                    {
                        args = line.Substring(idx + 10).Trim();
                        break;
                    }
                }
            }

            // Очищаем от кареток (^) и склеиваем в одну чистую строку
            args = args.Replace("^", " ");
            while (args.Contains("  ")) args = args.Replace("  ", " "); // Убираем двойные пробелы

            // Если пользователь ОТКЛЮЧИЛ YouTube: аккуратно вырезаем блоки с list-google.txt
            if (!youtube)
            {
                args = System.Text.RegularExpressions.Regex.Replace(args, @"--filter-[^\s]+ [^-]*?list-google\.txt.*?--new\s", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            // Если пользователь ОТКЛЮЧИЛ Discord: аккуратно вырезаем блоки Discord
            if (!discord)
            {
                args = System.Text.RegularExpressions.Regex.Replace(args, @"--filter-[^\s]+ [^-]*?discord,stun.*?--new\s", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                args = System.Text.RegularExpressions.Regex.Replace(args, @"--filter-[^\s]+ [^-]*?discord\.media.*?--new\s", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            }

            // ЗАМЕНА ПУТЕЙ И ПЕРЕМЕННЫХ
            // ВАЖНО: Мы убираем кавычки вокруг %BIN% и %LISTS% в батнике, потому что мы добавим свои кавычки ко всем путям
            args = args.Replace("\"%BIN%", "\"").Replace("\"%LISTS%", "\""); // Чистим оригинальные кавычки
            args = args.Replace("%BIN%", $"\"{binPrefix}\"");
            args = args.Replace("%LISTS%", $"\"{listsPrefix}\"");

            // Игровые порты (Включаем высокие порты для видео CDN, как в оригинале Flowseal!)
            args = args.Replace("%GameFilterTCP%", "1024-65535");
            args = args.Replace("%GameFilterUDP%", "1024-65535");

            // --- МЕДИА ЛИСТЫ ---
            if (File.Exists(Path.Combine(baseDir, "lists", "list-media.txt")))
            {
                args = args.Replace("--hostlist-exclude=", $"--hostlist=\"{listsPrefix}list-media.txt\" --hostlist-exclude=");
            }

            // ПАРАЛЛЕЛЬНЫЙ РЕЖИМ (Работает ВМЕСТЕ с основным Flowseal, не мешая ему)
            if (telegram)
            {
                // Забываем про все предыдущие аргументы (сбрасываем строку), так как основную работу делает Flowseal
                args = $"--wf-tcp=80,443,5222,5228 --wf-udp=443 ";

                // 1. Телеграм
                args += $"--filter-tcp=80,443,5222,5228 --ipset=\"{listsPrefix}ipset-telegram.txt\" --dpi-desync=split2 --dpi-desync-split-pos=2 --dpi-desync-any-protocol=1 --new ";
                args += $"--filter-udp=443 --ipset=\"{listsPrefix}ipset-telegram.txt\" --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-any-protocol=1 --new ";

                if (_enableMediaBypass)
                {
                    // Для Инсты и Медиа применяем жесткий fakedsplit с подменой SNI, который пробивает ТСПУ
                    args += $"--filter-tcp=80,443 --ipset=\"{listsPrefix}ipset-media.txt\" --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-fake-http=\"{binPrefix}tls_clienthello_max_ru.bin\" --new ";
                    args += $"--filter-udp=443 --ipset=\"{listsPrefix}ipset-media.txt\" --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic=\"{binPrefix}quic_initial_www_google_com.bin\" --new ";
                }

                return args.Trim();
            }
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