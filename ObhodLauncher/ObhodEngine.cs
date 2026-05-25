using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace ZapretWPF
{
    public class ZapretEngine
    {
        private Process _winwsProcess;
        public Action<string> OnLog { get; set; }

        public void Start(bool enableDiscord, bool enableYouTube, int strategyIndex)
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                OnLog?.Invoke("Обход уже запущен в режиме консоли!");
                return;
            }

            CreateDummyListsIfMissing();
            string args = GetArguments(enableDiscord, enableYouTube, strategyIndex);
            OnLog?.Invoke($"[Запуск winws.exe] Стратегия #{strategyIndex + 1}\nАргументы: {args}\n");

            try
            {
                _winwsProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = @"bin\winws.exe",
                        Arguments = args,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        CreateNoWindow = true
                    }
                };

                // Читаем логи и ошибки
                _winwsProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke(e.Data); };
                _winwsProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke("ОШИБКА WINWS: " + e.Data); };

                _winwsProcess.Start();
                _winwsProcess.BeginOutputReadLine();
                _winwsProcess.BeginErrorReadLine();

                // Проверяем, не крашнулся ли он мгновенно (в течение 500мс)
                if (_winwsProcess.WaitForExit(500))
                {
                    OnLog?.Invoke($"[КРИТИЧЕСКАЯ ОШИБКА] winws.exe мгновенно закрылся (Код: {_winwsProcess.ExitCode}). Проверьте логи выше, возможно параметр не поддерживается.");
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

        public void InstallService(bool enableDiscord, bool enableYouTube, int strategyIndex)
        {
            CreateDummyListsIfMissing();
            string args = GetArguments(enableDiscord, enableYouTube, strategyIndex);
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
        }

        public void FlushDNS()
        {
            try
            {
                OnLog?.Invoke("=== Выполнение сброса сети ===");

                // Выполняем очистку DNS
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

        private string GetArguments(bool discord, bool youtube, int strategyIndex)
        {
            string bin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin") + "\\";
            string lists = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists") + "\\";

            string args = $"--wf-tcp=80,443,2053,2083,2087,2096,8443 --wf-udp=443,19294-19344,50000-50100 ";

            switch (strategyIndex)
            {
                case 0: // 1. Flowseal General
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_4pda_to.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    break;

                case 1: // 2. Flowseal ALT (fakedsplit + ts)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=\"{bin}stun.bin\" --dpi-desync-fake-tls=\"{bin}tls_clienthello_www_google_com.bin\" --dpi-desync-fake-http=\"{bin}tls_clienthello_max_ru.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,fakedsplit --dpi-desync-repeats=6 --dpi-desync-fooling=ts --dpi-desync-fakedsplit-pattern=0x00 --dpi-desync-fake-tls=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    break;

                case 2: // 3. Flowseal ALT 2 (multisplit pos=2)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=652 --dpi-desync-split-pos=2 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=652 --dpi-desync-split-pos=2 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-seqovl=652 --dpi-desync-split-pos=2 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    break;

                case 3: // 4. Flowseal ALT 3 (hostfakesplit)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,hostfakesplit --dpi-desync-fake-tls-mod=rnd,dupsid,sni=ya.ru --dpi-desync-hostfakesplit-mod=host=ya.ru,altorder=1 --dpi-desync-fooling=ts --dpi-desync-fake-http=\"{bin}tls_clienthello_max_ru.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,hostfakesplit --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-hostfakesplit-mod=host=www.google.com,altorder=1 --dpi-desync-fooling=ts --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,hostfakesplit --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-hostfakesplit-mod=host=www.google.com,altorder=1 --dpi-desync-fooling=ts --new ";
                    }
                    break;

                case 4: // 5. Flowseal FAKE TLS AUTO ALT
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --dpi-desync-fake-http=\"{bin}tls_clienthello_max_ru.bin\" --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=fake,fakedsplit --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=2 --dpi-desync-repeats=8 --dpi-desync-fake-tls-mod=rnd,dupsid,sni=www.google.com --new ";
                    }
                    break;

                case 5: // 6. SupaModd Custom (Билайн / Регионы - Фикс Discord)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=10000000 --dpi-desync-repeats=6 --new ";

                    if (discord)
                    {
                        // UDP (Голос). Фильтруем по l7 протоколам (discord,stun) и кормим DPI фейковыми бинарниками
                        args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=11 --new ";
                        // UDP (Голос). Fallback-правило для неопознанного UDP-трафика дискорда, добавляем инкремент размера, чтобы сбить сигнатуры
                        args += $"--filter-udp=19294-19344,50000-50100 --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-udplen-increment=2 --dpi-desync-any-protocol --new ";

                        // TCP (Медиа)
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=10000000 --dpi-desync-repeats=6 --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --dpi-desync=fake,split2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq --dpi-desync-badseq-increment=10000000 --dpi-desync-repeats=6 --new ";
                    }
                    break;
            }

            return args;
        }

        public async Task TestConnectionAsync()
        {
            OnLog?.Invoke("=== Запуск проверки соединения ===");

            // Проверяем Discord (API и страницу статуса вместо CDN)
            await CheckUrlAsync("Discord API", "https://discord.com/api/v9/gateway");
            await CheckUrlAsync("Discord Status", "https://discordstatus.com");

            // Проверяем YouTube (Главная и скрипт плеера вместо googlevideo)
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
                    // Таймаут 4 секунды
                    client.Timeout = TimeSpan.FromSeconds(4);

                    // Эмулируем настоящий браузер Chrome
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

                    Stopwatch sw = Stopwatch.StartNew();
                    HttpResponseMessage response = await client.GetAsync(url);
                    sw.Stop();

                    // Если сервер вернул что угодно (OK, Forbidden, NotFound, Unauthorized) — значит он ДОСТУПЕН!
                    // Блокировка провайдера всегда выдает Таймаут или обрыв соединения.
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