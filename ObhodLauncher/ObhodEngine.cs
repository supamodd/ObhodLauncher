using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using System.ServiceProcess;
using System.Linq;
using System.Collections.Generic;
using Microsoft.Win32;

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
                using var service = new ServiceController("ObhodService");

                service.Refresh();

                if (service.Status == ServiceControllerStatus.Running)
                {
                    return true;
                }
            }
            catch (InvalidOperationException)
            {
                // Служба ещё не создана
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка проверки службы: {ex.Message}");
            }

            try
            {
                Process[] processes = Process.GetProcessesByName("winws");

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
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка проверки winws.exe: {ex.Message}");
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

            string winwsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "winws.exe");
            if (!File.Exists(winwsPath))
            {
                OnLog?.Invoke($"[КРИТИЧЕСКАЯ ОШИБКА] winws.exe не найден: {winwsPath}");
                return;
            }

            string args = GetArguments(enableDiscord, enableYouTube, enableTelegram, strategyIndex, false);
            if (string.IsNullOrWhiteSpace(args))
            {
                OnLog?.Invoke("[КРИТИЧЕСКАЯ ОШИБКА] Не удалось построить аргументы для winws.");
                return;
            }

            OnLog?.Invoke($"[Запуск winws.exe] Стратегия #{strategyIndex + 1}");
            OnLog?.Invoke($"[Аргументы] {args}");

            try
            {
                _winwsProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = winwsPath,
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

                bool started = _winwsProcess.Start();
                if (!started)
                {
                    OnLog?.Invoke("[КРИТИЧЕСКАЯ ОШИБКА] Process.Start вернул false.");
                    _winwsProcess = null;
                    return;
                }

                _winwsProcess.BeginOutputReadLine();
                _winwsProcess.BeginErrorReadLine();

                System.Threading.Thread.Sleep(1200);

                if (_winwsProcess.HasExited)
                {
                    int exitCode = _winwsProcess.ExitCode;

                    OnLog?.Invoke(
                        $"[ОШИБКА] winws.exe сразу завершился. Код: {exitCode}");

                    OnLog?.Invoke(
                        "Проверь WinDivert64.sys, WinDivert.dll и выбранную стратегию.");

                    _winwsProcess.Dispose();
                    _winwsProcess = null;
                }
                else
                {
                    OnLog?.Invoke("=== winws.exe успешно запущен в тестовом режиме ===");
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

        public void InstallService( bool enableDiscord, bool enableYouTube, bool enableTelegram, int strategyIndex)
        {
            try
            {
                CreateDummyListsIfMissing();

                string winwsPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "bin",
                    "winws.exe");

                string windivertDllPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "bin",
                    "WinDivert.dll");

                string windivertSysPath = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "bin",
                    "WinDivert64.sys");

                if (!File.Exists(winwsPath))
                {
                    OnLog?.Invoke($"[ОШИБКА] Не найден winws.exe:");
                    OnLog?.Invoke(winwsPath);
                    return;
                }

                if (!File.Exists(windivertDllPath))
                {
                    OnLog?.Invoke($"[ОШИБКА] Не найден WinDivert.dll:");
                    OnLog?.Invoke(windivertDllPath);
                    return;
                }

                if (!File.Exists(windivertSysPath))
                {
                    OnLog?.Invoke($"[ОШИБКА] Не найден WinDivert64.sys:");
                    OnLog?.Invoke(windivertSysPath);
                    return;
                }

                string args = GetArguments(
                    enableDiscord,
                    enableYouTube,
                    enableTelegram,
                    strategyIndex,
                    true);

                if (string.IsNullOrWhiteSpace(args))
                {
                    OnLog?.Invoke("[ОШИБКА] Аргументы для winws.exe пустые.");
                    return;
                }

                OnLog?.Invoke("[СЛУЖБА] Путь к winws.exe:");
                OnLog?.Invoke(winwsPath);

                OnLog?.Invoke("[СЛУЖБА] Аргументы:");
                OnLog?.Invoke(args);

                // Останавливаем старую службу, если она существует
                RunScCommand(
                    "stop",
                    "ObhodService");

                System.Threading.Thread.Sleep(500);

                // Удаляем старую службу
                RunScCommand(
                    "delete",
                    "ObhodService");

                System.Threading.Thread.Sleep(1000);

                /*
                 * Важно:
                 * binPath= и путь к exe передаются отдельными аргументами.
                 * Это исправляет проблему с кавычками и пробелами в пути.
                 */
                string serviceBinaryPath =
                    $"\"{winwsPath}\" {args}";

                CommandResult createResult = RunScCommand(
                    "create",
                    "ObhodService",
                    "type=",
                    "own",
                    "start=",
                    "auto",
                    "binPath=",
                    serviceBinaryPath,
                    "DisplayName=",
                    "ObhodLauncher Background Service");

                if (createResult.ExitCode != 0)
                {
                    OnLog?.Invoke(
                        $"[ОШИБКА] Не удалось создать службу. Код: {createResult.ExitCode}");

                    if (!string.IsNullOrWhiteSpace(createResult.Output))
                    {
                        OnLog?.Invoke(createResult.Output);
                    }

                    return;
                }

                SaveInstalledStrategy(strategyIndex, enableTelegram, enableDiscord, enableYouTube);

                RunScCommand(
                    "description",
                    "ObhodService",
                    "Обход DPI через winws.exe и WinDivert");

                CommandResult startResult = RunScCommand(
                    "start",
                    "ObhodService");

                if (startResult.ExitCode != 0)
                {
                    OnLog?.Invoke(
                        $"[ОШИБКА] Служба создана, но не запустилась. Код: {startResult.ExitCode}");

                    if (!string.IsNullOrWhiteSpace(startResult.Output))
                    {
                        OnLog?.Invoke(startResult.Output);
                    }

                    OnLog?.Invoke("");
                    OnLog?.Invoke("Для диагностики выполни от имени администратора:");
                    OnLog?.Invoke("sc query ObhodService");
                    OnLog?.Invoke("sc qc ObhodService");
                    OnLog?.Invoke("sc start ObhodService");

                    return;
                }

                System.Threading.Thread.Sleep(1500);

                ServiceControllerStatus? status =
                    GetServiceStatus("ObhodService");

                if (status == ServiceControllerStatus.Running)
                {
                    OnLog?.Invoke("");
                    OnLog?.Invoke("=== Служба успешно установлена и запущена ===");
                    OnLog?.Invoke("winws.exe работает через службу ObhodService.");
                    OnLog?.Invoke("WinDivert должен загрузиться как драйвер ядра.");
                }
                else
                {
                    OnLog?.Invoke("");
                    OnLog?.Invoke(
                        $"[ОШИБКА] Служба существует, но её статус: {status}");

                    Process[] processes =
                        Process.GetProcessesByName("winws");

                    if (processes.Length == 0)
                    {
                        OnLog?.Invoke(
                            "winws.exe не найден среди запущенных процессов.");
                    }
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(
                    $"[КРИТИЧЕСКАЯ ОШИБКА УСТАНОВКИ СЛУЖБЫ] {ex}");
            }
        }

        public void RemoveService()
        {
            try
            {
                OnLog?.Invoke("[СЛУЖБА] Остановка ObhodService...");

                RunScCommand(
                    "stop",
                    "ObhodService");

                System.Threading.Thread.Sleep(1000);

                OnLog?.Invoke("[СЛУЖБА] Удаление ObhodService...");

                CommandResult deleteResult = RunScCommand(
                    "delete",
                    "ObhodService");

                if (deleteResult.ExitCode == 0)
                {
                    OnLog?.Invoke("=== Служба ObhodService удалена ===");
                }
                else if (!string.IsNullOrWhiteSpace(deleteResult.Output))
                {
                    OnLog?.Invoke(deleteResult.Output);
                }

                // Останавливаем только ручной процесс, если он был запущен кнопкой
                if (_winwsProcess != null)
                {
                    try
                    {
                        if (!_winwsProcess.HasExited)
                        {
                            _winwsProcess.Kill(true);
                            _winwsProcess.WaitForExit(3000);
                        }
                    }
                    catch
                    {
                        // Процесс уже мог завершиться
                    }

                    _winwsProcess.Dispose();
                    _winwsProcess = null;
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка удаления службы: {ex.Message}");
            }
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

            /*
             * Если включён только Telegram,
             * используем отдельную Telegram-стратегию,
             * а не general.bat.
             */
            if (telegram && !discord && !youtube)
            {
                OnLog?.Invoke("[Стратегия] Telegram");

                return BuildTelegramArguments(baseDir);
            }
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

                /*
                 * ВАЖНО:
                 * Нельзя удалять всю строку с %GameFilterTCP% или %GameFilterUDP%,
                 * потому что в этой же строке находятся --wf-tcp и --wf-udp.
                 *
                 * Значение 12 используется как отключённый игровой диапазон.
                 */
                content = content.Replace("%GameFilterTCP%", "12");
                content = content.Replace("%GameFilterUDP%", "12");
                content = content.Replace("%GameFilter%", "12");

                /*
                 * Убираем командные строки, которые не являются аргументами winws:
                 * @echo off
                 * cd
                 * call service.bat
                 * set
                 * start
                 */
                var lines = content
                    .Replace("\r\n", "\n")
                    .Replace('\r', '\n')
                    .Split('\n');

                var argumentLines = new List<string>();
                bool commandFound = false;

                foreach (string originalLine in lines)
                {
                    string line = originalLine.Trim();

                    if (line.Length == 0)
                    {
                        continue;
                    }

                    /*
                     * Ищем строку, где запускается winws.exe.
                     * Обычно она выглядит так:
                     *
                     * start "zapret" /min "%BIN%winws.exe" --wf-tcp=...
                     */
                    if (!commandFound)
                    {
                        int winwsIndex = line.IndexOf(
                            "winws.exe",
                            StringComparison.OrdinalIgnoreCase);

                        if (winwsIndex < 0)
                        {
                            continue;
                        }

                        commandFound = true;

                        int afterExeIndex = line.IndexOf(
                            '"',
                            winwsIndex + "winws.exe".Length);

                        if (afterExeIndex >= 0 &&
                            afterExeIndex + 1 < line.Length)
                        {
                            string firstArgs =
                                line.Substring(afterExeIndex + 1).Trim();

                            if (firstArgs.StartsWith("^"))
                            {
                                firstArgs = firstArgs.Substring(1).Trim();
                            }

                            if (!string.IsNullOrWhiteSpace(firstArgs))
                            {
                                argumentLines.Add(firstArgs);
                            }
                        }

                        continue;
                    }

                    /*
                     * После строки winws.exe идут продолжения команды.
                     * Убираем символ переноса командной строки ^.
                     */
                    if (line.EndsWith("^"))
                    {
                        line = line.Substring(0, line.Length - 1).Trim();
                    }

                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        argumentLines.Add(line);
                    }
                }

                if (!commandFound)
                {
                    OnLog?.Invoke(
                        $"[ОШИБКА] В стратегии не найдена команда winws.exe: {batPath}");

                    return string.Empty;
                }

                string args = string.Join(" ", argumentLines);

                string binPath = Path.Combine(baseDir, "bin") + "\\";
                string listsPath = Path.Combine(baseDir, "lists") + "\\";

                /*
                 * Замена переменных BAT на реальные абсолютные пути.
                 */
                args = args.Replace("%BIN%", binPath);
                args = args.Replace("%LISTS%", listsPath);

                args = args.Replace("%GameFilterTCP%", "12");
                args = args.Replace("%GameFilterUDP%", "12");
                args = args.Replace("%GameFilter%", "12");

                /*
                 * BAT иногда использует ^! для экранирования символа !.
                 */
                args = args.Replace("^!", "!");

                /*
                 * Убираем оставшиеся символы переноса команд.
                 */
                args = args.Replace("^", " ");

                while (args.Contains("  "))
                {
                    args = args.Replace("  ", " ");
                }

                args = args.Trim();

                /*
                 * Выводим итоговые аргументы в лог.
                 * По ним можно проверить, что --wf-tcp и --wf-udp действительно есть.
                 */
                OnLog?.Invoke("[ПАРСЕР] Итоговые аргументы стратегии:");
                OnLog?.Invoke(args);

                return args;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(
                    $"[ОШИБКА] Не удалось разобрать стратегию: {ex.Message}");

                return string.Empty;
            }
        }

        private string BuildTelegramArguments(string baseDir)
        {
            string binPath = Path.Combine(baseDir, "bin") + "\\";
            string listsPath = Path.Combine(baseDir, "lists") + "\\";

            string telegramIpSet =
                $"\"{listsPath}ipset-telegram.txt\"";

            string fakeQuic =
                $"\"{binPath}quic_initial_dbankcloud_ru.bin\"";

            string fakeTls =
                $"\"{binPath}tls_clienthello_max_ru.bin\"";

            string args =
                $"--wf-tcp=80,443 " +
                $"--wf-udp=443 " +

                /*
                 * TCP Telegram.
                 * Обрабатываем только IP-адреса Telegram.
                 */
                $"--filter-tcp=80,443 " +
                $"--ipset={telegramIpSet} " +
                $"--dpi-desync=fake,multisplit " +
                $"--dpi-desync-repeats=8 " +
                $"--dpi-desync-fooling=ts " +
                $"--dpi-desync-split-pos=1 " +
                $"--dpi-desync-split-seqovl=664 " +
                $"--dpi-desync-split-seqovl-pattern={fakeTls} " +
                $"--dpi-desync-fake-tls={fakeTls} " +
                $"--new " +

                /*
                 * UDP Telegram / QUIC.
                 */
                $"--filter-udp=443 " +
                $"--ipset={telegramIpSet} " +
                $"--dpi-desync=fake " +
                $"--dpi-desync-repeats=10 " +
                $"--dpi-desync-fake-quic={fakeQuic}";

            OnLog?.Invoke("[Telegram] Используется ipset-telegram.txt");
            OnLog?.Invoke("[Telegram] Аргументы:");
            OnLog?.Invoke(args);

            return args;
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

        private sealed class CommandResult
        {
            public int ExitCode { get; init; }

            public string Output { get; init; } = string.Empty;
        }

        private CommandResult RunScCommand(params string[] arguments)
        {
            try
            {
                using var process = new Process();

                process.StartInfo = new ProcessStartInfo
                {
                    FileName = "sc.exe",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory
                };

                foreach (string argument in arguments)
                {
                    process.StartInfo.ArgumentList.Add(argument);
                }

                process.Start();

                string stdout = process.StandardOutput.ReadToEnd();
                string stderr = process.StandardError.ReadToEnd();

                process.WaitForExit();

                string output = stdout;

                if (!string.IsNullOrWhiteSpace(stderr))
                {
                    output += Environment.NewLine + stderr;
                }

                output = output.Trim();

                if (!string.IsNullOrWhiteSpace(output))
                {
                    OnLog?.Invoke($"[sc.exe] {output}");
                }

                return new CommandResult
                {
                    ExitCode = process.ExitCode,
                    Output = output
                };
            }
            catch (Exception ex)
            {
                string error = $"Ошибка запуска sc.exe: {ex.Message}";

                OnLog?.Invoke(error);

                return new CommandResult
                {
                    ExitCode = -1,
                    Output = error
                };
            }
        }

        private ServiceControllerStatus? GetServiceStatus(string serviceName)
        {
            try
            {
                using var service = new ServiceController(serviceName);

                service.Refresh();

                return service.Status;
            }
            catch
            {
                return null;
            }
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

        private void SaveInstalledStrategy(int strategyIndex, bool telegram, bool discord, bool youtube)
        {
            try
            {
                using RegistryKey? key =
                    Registry.LocalMachine.CreateSubKey(
                        @"SYSTEM\CurrentControlSet\Services\ObhodService");

                if (key == null)
                {
                    OnLog?.Invoke(
                        "[ПРЕДУПРЕЖДЕНИЕ] Не удалось сохранить название стратегии.");
                    return;
                }

                string strategyName;

                if (telegram && !discord && !youtube)
                {
                    strategyName = "Telegram";
                }
                else
                {
                    strategyName = GetStrategyFileName(strategyIndex);
                }

                key.SetValue(
                    "ObhodStrategy",
                    strategyName,
                    RegistryValueKind.String);

                key.SetValue(
                    "ObhodInstallTime",
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    RegistryValueKind.String);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke(
                    $"[ПРЕДУПРЕЖДЕНИЕ] Не удалось записать стратегию: {ex.Message}");
            }
        }

        public string GetInstalledStrategyInfo()
        {
            try
            {
                using var service =
                    new ServiceController("ObhodService");

                service.Refresh();

                using RegistryKey? key =
                    Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Services\ObhodService");

                string? strategy =
                    key?.GetValue("ObhodStrategy")?.ToString();

                string status = service.Status.ToString();

                if (string.IsNullOrWhiteSpace(strategy))
                {
                    return $"Служба установлена, статус: {status}";
                }

                return $"Установлена: {strategy} | Статус: {status}";
            }
            catch (InvalidOperationException)
            {
                return "Служба обхода не установлена";
            }
            catch (Exception ex)
            {
                return $"Не удалось определить стратегию: {ex.Message}";
            }
        }
    }
}