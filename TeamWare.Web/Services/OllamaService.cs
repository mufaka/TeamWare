using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace TeamWare.Web.Services;

public class OllamaService : IOllamaService
{
    private const string DefaultModel = "llama3.1";
    private const int DefaultTimeoutSeconds = 60;
    internal const string CacheKeyPrefix = "OllamaConfig_";
    private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(30);

    private readonly IGlobalConfigurationService _configService;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IMemoryCache _cache;

    public OllamaService(
        IGlobalConfigurationService configService,
        IHttpClientFactory httpClientFactory,
        IMemoryCache cache)
    {
        _configService = configService;
        _httpClientFactory = httpClientFactory;
        _cache = cache;
    }

    public async Task<bool> IsConfigured()
    {
        var url = await GetCachedConfigValue("OLLAMA_URL");
        return !string.IsNullOrWhiteSpace(url);
    }

    public async Task<ServiceResult<string>> GenerateCompletion(string systemPrompt, string userPrompt)
    {
        var url = await GetCachedConfigValue("OLLAMA_URL");

        if (string.IsNullOrWhiteSpace(url))
        {
            return ServiceResult<string>.Failure("AI features are not configured.");
        }

        var model = await GetCachedConfigValue("OLLAMA_MODEL");

        if (string.IsNullOrWhiteSpace(model))
        {
            model = DefaultModel;
        }

        var timeoutSeconds = DefaultTimeoutSeconds;
        var timeoutValue = await GetCachedConfigValue("OLLAMA_TIMEOUT");

        if (!string.IsNullOrWhiteSpace(timeoutValue) && int.TryParse(timeoutValue, out var parsed) && parsed > 0)
        {
            timeoutSeconds = parsed;
        }

        var requestBody = new
        {
            model,
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            stream = false
        };

        var json = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var client = _httpClientFactory.CreateClient("Ollama");
        client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);

        HttpResponseMessage response;

        try
        {
            var endpoint = url.TrimEnd('/') + "/api/chat";
            response = await client.PostAsync(endpoint, content);
        }
        catch (TaskCanceledException)
        {
            return ServiceResult<string>.Failure("AI request timed out. Try again or write manually.");
        }
        catch (HttpRequestException)
        {
            return ServiceResult<string>.Failure("AI assistant unavailable.");
        }

        if (!response.IsSuccessStatusCode)
        {
            return ServiceResult<string>.Failure("AI assistant unavailable.");
        }

        string responseBody;

        try
        {
            responseBody = await response.Content.ReadAsStringAsync();
        }
        catch
        {
            return ServiceResult<string>.Failure("Could not generate a suggestion. Try again or write manually.");
        }

        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var messageContent = doc.RootElement
                .GetProperty("message")
                .GetProperty("content")
                .GetString();

            if (string.IsNullOrWhiteSpace(messageContent))
            {
                return ServiceResult<string>.Failure("Could not generate a suggestion. Try again or write manually.");
            }

            return ServiceResult<string>.Success(messageContent);
        }
        catch
        {
            return ServiceResult<string>.Failure("Could not generate a suggestion. Try again or write manually.");
        }
    }

    private async Task<string?> GetCachedConfigValue(string key)
    {
        var cacheKey = CacheKeyPrefix + key;

        if (_cache.TryGetValue(cacheKey, out string? cached))
        {
            return cached;
        }

        var result = await _configService.GetByKeyAsync(key);

        var value = result.Succeeded ? result.Data?.Value : null;

        _cache.Set(cacheKey, value, CacheDuration);

        return value;
    }
}
