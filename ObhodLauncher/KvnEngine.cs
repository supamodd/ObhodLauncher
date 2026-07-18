using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ZapretWPF
{
    public class KvnServerConfig
    {
        public string Protocol { get; set; } = "unknown";
        public string Remark { get; set; } = "";
        public string Address { get; set; } = "";
        public int Port { get; set; }
        public string Id { get; set; } = "";
        public string Password { get; set; } = "";
        public string RawLink { get; set; } = "";
        public string Security { get; set; } = "none";
        public string Network { get; set; } = "tcp";
        public string Path { get; set; } = "";
        public string Host { get; set; } = "";
        public string Sni { get; set; } = "";
        public string Fingerprint { get; set; } = "";
        public string PublicKey { get; set; } = "";
        public string ShortId { get; set; } = "";
        public string SpiderX { get; set; } = "";

        public string DisplayInfo => $"{Protocol} • {Address}:{Port} • {Network} • {Security}";
    }

    public class KvnEngine
    {
        private readonly HttpClient _client;
        public Action<string>? OnLog { get; set; }
        public string LastRawSubscription { get; private set; } = "";

        public KvnEngine()
        {
            var handler = new HttpClientHandler
            {
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
                AllowAutoRedirect = true,
                AutomaticDecompression = DecompressionMethods.All
            };
            _client = new HttpClient(handler);
            _client.Timeout = TimeSpan.FromSeconds(20);
            _client.DefaultRequestHeaders.Add("User-Agent", "v2rayN/6.0");
            _client.DefaultRequestHeaders.Add("Accept", "*/*");
            _client.DefaultRequestHeaders.Add("Cache-Control", "no-cache");
        }

        public async Task<string> GetRawSubscriptionAsync(string url)
        {
            try
            {
                string raw = await _client.GetStringAsync(url);
                LastRawSubscription = raw;
                return raw;
            }
            catch (Exception ex)
            {
                string details = ex.Message;
                if (ex.InnerException != null)
                    details += " | Inner: " + ex.InnerException.Message;
                OnLog?.Invoke($"[КВН] Ошибка загрузки: {details}");
                LastRawSubscription = "";
                return "";
            }
        }

        public async Task<List<KvnServerConfig>> FetchSubscriptionAsync(string url)
        {
            OnLog?.Invoke("[КВН] Загрузка подписки...");
            string raw = await GetRawSubscriptionAsync(url);
            return ParseSubscription(raw);
        }

        public List<KvnServerConfig> ParseSubscription(string raw)
        {
            var configs = new List<KvnServerConfig>();
            if (string.IsNullOrWhiteSpace(raw))
            {
                OnLog?.Invoke("[КВН] Подписка пустая.");
                return configs;
            }

            string trimmed = raw.Trim();

            // Если пришла HTML-страница вместо подписки
            if (trimmed.StartsWith("<") || trimmed.Contains("<!DOCTYPE", StringComparison.OrdinalIgnoreCase) || trimmed.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                OnLog?.Invoke("[КВН] ❌ Получена HTML-страница, а не подписка.");
                OnLog?.Invoke("[КВН] Откройте страницу подписки в браузере, нажмите кнопку 'Get link' / 'Получить ссылку' и вставьте именно ту ссылку.");
                return configs;
            }

            string decoded;
            try
            {
                decoded = DecodeBase64(trimmed.Replace("\n", "").Replace("\r", ""));
            }
            catch
            {
                decoded = raw;
            }

            // Если исходный текст сам содержит ссылки (plain text подписка), используем его
            if (trimmed.Contains("vless://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("vmess://", StringComparison.OrdinalIgnoreCase) ||
                trimmed.Contains("trojan://", StringComparison.OrdinalIgnoreCase))
            {
                decoded = trimmed;
            }

            var lines = decoded.Split(new[] { '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(l => l.Trim())
                               .Where(l => !string.IsNullOrWhiteSpace(l))
                               .ToArray();

            foreach (var line in lines)
            {
                var cfg = ParseLink(line);
                if (cfg != null)
                    configs.Add(cfg);
            }

            if (configs.Count == 0)
            {
                OnLog?.Invoke("[КВН] ⚠️ Не удалось распарсить подписку.");
                OnLog?.Invoke($"[КВН] Первые 200 символов ответа: {(trimmed.Length > 200 ? trimmed.Substring(0, 200) + "..." : trimmed)}");
                OnLog?.Invoke("[КВН] Если это Clash/YAML конфиг — пока не поддерживается. Скопируйте отдельный VLESS/Vmess/Trojan конфиг.");
            }

            OnLog?.Invoke($"[КВН] Найдено конфигураций: {configs.Count}");
            return configs;
        }

        private KvnServerConfig? ParseLink(string link)
        {
            try
            {
                if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                    return ParseVless(link);
                if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                    return ParseVmess(link);
                if (link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                    return ParseTrojan(link);
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[КВН] Ошибка парсинга ссылки: {ex.Message}");
            }
            return null;
        }

        private KvnServerConfig ParseVless(string link)
        {
            var uri = new Uri(link);
            var query = ParseQueryString(uri.Query);

            return new KvnServerConfig
            {
                Protocol = "VLESS",
                RawLink = link,
                Id = uri.UserInfo,
                Address = uri.Host,
                Port = uri.Port,
                Remark = WebUtility.UrlDecode(uri.Fragment.TrimStart('#')),
                Security = GetValue(query, "security") ?? GetValue(query, "tls") ?? "none",
                Network = GetValue(query, "type") ?? "tcp",
                Path = GetValue(query, "path") ?? "/",
                Host = GetValue(query, "host") ?? uri.Host,
                Sni = GetValue(query, "sni") ?? GetValue(query, "host") ?? uri.Host,
                Fingerprint = GetValue(query, "fp") ?? "",
                PublicKey = GetValue(query, "pbk") ?? "",
                ShortId = GetValue(query, "sid") ?? "",
                SpiderX = GetValue(query, "spx") ?? ""
            };
        }

        private KvnServerConfig ParseVmess(string link)
        {
            string b64 = link.Substring("vmess://".Length);
            string json = DecodeBase64(b64);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            int port = 443;
            if (int.TryParse(GetJsonString(root, "port"), out int p))
                port = p;

            return new KvnServerConfig
            {
                Protocol = "Vmess",
                RawLink = link,
                Id = GetJsonString(root, "id"),
                Address = GetJsonString(root, "add"),
                Port = port,
                Remark = GetJsonString(root, "ps"),
                Security = GetJsonString(root, "tls") == "tls" ? "tls" : GetJsonString(root, "scy"),
                Network = GetJsonString(root, "net"),
                Path = GetJsonString(root, "path"),
                Host = GetJsonString(root, "host"),
                Sni = GetJsonString(root, "sni")
            };
        }

        private KvnServerConfig ParseTrojan(string link)
        {
            var uri = new Uri(link);
            var query = ParseQueryString(uri.Query);

            return new KvnServerConfig
            {
                Protocol = "Trojan",
                RawLink = link,
                Password = uri.UserInfo,
                Address = uri.Host,
                Port = uri.Port,
                Remark = WebUtility.UrlDecode(uri.Fragment.TrimStart('#')),
                Security = "tls",
                Network = GetValue(query, "type") ?? "tcp",
                Path = GetValue(query, "path") ?? "/",
                Host = GetValue(query, "host") ?? uri.Host,
                Sni = GetValue(query, "sni") ?? GetValue(query, "host") ?? uri.Host
            };
        }

        private static Dictionary<string, string> ParseQueryString(string query)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(query)) return result;

            string trimmed = query.TrimStart('?');
            foreach (var part in trimmed.Split('&'))
            {
                int eq = part.IndexOf('=');
                if (eq < 0)
                    result[WebUtility.UrlDecode(part)] = "";
                else
                    result[WebUtility.UrlDecode(part.Substring(0, eq))] = WebUtility.UrlDecode(part.Substring(eq + 1));
            }
            return result;
        }

        private static string? GetValue(Dictionary<string, string> dict, string key)
        {
            return dict.TryGetValue(key, out var value) ? value : null;
        }

        private static string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out var prop))
            {
                if (prop.ValueKind == JsonValueKind.String)
                    return prop.GetString() ?? "";
                if (prop.ValueKind == JsonValueKind.Number)
                    return prop.GetRawText();
            }
            return "";
        }

        private static string DecodeBase64(string input)
        {
            string padded = input.PadRight(input.Length + (4 - input.Length % 4) % 4, '=');
            byte[] bytes = Convert.FromBase64String(padded);
            return Encoding.UTF8.GetString(bytes);
        }

        public string SaveSubscriptionToFile(string rawSubscription)
        {
            try
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "kvn_subscription.txt");
                File.WriteAllText(path, rawSubscription);
                return path;
            }
            catch (Exception ex)
            {
                OnLog?.Invoke($"[КВН] Ошибка сохранения: {ex.Message}");
                return string.Empty;
            }
        }
    }
}