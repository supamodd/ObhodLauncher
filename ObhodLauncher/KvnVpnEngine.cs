using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.Win32;

namespace ZapretWPF
{
    public enum VpnCoreType
    {
        None,
        SingBox,
        Xray
    }

    public class KvnVpnEngine
    {
        private Process? _coreProcess;
        private VpnCoreType _activeCore = VpnCoreType.None;
        private readonly string _baseDir;
        private readonly string _coreDir;
        private readonly string _tempDir;

        public Action<string>? OnLog { get; set; }
        public bool IsConnected => _coreProcess != null && !_coreProcess.HasExited;
        public VpnCoreType ActiveCore => _activeCore;

        public KvnVpnEngine()
        {
            _baseDir = AppDomain.CurrentDomain.BaseDirectory;
            _coreDir = Path.Combine(_baseDir, "vpn-core");
            _tempDir = Path.Combine(_baseDir, "kvn-temp");
            Directory.CreateDirectory(_tempDir);
        }

        public VpnCoreType DetectCore()
        {
            if (File.Exists(Path.Combine(_coreDir, "sing-box.exe")))
                return VpnCoreType.SingBox;
            if (File.Exists(Path.Combine(_coreDir, "xray.exe")))
                return VpnCoreType.Xray;
            return VpnCoreType.None;
        }

        public string GetCorePath(VpnCoreType type)
        {
            string fileName = type == VpnCoreType.SingBox ? "sing-box.exe" : "xray.exe";
            return Path.Combine(_coreDir, fileName);
        }

        public async Task<bool> ConnectAsync(KvnServerConfig config)
        {
            Disconnect();

            var coreType = DetectCore();
            if (coreType == VpnCoreType.None)
            {
                OnLog?.Invoke("[КВН] ❌ Не найдено ядро VPN. Создайте папку 'vpn-core' рядом с .exe и положите туда sing-box.exe или xray.exe");
                OnLog?.Invoke("[КВН] sing-box рекомендуется для полного TUN-туннеля.");
                return false;
            }

            OnLog?.Invoke($"[КВН] Используется ядро: {coreType}");

            try
            {
                if (coreType == VpnCoreType.SingBox)
                {
                    return await StartSingBoxAsync(config);
                }
                else
                {
                    return await StartXrayAsync(config);
                }
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[КВН] Ошибка подключения: {ex.Message}");
                return false;
            }
        }

        public void Disconnect()
        {
            try
            {
                if (_coreProcess != null && !_coreProcess.HasExited)
                {
                    _coreProcess.Kill();
                    _coreProcess.Dispose();
                    _coreProcess = null;
                }
            }
            catch { }

            if (_activeCore == VpnCoreType.Xray)
            {
                DisableSystemProxy();
            }

            _activeCore = VpnCoreType.None;
            OnLog?.Invoke("[КВН] VPN отключён.");
        }

        private async Task<bool> StartSingBoxAsync(KvnServerConfig config)
        {
            string configPath = Path.Combine(_tempDir, "sing-box-config.json");
            string json = BuildSingBoxConfig(config);
            File.WriteAllText(configPath, json, Encoding.UTF8);

            OnLog?.Invoke("[КВН] Запуск sing-box в режиме TUN...");

            _coreProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetCorePath(VpnCoreType.SingBox),
                    Arguments = $"run -c \"{configPath}\"",
                    WorkingDirectory = _coreDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _coreProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke($"[sing-box] {e.Data}"); };
            _coreProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke($"[sing-box] {e.Data}"); };

            _coreProcess.Start();
            _coreProcess.BeginOutputReadLine();
            _coreProcess.BeginErrorReadLine();

            await Task.Delay(1500);

            if (_coreProcess.HasExited)
            {
                OnLog?.Invoke($"[КВН] sing-box не запустился. Код: {_coreProcess.ExitCode}");
                _coreProcess = null;
                return false;
            }

