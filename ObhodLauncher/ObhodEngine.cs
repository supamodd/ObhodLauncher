using System;
using System.Diagnostics;
using System.IO;

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

                _winwsProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke(e.Data); };
                _winwsProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke("ERROR: " + e.Data); };

                _winwsProcess.Start();
                _winwsProcess.BeginOutputReadLine();
                _winwsProcess.BeginErrorReadLine();

                OnLog?.Invoke("=== Обход успешно запущен (Тест) ===");
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

        private string GetArguments(bool discord, bool youtube, int strategyIndex)
        {
            string bin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin") + "\\";
            string lists = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists") + "\\";

            string baseTcpPorts = "80,443,2053,2083,2087,2096,8443,12";
            string baseUdpPorts = "443,19294-19344,50000-50100,12";
            string args = $"--wf-tcp={baseTcpPorts} --wf-udp={baseUdpPorts} ";

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

                case 1: // 2. Flowseal ALT 1 (Fake)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-udplen-increment=10 --dpi-desync-repeats=6 --dpi-desync-udplen-pattern=0xDeadBeef --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=50000-65535 --dpi-desync=anycast --dpi-desync-any-protocol --dpi-desync-cutoff=d3 --new ";
                        args += $"--filter-tcp=443 --hostlist-domains=discord.media --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new ";
                    }
                    break;

                case 2: // 3. Flowseal ALT 2 (Disorder)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-udplen-increment=10 --dpi-desync-repeats=6 --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,disorder2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=50000-65535 --dpi-desync=anycast --dpi-desync-any-protocol --dpi-desync-cutoff=d3 --new ";
                        args += $"--filter-tcp=443 --hostlist-domains=discord.media --dpi-desync=fake,disorder2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --dpi-desync=fake,disorder2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new ";
                    }
                    break;

                case 3: // 4. Flowseal Fake TLS
                    args += $"--filter-tcp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-split-seqovl=1 --dpi-desync-fake-tls=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --dpi-desync=fake,split2 --dpi-desync-split-seqovl=1 --dpi-desync-fake-tls=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                    }
                    if (discord)
                    {
                        args += $"--filter-udp=50000-65535 --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                    }
                    break;

                case 4: // 5. SupaModd Custom (Макс. Пробив)
                    // Кастомная тактика: комбинация disorder2 + badseq + md5sig для обмана сложных систем DPI (МТС, Ростелеком, Дом.ру)
                    args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-udplen-increment=2 --dpi-desync-any-protocol --new ";
                    args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,disorder2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq,md5sig --dpi-desync-autohost=sni --new ";
                    if (discord)
                    {
                        args += $"--filter-udp=19294-19344,50000-50100 --dpi-desync=fake --dpi-desync-repeats=11 --dpi-desync-udplen-increment=2 --dpi-desync-any-protocol --new ";
                        args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=fake,disorder2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq,md5sig --dpi-desync-autohost=sni --new ";
                    }
                    if (youtube)
                    {
                        args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --dpi-desync=fake,disorder2 --dpi-desync-split-pos=1 --dpi-desync-fooling=badseq,md5sig --dpi-desync-autohost=sni --new ";
                    }
                    break;
            }

            // Добавляем общий fallback для всех остальных заблокированных сайтов (IPSet)
            args += $"--filter-udp=443 --ipset=\"{lists}ipset-all.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --new ";
            args += $"--filter-tcp=80,443 --ipset=\"{lists}ipset-all.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1";

            return args;
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