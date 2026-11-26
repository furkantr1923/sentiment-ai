using SentimentAI.Core.Models;

namespace SentimentAI.Core.Providers;

/// <summary>Bir yapay zekâ sağlayıcısının ortak sözleşmesi (Provider Abstraction).</summary>
public interface IAIProvider
{
    /// <summary>Kalıcı kimlik (ör. "gemini"). Anahtar deposunda da bu kullanılır.</summary>
    string Id { get; }

    /// <summary>Kullanıcıya gösterilen ad.</summary>
    string DisplayName { get; }

    /// <summary>Şu an kullanılan (veya ilk denenecek) model.</summary>
    string ModelName { get; }

    /// <summary>Verinin nereye gönderildiğini ve sağlayıcının veri politikasını özetler.</summary>
    string PrivacyNote { get; }

    /// <summary>Ücretsiz API anahtarının alınabileceği adres.</summary>
    string KeyHelpUrl { get; }

    /// <summary>Sağlayıcı kullanıma hazır mı? (anahtar gerekiyorsa girilmiş mi)</summary>
    bool IsConfigured { get; }

    /// <summary>Bu sağlayıcı API anahtarı istiyor mu? (anahtarsız ücretsiz servislerde false)</summary>
    bool RequiresApiKey => true;

    /// <summary>Metni analiz eder. Metin önceden doğrulanmış ve anonimleştirilmiş olmalıdır.</summary>
    Task<AnalysisResult> AnalyzeAsync(string anonymizedText, CancellationToken cancellationToken);

    /// <summary>Sağlayıcı şu an kullanılabilir mi? (anahtar geçerli ve servis erişilebilir)</summary>
    Task<bool> IsAvailableAsync(CancellationToken cancellationToken);

    /// <summary>Ayarlar ekranı için ayrıntılı durum.</summary>
    Task<ProviderStatus> GetProviderStatusAsync(CancellationToken cancellationToken);
}

public enum ProviderState
{
    NotConfigured,
    Ready,
    InvalidKey,
    QuotaExceeded,
    Unreachable,
    Unknown,
}

public sealed record ProviderStatus(
    string ProviderId,
    string DisplayName,
    ProviderState State,
    string Message,
    DateTimeOffset CheckedAt)
{
    public bool IsReady => State == ProviderState.Ready;
}

/// <summary>API anahtarlarının güvenli deposu (Android Keystore / Windows DPAPI üzerinden).</summary>
public interface IApiKeyStore
{
    /// <summary>Önbellekteki anahtarı döndürür (yoksa null).</summary>
    string? Get(string providerId);

    Task SetAsync(string providerId, string apiKey);

    Task RemoveAsync(string providerId);

    /// <summary>Anahtarları güvenli depodan belleğe yükler (uygulama açılışında bir kez).</summary>
    Task LoadAsync();
}

/// <summary>Cihazın internet bağlantı durumu.</summary>
public interface INetworkStatus
{
    bool HasInternet { get; }
}
