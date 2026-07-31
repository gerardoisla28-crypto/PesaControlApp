using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace PesaControl.Core.Services;

public class DahuaHttpClient : IDisposable
{
    private readonly HttpClient _httpClient;
    private string _host = "";
    private int _port = 80;
    private string _username = "";
    private string _password = "";
    private bool _disposed;

    public DahuaHttpClient()
    {
        var handler = new HttpClientHandler();
        _httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
    }

    public void Configure(string ipAddress, int port, string username, string password)
    {
        _host = ipAddress;
        _port = port;
        _username = username;
        _password = password;
    }

    public async Task<string> GetStringAsync(string cgiPath)
    {
        var url = $"http://{_host}:{_port}{cgiPath}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AuthenticateRequestAsync(request);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    public async Task<byte[]> GetBytesAsync(string cgiPath)
    {
        var url = $"http://{_host}:{_port}{cgiPath}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        await AuthenticateRequestAsync(request);
        var response = await _httpClient.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsByteArrayAsync();
    }

    public async Task<bool> TestConnectionAsync()
    {
        try
        {
            var url = $"http://{_host}:{_port}/cgi-bin/magicBox.cgi?action=getSystemInfo";
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            await AuthenticateRequestAsync(request);
            var response = await _httpClient.SendAsync(request);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task AuthenticateRequestAsync(HttpRequestMessage request)
    {
        var digestAuth = new DigestAuthHelper(_httpClient, _username, _password);
        await digestAuth.AuthenticateAsync(request);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _httpClient.Dispose();
    }

    private class DigestAuthHelper
    {
        private readonly HttpClient _client;
        private readonly string _username;
        private readonly string _password;
        private static readonly SemaphoreSlim _nonceLock = new(1, 1);
        private static readonly Dictionary<string, DigestNonceInfo> _nonceCache = new();

        public DigestAuthHelper(HttpClient client, string username, string password)
        {
            _client = client;
            _username = username;
            _password = password;
        }

        public async Task AuthenticateAsync(HttpRequestMessage request)
        {
            var hostPort = $"{request.RequestUri!.Host}:{request.RequestUri.Port}";
            var method = request.Method.Method;
            var uri = request.RequestUri.PathAndQuery;

            DigestNonceInfo? nonceInfo;
            lock (_nonceCache)
            {
                _nonceCache.TryGetValue(hostPort, out nonceInfo);
            }

            if (nonceInfo != null && DateTime.UtcNow < nonceInfo.Expires)
            {
                var authHeader = BuildDigestHeader(method, uri, nonceInfo);
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
                return;
            }

            var response = await _client.SendAsync(
                new HttpRequestMessage(HttpMethod.Get, request.RequestUri),
                HttpCompletionOption.ResponseContentRead
            );

            if (response.StatusCode == HttpStatusCode.Unauthorized)
            {
                var wwwAuth = response.Headers.WwwAuthenticate.FirstOrDefault()?.ToString();
                if (wwwAuth == null || !wwwAuth.StartsWith("Digest"))
                    return;

                var parsed = ParseDigestChallenge(wwwAuth);
                nonceInfo = new DigestNonceInfo
                {
                    Realm = parsed["realm"],
                    Nonce = parsed["nonce"],
                    Qop = parsed.GetValueOrDefault("qop", "auth"),
                    Opaque = parsed.GetValueOrDefault("opaque", ""),
                    Expires = DateTime.UtcNow.AddSeconds(
                        int.TryParse(parsed.GetValueOrDefault("stale", ""), out _) ? 60 : 300)
                };

                lock (_nonceCache)
                {
                    _nonceCache[hostPort] = nonceInfo;
                }

                var authHeader = BuildDigestHeader(method, uri, nonceInfo);
                request.Headers.TryAddWithoutValidation("Authorization", authHeader);
            }
        }

        private static Dictionary<string, string> ParseDigestChallenge(string challenge)
        {
            var result = new Dictionary<string, string>();
            var matches = Regex.Matches(challenge, @"(\w+)=(?:\""([^\""]+)\""|(\S+))");
            foreach (Match match in matches)
            {
                var value = match.Groups[2].Success ? match.Groups[2].Value : match.Groups[3].Value;
                result[match.Groups[1].Value] = value;
            }
            return result;
        }

        private string BuildDigestHeader(string method, string uri, DigestNonceInfo info)
        {
            var nc = $"{++_requestCounter:X8}";
            var cnonce = Guid.NewGuid().ToString("N")[..16];
            var ha1 = HashToHex($"{_username}:{info.Realm}:{_password}");
            var ha2 = HashToHex($"{method}:{uri}");
            var response = HashToHex($"{ha1}:{info.Nonce}:{nc}:{cnonce}:{info.Qop}:{ha2}");

            var sb = new StringBuilder("Digest ");
            sb.Append($"username=\"{_username}\", ");
            sb.Append($"realm=\"{info.Realm}\", ");
            sb.Append($"nonce=\"{info.Nonce}\", ");
            sb.Append($"uri=\"{uri}\", ");
            sb.Append($"qop={info.Qop}, ");
            sb.Append($"nc={nc}, ");
            sb.Append($"cnonce=\"{cnonce}\", ");
            sb.Append($"response=\"{response}\"");
            if (!string.IsNullOrEmpty(info.Opaque))
                sb.Append($", opaque=\"{info.Opaque}\"");

            return sb.ToString();
        }

        private static string HashToHex(string input)
        {
            var bytes = Encoding.UTF8.GetBytes(input);
            var hash = System.Security.Cryptography.MD5.HashData(bytes);
            return Convert.ToHexStringLower(hash);
        }

        private static int _requestCounter;

        private class DigestNonceInfo
        {
            public string Realm { get; set; } = "";
            public string Nonce { get; set; } = "";
            public string Qop { get; set; } = "auth";
            public string Opaque { get; set; } = "";
            public DateTime Expires { get; set; }
        }
    }
}
