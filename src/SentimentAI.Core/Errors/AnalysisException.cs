namespace SentimentAI.Core.Errors;

/// <summary>Kullanıcıya gösterilebilecek hata kategorileri.</summary>
public enum ErrorKind
{
    NoInternet,
    MissingApiKey,
    InvalidApiKey,
    QuotaExceeded,
    ProviderUnavailable,
    Timeout,
    UnsupportedText,
    TextTooLong,
    FileReadFailed,
    InvalidResponse,
    AnalysisFailed,
}

/// <summary>
/// Uygulamanın kendi hata türü. İçinde ASLA kullanıcı metni bulunmaz;
/// yalnızca kategori, sağlayıcı ve teknik durum kodu taşır.
/// </summary>
public sealed class AnalysisException : Exception
{
    public AnalysisException(
        ErrorKind kind,
        string? detail = null,
        string? providerId = null,
        bool isTransient = false,
        int? statusCode = null,
        Exception? inner = null)
        : base($"{kind}{(providerId is null ? "" : $" [{providerId}]")}{(statusCode is null ? "" : $" HTTP {statusCode}")}", inner)
    {
        Kind = kind;
        Detail = detail;
        ProviderId = providerId;
        IsTransient = isTransient;
        StatusCode = statusCode;
    }

    public ErrorKind Kind { get; }

    /// <summary>Kullanıcıya gösterilebilecek ek açıklama (Türkçe, kişisel veri içermez).</summary>
    public string? Detail { get; }

    public string? ProviderId { get; }

    /// <summary>Kısa süre sonra tekrar denenince düzelebilecek bir hata mı?</summary>
    public bool IsTransient { get; }

    public int? StatusCode { get; }

    /// <summary>Sağlayıcı bu modeli tanımıyor (404 vb.); aynı sağlayıcının yedek modeli denenebilir.</summary>
    public bool IsModelNotFound { get; init; }
}
