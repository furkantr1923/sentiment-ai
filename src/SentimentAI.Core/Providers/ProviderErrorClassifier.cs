using System.Net;
using System.Net.Sockets;
using SentimentAI.Core.Errors;

namespace SentimentAI.Core.Providers;

/// <summary>HTTP durum kodlarını ve ağ istisnalarını uygulama hata kategorilerine çevirir.</summary>
public static class ProviderErrorClassifier
{
    public static AnalysisException FromStatus(int status, string? body, string providerId)
    {
        var b = body ?? "";

        switch (status)
        {
            case 400:
                if (Has(b, "API_KEY_INVALID") || Has(b, "API key not valid") || Has(b, "invalid_api_key"))
                    return new AnalysisException(ErrorKind.InvalidApiKey, null, providerId, statusCode: status);
                if (Has(b, "model_not_found") || Has(b, "model_decommissioned") || Has(b, "does not exist"))
                    return new AnalysisException(ErrorKind.ProviderUnavailable, "Model bu sağlayıcıda artık kullanılamıyor.", providerId, statusCode: status) { IsModelNotFound = true };
                if (Has(b, "location is not supported") || Has(b, "FAILED_PRECONDITION"))
                    return new AnalysisException(ErrorKind.ProviderUnavailable, "Sağlayıcı bu bölgede ücretsiz hizmet vermiyor.", providerId, statusCode: status);
                if (Has(b, "context_length") || Has(b, "too long") || Has(b, "maximum context"))
                    return new AnalysisException(ErrorKind.TextTooLong, null, providerId, statusCode: status);
                if (Has(b, "json_validate_failed"))
                    return new AnalysisException(ErrorKind.InvalidResponse, null, providerId, statusCode: status);
                return new AnalysisException(ErrorKind.AnalysisFailed, "Sağlayıcı isteği reddetti (HTTP 400).", providerId, statusCode: status);

            case 401:
            case 403:
                return new AnalysisException(ErrorKind.InvalidApiKey, null, providerId, statusCode: status);

            case 404:
                return new AnalysisException(ErrorKind.ProviderUnavailable, "İstenen model bulunamadı.", providerId, statusCode: status) { IsModelNotFound = true };

            case 408:
                return new AnalysisException(ErrorKind.Timeout, null, providerId, isTransient: true, statusCode: status);

            case 413:
                return new AnalysisException(ErrorKind.TextTooLong, null, providerId, statusCode: status);

            case 429:
                // Kota/oran sınırı: yeniden deneyerek sınırı zorlamayız.
                return new AnalysisException(ErrorKind.QuotaExceeded, null, providerId, statusCode: status);

            case 500:
            case 502:
            case 503:
            case 504:
                return new AnalysisException(ErrorKind.ProviderUnavailable, null, providerId, isTransient: true, statusCode: status);

            default:
                return new AnalysisException(ErrorKind.AnalysisFailed, $"Beklenmeyen yanıt (HTTP {status}).", providerId, statusCode: status);
        }
    }

    public static AnalysisException FromException(Exception ex, string providerId) => ex switch
    {
        AnalysisException a => a,
        TaskCanceledException or TimeoutException =>
            new AnalysisException(ErrorKind.Timeout, null, providerId, isTransient: true, inner: ex),
        HttpRequestException { StatusCode: { } code } =>
            FromStatus((int)code, null, providerId),
        HttpRequestException h when h.InnerException is SocketException { SocketErrorCode: SocketError.HostNotFound or SocketError.TryAgain } =>
            new AnalysisException(ErrorKind.ProviderUnavailable, "Sağlayıcının sunucu adı çözümlenemedi (DNS).", providerId, isTransient: true, inner: ex),
        HttpRequestException =>
            new AnalysisException(ErrorKind.ProviderUnavailable, "Sağlayıcı sunucusuna bağlanılamadı.", providerId, isTransient: true, inner: ex),
        IOException =>
            new AnalysisException(ErrorKind.ProviderUnavailable, "Bağlantı beklenmedik şekilde kesildi.", providerId, isTransient: true, inner: ex),
        _ => new AnalysisException(ErrorKind.AnalysisFailed, null, providerId, inner: ex),
    };

    public static ProviderState ToState(ErrorKind kind) => kind switch
    {
        ErrorKind.InvalidApiKey => ProviderState.InvalidKey,
        ErrorKind.QuotaExceeded => ProviderState.QuotaExceeded,
        ErrorKind.MissingApiKey => ProviderState.NotConfigured,
        ErrorKind.ProviderUnavailable or ErrorKind.Timeout or ErrorKind.NoInternet => ProviderState.Unreachable,
        _ => ProviderState.Unknown,
    };

    private static bool Has(string body, string token) =>
        body.Contains(token, StringComparison.OrdinalIgnoreCase);

    internal static bool IsSuccess(HttpStatusCode code) => (int)code is >= 200 and < 300;
}