            _activeCore = VpnCoreType.SingBox;
            OnLog?.Invoke("[КВН] ✅ Подключено через sing-box TUN!");
            return true;
        }

        private async Task<bool> StartXrayAsync(KvnServerConfig config)
        {
            string configPath = Path.Combine(_tempDir, "xray-config.json");
            string json = BuildXrayConfig(config);
            File.WriteAllText(configPath, json, Encoding.UTF8);

            OnLog?.Invoke("[КВН] Запуск xray...");

            _coreProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = GetCorePath(VpnCoreType.Xray),
                    Arguments = $"-c \"{configPath}\"",
                    WorkingDirectory = _coreDir,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                }
            };

            _coreProcess.OutputDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke($"[xray] {e.Data}"); };
            _coreProcess.ErrorDataReceived += (s, e) => { if (!string.IsNullOrEmpty(e.Data)) OnLog?.Invoke($"[xray] {e.Data}"); };

            _coreProcess.Start();
            _coreProcess.BeginOutputReadLine();
            _coreProcess.BeginErrorReadLine();

            await Task.Delay(1500);

            if (_coreProcess.HasExited)
            {
                OnLog?.Invoke($"[КВН] xray не запустился. Код: {_coreProcess.ExitCode}");
                _coreProcess = null;
                return false;
            }

            SetSystemProxy("127.0.0.1:10809");
            _activeCore = VpnCoreType.Xray;
            OnLog?.Invoke("[КВН] ✅ Подключено через xray (системный прокси 127.0.0.1:10809)!");
            return true;
        }

        private string BuildSingBoxConfig(KvnServerConfig cfg)
        {
            JsonObject outbound = cfg.Protocol.ToUpperInvariant() switch
            {
                "VLESS" => BuildVlessOutboundSingBox(cfg),
                "VMESS" => BuildVmessOutboundSingBox(cfg),
                "TROJAN" => BuildTrojanOutboundSingBox(cfg),
                _ => throw new NotSupportedException($"Протокол {cfg.Protocol} не поддерживается для туннеля.")
            };

            var config = new JsonObject
            {
                ["log"] = new JsonObject { ["level"] = "info" },
                ["dns"] = new JsonObject
                {
                    ["servers"] = new JsonArray
                    {
                        new JsonObject { ["tag"] = "google", ["address"] = "tls://8.8.8.8" },
                        new JsonObject { ["tag"] = "local", ["address"] = "223.5.5.5", ["detour"] = "direct" }
                    },
                    ["rules"] = new JsonArray
                    {
                        new JsonObject { ["geosite"] = "cn", ["server"] = "local" }
                    },
                    ["final"] = "google",
                    ["strategy"] = "ipv4_only"
                },
                ["inbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "tun",
                        ["inet4_address"] = "172.19.0.1/30",
                        ["auto_route"] = true,
                        ["strict_route"] = false,
                        ["stack"] = "gvisor",
                        ["sniff"] = true
                    }
                },
                ["outbounds"] = new JsonArray
                {
                    outbound,
                    new JsonObject { ["type"] = "direct", ["tag"] = "direct" },
                    new JsonObject { ["type"] = "block", ["tag"] = "block" }
                },
                ["route"] = new JsonObject
                {
                    ["auto_detect_interface"] = true,
                    ["final"] = "proxy"
                }
            };

            return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private JsonObject BuildVlessOutboundSingBox(KvnServerConfig cfg)
        {
            JsonObject tls = cfg.Security.ToLowerInvariant() switch
            {
                "tls" => new JsonObject { ["enabled"] = true, ["server_name"] = cfg.Sni, ["insecure"] = false },
                "reality" => new JsonObject
                {
                    ["enabled"] = true,
                    ["server_name"] = cfg.Sni,
                    ["insecure"] = false,
                    ["utls"] = new JsonObject { ["enabled"] = true, ["fingerprint"] = cfg.Fingerprint }
                },
                _ => new JsonObject { ["enabled"] = false }
            };

            JsonObject? transport = cfg.Network.ToLowerInvariant() switch
            {
                "ws" => new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = cfg.Path,
                    ["headers"] = new JsonObject { ["Host"] = cfg.Host }
                },
                "grpc" => new JsonObject
                {
                    ["type"] = "grpc",
                    ["service_name"] = cfg.Path.TrimStart('/')
                },
                _ => null
            };

            var outbound = new JsonObject
            {
                ["type"] = "vless",
                ["tag"] = "proxy",
                ["server"] = cfg.Address,
                ["server_port"] = cfg.Port,
                ["uuid"] = cfg.Id,
                ["tls"] = tls
            };

            if (transport != null)
                outbound["transport"] = transport;

            return outbound;
        }

        private JsonObject BuildVmessOutboundSingBox(KvnServerConfig cfg)
        {
            JsonObject tls = cfg.Security.ToLowerInvariant() == "tls"
                ? new JsonObject { ["enabled"] = true, ["server_name"] = cfg.Sni, ["insecure"] = false }
                : new JsonObject { ["enabled"] = false };

            JsonObject? transport = cfg.Network.ToLowerInvariant() switch
            {
                "ws" => new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = cfg.Path,
                    ["headers"] = new JsonObject { ["Host"] = cfg.Host }
                },
                "grpc" => new JsonObject
                {
                    ["type"] = "grpc",
                    ["service_name"] = cfg.Path.TrimStart('/')
                },
                _ => null
            };

            var outbound = new JsonObject
            {
                ["type"] = "vmess",
                ["tag"] = "proxy",
                ["server"] = cfg.Address,
                ["server_port"] = cfg.Port,
                ["uuid"] = cfg.Id,
                ["security"] = "auto",
                ["tls"] = tls
            };

            if (transport != null)
                outbound["transport"] = transport;

            return outbound;
        }

        private JsonObject BuildTrojanOutboundSingBox(KvnServerConfig cfg)
        {
            JsonObject? transport = cfg.Network.ToLowerInvariant() switch
            {
                "ws" => new JsonObject
                {
                    ["type"] = "ws",
                    ["path"] = cfg.Path,
                    ["headers"] = new JsonObject { ["Host"] = cfg.Host }
                },
                "grpc" => new JsonObject
                {
                    ["type"] = "grpc",
                    ["service_name"] = cfg.Path.TrimStart('/')
                },
                _ => null
            };

            var outbound = new JsonObject
            {
                ["type"] = "trojan",
                ["tag"] = "proxy",
                ["server"] = cfg.Address,
                ["server_port"] = cfg.Port,
                ["password"] = cfg.Password,
                ["tls"] = new JsonObject { ["enabled"] = true, ["server_name"] = cfg.Sni, ["insecure"] = false }
            };

            if (transport != null)
                outbound["transport"] = transport;

            return outbound;
        }

        private string BuildXrayConfig(KvnServerConfig cfg)
        {
            var streamSettings = new JsonObject
            {
                ["network"] = cfg.Network,
                ["security"] = cfg.Security
            };

            if (cfg.Security.ToLowerInvariant() == "tls")
                streamSettings["tlsSettings"] = new JsonObject { ["serverName"] = cfg.Sni };

            if (cfg.Network.ToLowerInvariant() == "ws")
                streamSettings["wsSettings"] = new JsonObject { ["path"] = cfg.Path, ["headers"] = new JsonObject { ["Host"] = cfg.Host } };

            if (cfg.Network.ToLowerInvariant() == "grpc")
                streamSettings["grpcSettings"] = new JsonObject { ["serviceName"] = cfg.Path.TrimStart('/') };

            JsonObject outbound;
            if (cfg.Protocol.ToUpperInvariant() == "VLESS")
            {
                outbound = new JsonObject
                {
                    ["protocol"] = "vless",
                    ["settings"] = new JsonObject
                    {
                        ["vnext"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["address"] = cfg.Address,
                                ["port"] = cfg.Port,
                                ["users"] = new JsonArray { new JsonObject { ["id"] = cfg.Id, ["encryption"] = "none" } }
                            }
                        }
                    },
                    ["streamSettings"] = streamSettings
                };
            }
            else if (cfg.Protocol.ToUpperInvariant() == "VMESS")
            {
                outbound = new JsonObject
                {
                    ["protocol"] = "vmess",
                    ["settings"] = new JsonObject
                    {
                        ["vnext"] = new JsonArray
                        {
                            new JsonObject
                            {
                                ["address"] = cfg.Address,
                                ["port"] = cfg.Port,
                                ["users"] = new JsonArray { new JsonObject { ["id"] = cfg.Id, ["security"] = "auto" } }
                            }
                        }
                    },
                    ["streamSettings"] = streamSettings
                };
            }
            else
            {
                outbound = new JsonObject
                {
                    ["protocol"] = "trojan",
                    ["settings"] = new JsonObject
                    {
                        ["servers"] = new JsonArray
                        {
                            new JsonObject { ["address"] = cfg.Address, ["port"] = cfg.Port, ["password"] = cfg.Password }
                        }
                    },
                    ["streamSettings"] = streamSettings
                };
            }

            var config = new JsonObject
            {
                ["log"] = new JsonObject { ["loglevel"] = "info" },
                ["inbounds"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["tag"] = "socks",
                        ["port"] = 10808,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "socks",
                        ["settings"] = new JsonObject { ["auth"] = "noauth", ["udp"] = true }
                    },
                    new JsonObject
                    {
                        ["tag"] = "http",
                        ["port"] = 10809,
                        ["listen"] = "127.0.0.1",
                        ["protocol"] = "http"
                    }
                },
                ["outbounds"] = new JsonArray
                {
                    outbound,
                    new JsonObject { ["protocol"] = "freedom", ["tag"] = "direct" }
                },
                ["routing"] = new JsonObject
                {
                    ["domainStrategy"] = "IPIfNonMatch",
                    ["rules"] = new JsonArray()
                }
            };

            return config.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        private void SetSystemProxy(string proxyAddress)
        {
            try
            {
                RegistryKey? key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
                if (key == null) return;

                key.SetValue("ProxyEnable", 1, RegistryValueKind.DWord);
                key.SetValue("ProxyServer", proxyAddress, RegistryValueKind.String);
                key.Close();

                RefreshSystemProxy();
                OnLog?.Invoke("[КВН] Системный прокси включён: " + proxyAddress);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[КВН] Не удалось включить системный прокси: {ex.Message}");
            }
        }

        private void DisableSystemProxy()
        {
            try
            {
                RegistryKey? key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Internet Settings", true);
                if (key == null) return;

                key.SetValue("ProxyEnable", 0, RegistryValueKind.DWord);
                key.DeleteValue("ProxyServer", false);
                key.Close();

                RefreshSystemProxy();
                OnLog?.Invoke("[КВН] Системный прокси отключён.");
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[КВН] Не удалось отключить системный прокси: {ex.Message}");
            }
        }

        private void RefreshSystemProxy()
        {
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_SETTINGS_CHANGED, IntPtr.Zero, 0);
            InternetSetOption(IntPtr.Zero, INTERNET_OPTION_REFRESH, IntPtr.Zero, 0);
        }

        private const int INTERNET_OPTION_SETTINGS_CHANGED = 39;
        private const int INTERNET_OPTION_REFRESH = 37;

        [DllImport("wininet.dll", SetLastError = true)]
        private static extern bool InternetSetOption(IntPtr hInternet, int dwOption, IntPtr lpBuffer, int dwBufferLength);
    }
}