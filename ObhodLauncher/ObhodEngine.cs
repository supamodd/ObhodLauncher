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
        }

        private string GetArguments(bool discord, bool youtube, bool telegram, int strategyIndex, bool forService)
        {
            string bin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin") + "\\";
            string fBin = forService ? bin : "";
            string lists = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists") + "\\";

            string args = $"--wf-tcp=80,443,2053,2083,2087,2096,8443 --wf-udp=443,19294-19344,50000-50100 ";

            string mediaListArg = File.Exists(Path.Combine(lists, "list-media.txt")) ? $"--hostlist=\"{lists}list-media.txt\"" : "";

            string incLists = $"--hostlist=\"{lists}list-general.txt\" --hostlist=\"{lists}list-general-user.txt\" {mediaListArg}";
            string exLists = $"--hostlist-exclude=\"{lists}list-exclude.txt\" --hostlist-exclude=\"{lists}list-exclude-user.txt\"";
            string exIps = $"--ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\"";

            switch (strategyIndex)
            {
                case 0: // 1. Flowseal General
                    args += $"--filter-udp=443 {incLists} {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 {incLists} {exLists} {exIps} --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_4pda_to.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    break;

                case 1: // 2. Flowseal ALT 1
                    args += $"--filter-udp=443 {incLists} {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 {incLists} {exLists} {exIps} --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=\"{fBin}stun.bin\" --dpi-desync-fake-tls=\"{fBin}tls_clienthello_www_google_com.bin\" --dpi-desync-fake-http=\"{fBin}tls_clienthello_max_ru.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    break;

                case 2: // 3. Flowseal ALT 2
                    args += $"--filter-udp=443 {incLists} {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 {incLists} {exLists} {exIps} --dpi-desync=multisplit --dpi-desync-split-seqovl=652 --dpi-desync-split-pos=2 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=652 --dpi-desync-split-pos=2 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-seqovl=652 --dpi-desync-split-pos=2 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    break;

                case 3: // 4. Flowseal ALT 3
                    args += $"--filter-udp=443 {incLists} {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 {incLists} {exLists} {exIps} --dpi-desync=fake,hostfakesplit --dpi-desync-fake-tls-mod=rnd,dupsid,sni=ya.ru --dpi-desync-hostfakesplit-mod=host=ya.ru,altorder=1 --dpi-desync-fooling=ts --dpi-desync-fake-http=\"{fBin}tls_clienthello_max_ru.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,hostfakesplit --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-hostfakesplit-mod=host=www.google.com,altorder=1 --dpi-desync-fooling=ts --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,hostfakesplit --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-hostfakesplit-mod=host=www.google.com,altorder=1 --dpi-desync-fooling=ts --new ";
                    }
                    break;

                case 4: // 5. Flowseal FAKE TLS AUTO ALT
                    args += $"--filter-udp=443 {incLists} {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 {incLists} {exLists} {exIps} --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-fake-http=\"{fBin}tls_clienthello_max_ru.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{fBin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --new ";
                    }
                    break;

                case 5: // 6. SupaModd Custom (Точная копия ALT 11)
                    args += $"--filter-udp=443 {incLists} {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 {incLists} {exLists} {exIps} --dpi-desync=fake,multisplit --dpi-desync-split-seqovl=664 --dpi-desync-split-pos=1 --dpi-desync-fooling=ts --dpi-desync-repeats=8 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_max_ru.bin\" --dpi-desync-fake-tls=\"{fBin}stun.bin\" --dpi-desync-fake-tls=\"{fBin}tls_clienthello_max_ru.bin\" --dpi-desync-fake-http=\"{fBin}tls_clienthello_max_ru.bin\" --new ";

                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-fooling=ts --dpi-desync-repeats=8 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --dpi-desync-fake-tls=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }

                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-fooling=ts --dpi-desync-repeats=8 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_www_google_com.bin\" --dpi-desync-fake-tls=\"{fBin}tls_clienthello_www_google_com.bin\" --new ";
                    }

                    // FALLBACK ПРАВИЛА ИЗ ALT 11
                    args += $"--filter-udp=443 --ipset=\"{lists}ipset-all.txt\" {exLists} {exIps} --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic=\"{fBin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443,8443 --ipset=\"{lists}ipset-all.txt\" {exLists} {exIps} --dpi-desync=fake,multisplit --dpi-desync-split-seqovl=664 --dpi-desync-split-pos=1 --dpi-desync-fooling=ts --dpi-desync-repeats=8 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_max_ru.bin\" --dpi-desync-fake-tls=\"{fBin}stun.bin\" --dpi-desync-fake-tls=\"{fBin}tls_clienthello_max_ru.bin\" --dpi-desync-fake-http=\"{fBin}tls_clienthello_max_ru.bin\" --new ";

                    args += $"--filter-tcp=12 --ipset=\"{lists}ipset-all.txt\" {exIps} --dpi-desync=fake,multisplit --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n4 --dpi-desync-split-seqovl=664 --dpi-desync-split-pos=1 --dpi-desync-fooling=ts --dpi-desync-repeats=8 --dpi-desync-split-seqovl-pattern=\"{fBin}tls_clienthello_max_ru.bin\" --dpi-desync-fake-tls=\"{fBin}stun.bin\" --dpi-desync-fake-tls=\"{fBin}tls_clienthello_max_ru.bin\" --dpi-desync-fake-http=\"{fBin}tls_clienthello_max_ru.bin\" --new ";
                    args += $"--filter-udp=12 --ipset=\"{lists}ipset-all.txt\" {exIps} --dpi-desync=fake --dpi-desync-repeats=10 --dpi-desync-any-protocol=1 --dpi-desync-fake-unknown-udp=\"{fBin}quic_initial_www_google_com.bin\" --dpi-desync-cutoff=n4 ";
                    break;
            }

            if (telegram)
            {
                args += $"--filter-tcp=80,443,5222,5228 --ipset=\"{lists}ipset-telegram.txt\" --dpi-desync=split2 --dpi-desync-split-pos=2 --dpi-desync-any-protocol=1 --new ";
                args += $"--filter-udp=443 --ipset=\"{lists}ipset-telegram.txt\" --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-any-protocol=1 --new ";
            }

            return args;
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
            try
            {
                OnLog?.Invoke("=== Установка обхода для Instagram ===");
                OnLog?.Invoke("1. Добавление чистых IP-адресов Meta в файл hosts...");

                string hostsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), @"drivers\etc\hosts");

                string hostsPatch =
                    Environment.NewLine + "# --- SUPAMODD INSTAGRAM BYPASS ---" + Environment.NewLine +
                    "57.144.244.34 instagram.com www.instagram.com" + Environment.NewLine +
                    "57.144.244.192 static.cdninstagram.com graph.instagram.com i.instagram.com api.instagram.com edge-chat.instagram.com" + Environment.NewLine +
                    "57.144.244.1 fbcdn.net facebook.com fb.com fbsbx.com" + Environment.NewLine +
                    "31.13.66.63 scontent-hel3-1.cdninstagram.com scontent.cdninstagram.com" + Environment.NewLine +
                    "57.144.244.128 static.xx.fbcdn.net scontent.xx.fbcdn.net" + Environment.NewLine +
                    "31.13.67.20 scontent-hel3-1.xx.fbcdn.net" + Environment.NewLine +
                    "# --- END SUPAMODD INSTAGRAM BYPASS ---" + Environment.NewLine;

                string currentHosts = "";
                if (File.Exists(hostsPath))
                {
                    currentHosts = File.ReadAllText(hostsPath);
                }

                if (!currentHosts.Contains("SUPAMODD INSTAGRAM BYPASS"))
                {
                    File.AppendAllText(hostsPath, hostsPatch);
                    OnLog?.Invoke("[✓] Файл hosts успешно пропатчен!");
                }
                else
                {
                    OnLog?.Invoke("[✓] Файл hosts уже содержит патч для Instagram.");
                }

                OnLog?.Invoke("2. Настройка маршрутов DPI для Instagram...");
                string listsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists");
                string userListPath = Path.Combine(listsPath, "list-general-user.txt");

                if (File.Exists(userListPath))
                {
                    string currentUserList = File.ReadAllText(userListPath);
                    if (!currentUserList.Contains("instagram.com"))
                    {
                        string instaDomains = Environment.NewLine + "instagram.com" + Environment.NewLine + "cdninstagram.com" + Environment.NewLine + "facebook.com" + Environment.NewLine + "fbcdn.net";
                        File.AppendAllText(userListPath, instaDomains);
                        OnLog?.Invoke("[✓] Домены добавлены в список DPI обхода.");
                    }
                }

                OnLog?.Invoke("=== Установка завершена ===");
                OnLog?.Invoke("ОБЯЗАТЕЛЬНО нажмите 'Сброс сети' на второй вкладке, чтобы очистить кэш старых IP.");
                OnLog?.Invoke("Instagram будет работать ТОЛЬКО при включенном обходе.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[✗] Ошибка при прошивке hosts: {ex.Message}");
                OnLog?.Invoke("Убедитесь, что ваш антивирус не блокирует изменение файла hosts.");
            }
        }

        public void AddMediaBypass()
        {
            try
            {
                OnLog?.Invoke("=== Добавление обхода для медиа-ресурсов ===");

                string listsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists");
                string mediaListPath = Path.Combine(listsPath, "list-media.txt");

                if (!Directory.Exists(listsPath)) Directory.CreateDirectory(listsPath);

                string currentUserList = "";
                if (File.Exists(mediaListPath))
                {
                    currentUserList = File.ReadAllText(mediaListPath);
                }

                string[] domainsToAdd = new string[]
                {
                    "pornhub.com", "phncdn.com", "phprcdn.com", "models.com", "gamma.app"
                };

                int addedCount = 0;
                using (StreamWriter sw = File.AppendText(mediaListPath))
                {
                    foreach (string domain in domainsToAdd)
                    {
                        if (!currentUserList.Contains(domain))
                        {
                            sw.WriteLine(domain);
                            addedCount++;
                        }
                    }
                }

                OnLog?.Invoke($"[✓] Медиа-ресурсы активированы (добавлено {addedCount} доменов).");
                OnLog?.Invoke("Перезапустите обход для применения.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[✗] Ошибка при добавлении доменов: {ex.Message}");
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
    }
}