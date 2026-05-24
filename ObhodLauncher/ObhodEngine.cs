using System;
using System.Diagnostics;
using System.IO;

namespace ZapretWPF
{
    public class ZapretEngine
    {
        private Process _winwsProcess;
        public Action<string> OnLog { get; set; }

        public void Start(bool enableDiscord, bool enableYouTube)
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                OnLog?.Invoke("Обход уже запущен!");
                return;
            }

            CreateDummyListsIfMissing();
            string args = GetArguments(enableDiscord, enableYouTube);
            OnLog?.Invoke($"Запуск winws.exe с аргументами:\n{args}\n");

            try
            {
                _winwsProcess = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = @"bin\winws.exe", // После сборки файлы лежат в папке bin
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

                OnLog?.Invoke("=== Обход успешно запущен ===");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка запуска: {ex.Message}. Убедитесь, что файлы скопировались правильно.");
            }
        }

        public void Stop()
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                _winwsProcess.Kill();
                _winwsProcess.Dispose();
                _winwsProcess = null;
                OnLog?.Invoke("=== Обход остановлен ===");
            }
        }

        public void InstallService(bool enableDiscord, bool enableYouTube)
        {
            CreateDummyListsIfMissing();
            string args = GetArguments(enableDiscord, enableYouTube);
            string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "winws.exe");

            string scArgs = $"create \"ObhodService\" binPath= \"\\\"{binPath}\\\" {args.Replace("\"", "\\\"")}\" start= auto displayname= \"ObhodLauncher Background Service\"";

            RunAsAdmin("sc.exe", "stop ObhodService");
            RunAsAdmin("sc.exe", "delete ObhodService");
            RunAsAdmin("sc.exe", scArgs);
            RunAsAdmin("sc.exe", "start ObhodService");

            OnLog?.Invoke("Служба установлена и запущена на уровне системы (автозапуск).");
        }

        private void CreateDummyListsIfMissing()
        {
            // Убеждаемся, что при запуске .exe все пользовательские файлы на месте
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

        private string GetArguments(bool discord, bool youtube)
        {
            // Здесь мы используем актуальную, самую мощную стратегию от Flowseal
            string gameFilterTcp = "12";
            string gameFilterUdp = "12";

            string bin = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin") + "\\";
            string lists = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lists") + "\\";

            string args = $"--wf-tcp=80,443,2053,2083,2087,2096,8443,{gameFilterTcp} --wf-udp=443,19294-19344,50000-50100,{gameFilterUdp} ";

            // Общие ресурсы и сервисы
            args += $"--filter-udp=443 --hostlist=\"{lists}list-general.txt\" --hostlist=\"{lists}list-general-user.txt\" --hostlist-exclude=\"{lists}list-exclude.txt\" --hostlist-exclude=\"{lists}list-exclude-user.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
            args += $"--filter-tcp=80,443 --hostlist=\"{lists}list-general.txt\" --hostlist=\"{lists}list-general-user.txt\" --hostlist-exclude=\"{lists}list-exclude.txt\" --hostlist-exclude=\"{lists}list-exclude-user.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_4pda_to.bin\" --new ";

            if (discord)
            {
                args += $"--filter-udp=19294-19344,50000-50100 --filter-l7=discord,stun --dpi-desync=fake --dpi-desync-fake-discord=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-fake-stun=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-repeats=6 --new ";
                args += $"--filter-tcp=2053,2083,2087,2096,8443 --hostlist-domains=discord.media --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
            }

            if (youtube)
            {
                args += $"--filter-tcp=443 --hostlist=\"{lists}list-google.txt\" --ip-id=zero --dpi-desync=multisplit --dpi-desync-split-seqovl=681 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_www_google_com.bin\" --new ";
            }

            // IPSET фильтры
            args += $"--filter-udp=443 --ipset=\"{lists}ipset-all.txt\" --hostlist-exclude=\"{lists}list-exclude.txt\" --hostlist-exclude=\"{lists}list-exclude-user.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\" --dpi-desync=fake --dpi-desync-repeats=6 --dpi-desync-fake-quic=\"{bin}quic_initial_www_google_com.bin\" --new ";
            args += $"--filter-tcp=80,443,8443 --ipset=\"{lists}ipset-all.txt\" --hostlist-exclude=\"{lists}list-exclude.txt\" --hostlist-exclude=\"{lists}list-exclude-user.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\" --dpi-desync=multisplit --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_4pda_to.bin\" --new ";

            // Фильтры для игр (отключены по умолчанию)
            args += $"--filter-tcp={gameFilterTcp} --ipset=\"{lists}ipset-all.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\" --dpi-desync=multisplit --dpi-desync-any-protocol=1 --dpi-desync-cutoff=n3 --dpi-desync-split-seqovl=568 --dpi-desync-split-pos=1 --dpi-desync-split-seqovl-pattern=\"{bin}tls_clienthello_4pda_to.bin\" --new ";
            args += $"--filter-udp={gameFilterUdp} --ipset=\"{lists}ipset-all.txt\" --ipset-exclude=\"{lists}ipset-exclude.txt\" --ipset-exclude=\"{lists}ipset-exclude-user.txt\" --dpi-desync=fake --dpi-desync-repeats=12 --dpi-desync-any-protocol=1 --dpi-desync-fake-unknown-udp=\"{bin}quic_initial_dbankcloud_ru.bin\" --dpi-desync-cutoff=n2";

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