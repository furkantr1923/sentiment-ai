using Microsoft.Extensions.Logging;
using SentimentAI.Core.Providers;

namespace SentimentAI.App.Services;

/// <summary>
/// API anahtarlarını işletim sisteminin güvenli deposunda saklar:
/// Android'de Android Keystore, Windows'ta DPAPI ile şifrelenmiş kullanıcıya özel depo.
/// Anahtarlar kaynak kodda veya düz metin ayar dosyalarında tutulmaz.
/// </summary>
public sealed class SecureApiKeyStore(ILogger<SecureApiKeyStore> logger) : IApiKeyStore
{
    private const string Prefix = "sentimentai.apikey.";
    private readonly Dictionary<string, string> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);

    public static IReadOnlyList<string> KnownProviders { get; } = [GeminiProvider.ProviderId, GroqProvider.ProviderId];

    public string? Get(string providerId)
    {
        lock (_cache) return _cache.TryGetValue(providerId, out var key) ? key : null;
    }

    public async Task LoadAsync()
    {
        await _gate.WaitAsync();
        try
        {
            foreach (var id in KnownProviders)
            {
                try
                {
                    var value = await SecureStorage.Default.GetAsync(Prefix + id);
                    lock (_cache)
                    {
                        if (string.IsNullOrWhiteSpace(value)) _cache.Remove(id);
                        else _cache[id] = value;
                    }
                }
                catch (Exception ex)
                {
                    // Depo bozulmuşsa (ör. cihaz yedeğinden geri yükleme) anahtar yeniden girilmelidir.
                    logger.LogWarning("Güvenli depo okunamadı ({Provider}): {Type}", id, ex.GetType().Name);
                    SecureStorage.Default.Remove(Prefix + id);
                }
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SetAsync(string providerId, string apiKey)
    {
        var key = apiKey.Trim();
        await _gate.WaitAsync();
        try
        {
            await SecureStorage.Default.SetAsync(Prefix + providerId, key);
            lock (_cache) _cache[providerId] = key;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RemoveAsync(string providerId)
    {
        await _gate.WaitAsync();
        try
        {
            SecureStorage.Default.Remove(Prefix + providerId);
            lock (_cache) _cache.Remove(providerId);
        }
        finally
        {
            _gate.Release();
        }
    }
}
