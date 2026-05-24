using System;
using System.Diagnostics;
using System.IO;

namespace ZapretWPF
{
    public class ZapretEngine
    {
        private Process _winwsProcess;
        public Action<string> OnLog { get; set; } // Событие для передачи логов в UI

        public void Start(bool enableDiscord, bool enableYouTube)
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                OnLog?.Invoke("Zapret уже запущен!");
                return;
            }

            string args = GetArguments(enableDiscord, enableYouTube);
            OnLog?.Invoke($"Запуск winws.exe с аргументами:\n{args}\n");

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
                        CreateNoWindow = true // Запускаем скрыто, без черного окна
                    }
                };

                _winwsProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke(e.Data); };
                _winwsProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke("ERROR: " + e.Data); };

                _winwsProcess.Start();
                _winwsProcess.BeginOutputReadLine();
                _winwsProcess.BeginErrorReadLine();

                OnLog?.Invoke("=== Процесс winws.exe успешно запущен ===");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"Ошибка запуска: {ex.Message}. Проверь наличие папки bin с winws.exe");
            }
        }

        public void Stop()
        {
            if (_winwsProcess != null && !_winwsProcess.HasExited)
            {
                _winwsProcess.Kill();
                _winwsProcess.Dispose();
                _winwsProcess = null;
                OnLog?.Invoke("=== Процесс остановлен ===");
            }
        }

        public void InstallService(bool enableDiscord, bool enableYouTube)
        {
            string args = GetArguments(enableDiscord, enableYouTube);
            string binPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "bin", "winws.exe");

            // Точный аналог service_install.bat
            // Экранируем кавычки для binPath и аргументов, чтобы sc.exe всё правильно понял
            string scArgs = $"create \"ZapretService\" binPath= \"\\\"{binPath}\\\" {args.Replace("\"", "\\\"")}\" start= auto displayname= \"Zapret (WPF Clone)\"";

            RunAsAdmin("sc.exe", "stop ZapretService"); // На всякий случай останавливаем старую
            RunAsAdmin("sc.exe", "delete ZapretService");
            RunAsAdmin("sc.exe", scArgs);
            RunAsAdmin("sc.exe", "start ZapretService");

            OnLog?.Invoke("Служба установлена и запущена на уровне системы (автозапуск).");
        }

        // Этот метод генерирует те самые параметры, которые лежат в батниках Flowseal
        private string GetArguments(bool discord, bool youtube)
        {
            // Здесь должна быть ТОЧНАЯ копия параметров из .bat файлов Flowseal!
            // В примере ниже приведены типовые рабочие стратегии для примера. 
            // Чтобы было 1 в 1, скопируй аргументы из general+discord.bat, youtube.bat и т.д.

            string args = "--wf-tcp=80,443 --wf-udp=443,50000-65535";

            if (discord && youtube)
            {
                args += " --filter-udp=443 --hostlist=\"lists\\list-discord.txt\" --dpi-desync=fake --dpi-desync-udplen-increment=10 --dpi-desync-repeats=6 --dpi-desync-udplen-pattern=0xDeadBeef --dpi-desync-any-protocol --new " +
                        "--filter-udp=50000-65535 --dpi-desync=anycast --dpi-desync-any-protocol --dpi-desync-cutoff=d3 --new " +
                        "--filter-tcp=80 --hostlist=\"lists\\list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --new " +
                        "--filter-tcp=443 --hostlist=\"lists\\list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1 --new " +
                        "--filter-tcp=443 --hostlist=\"lists\\list-discord.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1";
            }
            else if (youtube)
            {
                // Логика из youtube.bat
                args += " --filter-tcp=80 --hostlist=\"lists\\list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --new --filter-tcp=443 --hostlist=\"lists\\list-general.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1";
            }
            else if (discord)
            {
                // Логика из discord.bat
                args += " --filter-udp=443 --hostlist=\"lists\\list-discord.txt\" --dpi-desync=fake --dpi-desync-udplen-increment=10 --dpi-desync-repeats=6 --dpi-desync-udplen-pattern=0xDeadBeef --dpi-desync-any-protocol --new --filter-udp=50000-65535 --dpi-desync=anycast --dpi-desync-any-protocol --dpi-desync-cutoff=d3 --new --filter-tcp=443 --hostlist=\"lists\\list-discord.txt\" --dpi-desync=fake,split2 --dpi-desync-autohost=sni --dpi-desync-fooling=md5sig --dpi-desync-split-seqovl=1";
            }

            return args;
        }

        private void RunAsAdmin(string fileName, string args)
        {
            var process = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = args,
                UseShellExecute = true, // Нужно для запроса прав
                Verb = "runas", // Запрашиваем права администратора (окно UAC)
                WindowStyle = ProcessWindowStyle.Hidden
            });
            process?.WaitForExit();
        }
    }
}