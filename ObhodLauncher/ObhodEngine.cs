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
            OnLog?.Invoke($"Запуск winws.exe с аргументами (Стратегия {strategyIndex}):\n{args}\n");

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

                OnLog?.Invoke("=== Обход успешно запущен (Консольный режим) ===");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка запуска: {ex.Message}. Убедитесь, что файлы скопировались правильно.");
            }
        }

        public void Stop()
        {
            // Останавливаем консольный процесс, если он был
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                _winwsProcess.Kill();
                _winwsProcess.Dispose();
                _winwsProcess = null;
                OnLog?.Invoke("=== Консольный процесс остановлен ===");
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

            OnLog?.Invoke($"=== Служба установлена и запущена на уровне системы (Стратегия {strategyIndex}) ===");
            OnLog?.Invoke("Теперь вы можете закрыть программу. Обход продолжит работать!");
        }

        public void RemoveService()
        {
            RunAsAdmin("sc.exe", "stop ObhodService");
            RunAsAdmin("sc.exe", "delete ObhodService");
            OnLog?.Invoke("=== Фоновая служба удалена. Обход полностью отключен. ===");
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

            if (strategyIndex == 0) // General (Основная)
            {
                string args = $"--wf-tcp=80,443,2053,2083,2087,2096,8443,12 --wf-udp=443,19294-19344,50000-50100,12 ";
                args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_4pda_to.bin\" --new ";

                if (discord)
                {
                    args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                    args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                }
                if (youtube)
                {
                    args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
                }
                args += $"--filter-udp=443 --ipset=\"{lists}ipset-all.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
                args += $"--filter-tcp=80,443,8443 --ipset=\"{lists}ipset-all.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_4pda_to.bin\"";
                return args;
            }
            else if (strategyIndex == 1) // ALT 1 (Чуть более агрессивный fake)
            {
                // Это пример стратегии ALT, ты можешь менять эти параметры позже на те, которые тебе нравятся
                return $"--wf-tcp=80,443 --wf-udp=443,50000-65535 --filter-udp=443 --hostlist=\"{lists}list-discord.txt\" --dpi-desync=fake --dpi-desync-udplen-increment=10 --dpi-desync-repeats=6 --dpi-desync-udplen-pattern=0xDeadBeef --dpi-desync-any-protocol --new --filter-udp=50000-65535 --dpi-desync=anycast --dpi-desync-any-protocol --dpi-desync-cutoff=d3 --new --filter-tcp=80 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --new --filter-tcp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new --filter-tcp=443 --hostlist=\"{lists}list-discord.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1";
            }
            else // ALT 2 (Для Дом.ру / Ростелеком)
            {
                return $"--wf-tcp=80,443 --wf-udp=443,50000-65535 --filter-udp=443 --hostlist=\"{lists}list-discord.txt\" --dpi-desync=fake --dpi-desync-udplen-increment=10 --dpi-desync-repeats=6 --dpi-desync-udplen-pattern=0xDeadBeef --dpi-desync-any-protocol --new --filter-udp=50000-65535 --dpi-desync=anycast --dpi-desync-any-protocol --dpi-desync-cutoff=d3 --new --filter-tcp=80 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,disorder2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --new --filter-tcp=443 --hostlist=\"{lists}list-general.txt\" --dpi-desync=fake,disorder2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new --filter-tcp=443 --hostlist=\"{lists}list-discord.txt\" --dpi-desync=fake,disorder2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1";
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