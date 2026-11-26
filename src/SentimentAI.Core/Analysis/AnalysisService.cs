using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;
using SentimentAI.Core.Privacy;
using SentimentAI.Core.Providers;
using SentimentAI.Core.Text;

namespace SentimentAI.Core.Analysis;

/// <summary>
/// Analiz akışını yönetir: doğrulama → bağlantı kontrolü → anonimleştirme → ana sağlayıcı →
/// (gerekirse ve kullanıcı onaylarsa) yedek sağlayıcı.
/// Aynı istek hiçbir zaman aynı anda birden fazla sağlayıcıya gönderilmez.
/// </summary>
public sealed class AnalysisService
{
    private readonly IReadOnlyList<IAIProvider> _providers;
    private readonly INetworkStatus _network;
    private readonly ILogger _logger;
    private readonly TimeSpan _retryDelay;

    public AnalysisService(
        IEnumerable<IAIProvider> providers,
        INetworkStatus network,
        ILogger<AnalysisService>? logger = null,
        TimeSpan? retryDelay = null)
    {
        _providers = providers.ToList();
        _network = network;
        _logger = (ILogger?)logger ?? NullLogger.Instance;
        _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(900);
    }

    /// <summary>Tercih sırasıyla kayıtlı sağlayıcılar (ilk sıradaki ana sağlayıcıdır).</summary>
    public IReadOnlyList<IAIProvider> Providers => _providers;

    public bool HasConfiguredProvider => _providers.Any(p => p.IsConfigured);

    /// <param name="rawText">Kullanıcının girdiği metin (henüz doğrulanmamış).</param>
    /// <param name="confirmFallback">
    /// Ana sağlayıcı başarısız olunca, metnin başka bir hizmete gönderilmesi için kullanıcıdan onay ister.
    /// false dönerse yedek sağlayıcı kullanılmaz.
    /// </param>
    public async Task<AnalysisOutcome> AnalyzeAsync(
        string? rawText,
        Func<FallbackRequest, Task<bool>> confirmFallback,
        CancellationToken cancellationToken)
    {
        var text = TextValidator.Validate(rawText);

        if (!_network.HasInternet)
            throw new AnalysisException(ErrorKind.NoInternet);

        var configured = _providers.Where(p => p.IsConfigured).ToList();
        if (configured.Count == 0)
            throw new AnalysisException(ErrorKind.MissingApiKey);

        // Kişisel veri kalıpları cihazda maskelenir; sağlayıcıya yalnızca bu metin gider.
        var anonymized = Anonymizer.Anonymize(text);

        AnalysisException? last = null;
        for (int i = 0; i < configured.Count; i++)
        {
            var provider = configured[i];

            if (i > 0)
            {
                var previous = configured[i - 1];
                bool approved = await confirmFallback(new FallbackRequest(previous, provider, last!.Kind)).ConfigureAwait(false);
                if (!approved)
                {
                    _logger.LogInformation("Kullanıcı yedek sağlayıcıya geçişi onaylamadı.");
                    break;
                }
            }

            for (int attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    var result = await provider.AnalyzeAsync(anonymized.Text, cancellationToken).ConfigureAwait(false);
                    _logger.LogInformation("Analiz tamamlandı: {Provider} ({Model}).", provider.Id, result.ModelName);
                    return new AnalysisOutcome(result, anonymized, UsedFallback: i > 0);
                }
                catch (AnalysisException ex)
                {
                    last = ex;
                    _logger.LogWarning("{Provider} başarısız: {Kind} (deneme {Attempt}).", provider.Id, ex.Kind, attempt + 1);

                    // Bağlantı istek sırasında koptuysa bunu açıkça bildir.
                    if (ex.IsTransient && !_network.HasInternet)
                        throw new AnalysisException(ErrorKind.NoInternet, inner: ex);

                    if (ex.IsTransient && attempt < AppLimits.MaxTransientRetries)
                    {
                        await Task.Delay(_retryDelay * (attempt + 1), cancellationToken).ConfigureAwait(false);
                        continue;
                    }
                    break;
                }
            }

            if (!ShouldTryNextProvider(last!.Kind))
                break;
        }

        throw last ?? new AnalysisException(ErrorKind.AnalysisFailed);
    }

    /// <summary>Hangi hatalarda yedek sağlayıcı denenebilir? (içerik/uzunluk hataları sağlayıcı değişince düzelmez)</summary>
    public static bool ShouldTryNextProvider(ErrorKind kind) => kind is
        ErrorKind.QuotaExceeded or
        ErrorKind.InvalidApiKey or
        ErrorKind.ProviderUnavailable or
        ErrorKind.Timeout or
        ErrorKind.InvalidResponse or
        ErrorKind.AnalysisFailed;
}
