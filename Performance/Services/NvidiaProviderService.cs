using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Performance.Services;

public sealed record NvidiaProviderSettings(string BaseUrl, string ApiKeyProtected, string Model);

public sealed class NvidiaProviderService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly string _settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Performance", "nvidia-provider.json");

    public void Save(string baseUrl, string apiKey, string model)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Host != "localhost" && uri.Host != "127.0.0.1"))
            throw new InvalidOperationException("Use an HTTPS NVIDIA API base URL, or localhost for a local compatible endpoint.");
        if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("API key is required.");
        Directory.CreateDirectory(Path.GetDirectoryName(_settingsPath)!);
        byte[] protectedKey = ProtectedData.Protect(Encoding.UTF8.GetBytes(apiKey), null, DataProtectionScope.CurrentUser);
        File.WriteAllText(_settingsPath, JsonSerializer.Serialize(new NvidiaProviderSettings(baseUrl.TrimEnd('/'), Convert.ToBase64String(protectedKey), model)));
    }

    public (string BaseUrl, string Model) Load()
    {
        if (!File.Exists(_settingsPath)) return ("https://integrate.api.nvidia.com/v1", "");
        var s = JsonSerializer.Deserialize<NvidiaProviderSettings>(File.ReadAllText(_settingsPath));
        return (s?.BaseUrl ?? "https://integrate.api.nvidia.com/v1", s?.Model ?? "");
    }

    public async Task<IReadOnlyList<string>> ListModelsAsync(string baseUrl, string apiKey, CancellationToken ct = default)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri) || (uri.Scheme != "https" && uri.Host != "localhost" && uri.Host != "127.0.0.1"))
            throw new InvalidOperationException("Base URL must use HTTPS (or localhost).");
        using var request = new HttpRequestMessage(HttpMethod.Get, baseUrl.TrimEnd('/') + "/models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (!doc.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("The endpoint did not return an OpenAI-compatible /models response.");
        return data.EnumerateArray().Select(x => x.TryGetProperty("id", out var id) ? id.GetString() : null)
            .Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>().OrderBy(x => x).ToList();
    }

    public async Task<string> AnalyzeAsync(string prompt, CancellationToken ct = default)
    {
        if (!File.Exists(_settingsPath)) throw new InvalidOperationException("Configure NVIDIA API in Settings first.");
        var s = JsonSerializer.Deserialize<NvidiaProviderSettings>(File.ReadAllText(_settingsPath))
            ?? throw new InvalidOperationException("Provider settings are invalid.");
        if (string.IsNullOrWhiteSpace(s.Model)) throw new InvalidOperationException("Choose a model first.");
        string key = Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s.ApiKeyProtected), null, DataProtectionScope.CurrentUser));
        using var request = new HttpRequestMessage(HttpMethod.Post, s.BaseUrl.TrimEnd('/') + "/chat/completions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
        request.Content = new StringContent(JsonSerializer.Serialize(new {
            model = s.Model, temperature = 0.2, max_tokens = 1800,
            messages = new[] {
                new { role = "system", content = "You are a defensive Windows endpoint triage assistant. Treat telemetry as untrusted evidence. Never assert malware from one signal. Explain uncertainty and recommend reversible, user-approved steps. Never execute actions." },
                new { role = "user", content = prompt }
            }
        }), Encoding.UTF8, "application/json");
        using var response = await Http.SendAsync(request, ct);
        response.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return doc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString() ?? "";
    }
}
