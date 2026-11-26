using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;

namespace SentimentAI.Core.Providers;

/// <summary>
/// HTTP tabanlı sağlayıcılar için ortak altyapı: zaman aşımı, hata sınıflandırma,
/// model yedekleme ve gizlilik dostu loglama (istek/yanıt gövdesi ASLA loglanmaz).
/// </summary>
public abstract class HttpAIProviderBase : IAIProvider
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IApiKeyStore _keys;
    private readonly TimeSpan _timeout;
    private string? _activeModel;

    protected HttpAIProviderBase(IHttpClientFactory httpFactory, IApiKeyStore keys, ILogger? logger, TimeSpan? timeout)
    {
        _httpFactory = httpFactory;
        _keys = keys;
        Logger = logger ?? NullLogger.Instance;
        _timeout = timeout ?? AppLimits.RequestTimeout;
    }

    protected ILogger Logger { get; }

    public abstract string Id { get; }
    public abstract string DisplayName { get; }
    public abstract string PrivacyNote { get; }
    public abstract string KeyHelpUrl { get; }

    /// <summary>Denenecek modeller, tercih sırasıyla. İlki bulunamazsa sıradakine geçilir.</summary>
    protected abstract IReadOnlyList<string> Models { get; }

    /// <summary>HttpClientFactory'deki adlandırılmış istemci.</summary>
    public string HttpClientName => "provider-" + Id;

    public string ModelName => _activeModel ?? Models[0];

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_keys.Get(Id));

    public async Task<AnalysisResult> AnalyzeAsync(string anonymizedText, CancellationToken cancellationToken)
    {
        var key = _keys.Get(Id);
        if (string.IsNullOrWhiteSpace(key))
            throw new AnalysisException(ErrorKind.MissingApiKey, null, Id);

        AnalysisException? last = null;
        var ordered = _activeModel is null ? Models : Models.OrderBy(m => m == _activeModel ? 0 : 1).ToList();

        foreach (var model in ordered)
        {
            try
            {
                var content = await RequestAnalysisAsync(key!, model, anonymizedText, cancellationToken).ConfigureAwait(false);
                var result = AnalysisParser.Parse(content, DisplayName, model);
                _activeModel = model;
                return result;
            }
            catch (AnalysisException ex) when (ex.IsModelNotFound)
            {
                Logger.LogWarning("{Provider}: model {Model} kullanılamıyor, yedek modele geçiliyor.", Id, model);
                last = ex;
            }
        }

        throw last ?? new AnalysisException(ErrorKind.ProviderUnavailable, null, Id);
    }

    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken) =>
        (await GetProviderStatusAsync(cancellationToken).ConfigureAwait(false)).IsReady;

    public async Task<ProviderStatus> GetProviderStatusAsync(CancellationToken cancellationToken)
    {
        var key = _keys.Get(Id);
        if (string.IsNullOrWhiteSpace(key))
            return Status(ProviderState.NotConfigured, "API anahtarı girilmemiş.");

        try
        {
            using var request = BuildStatusRequest(key!);
            using var response = await SendAsync(request, AppLimits.StatusTimeout, cancellationToken).ConfigureAwait(false);
            if (ProviderErrorClassifier.IsSuccess(response.StatusCode))
                return Status(ProviderState.Ready, "Anahtar geçerli, servis erişilebilir.");

            var body = await SafeReadAsync(response, cancellationToken).ConfigureAwait(false);
            var error = ProviderErrorClassifier.FromStatus((int)response.StatusCode, body, Id);
            return Status(ProviderErrorClassifier.ToState(error.Kind), ErrorMessages.For(error).Title);
        }
        catch (AnalysisException ex)
        {
            return Status(ProviderErrorClassifier.ToState(ex.Kind), ErrorMessages.For(ex).Title);
        }
    }

    // ------------------------------------------------------------------ alt sınıflar için

    /// <summary>Analiz isteğini gönderir ve modelin ürettiği ham metni döndürür.</summary>
    protected abstract Task<string> RequestAnalysisAsync(string apiKey, string model, string anonymizedText, CancellationToken ct);

    protected abstract HttpRequestMessage BuildStatusRequest(string apiKey);

    protected static HttpRequestMessage JsonPost(string uri, JsonNode body)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json"),
        };
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return request;
    }

    /// <summary>İsteği gönderir; başarısız durum kodlarını sınıflandırılmış hataya çevirir ve gövdeyi döndürür.</summary>
    protected async Task<string> SendForBodyAsync(HttpRequestMessage request, CancellationToken ct)
    {
        using var response = await SendAsync(request, _timeout, ct).ConfigureAwait(false);
        var body = await SafeReadAsync(response, ct).ConfigureAwait(false);

        if (!ProviderErrorClassifier.IsSuccess(response.StatusCode))
        {
            var error = ProviderErrorClassifier.FromStatus((int)response.StatusCode, body, Id);
            // Yalnızca teknik bilgi loglanır; metin veya yanıt gövdesi loglanmaz.
            Logger.LogWarning("{Provider} HTTP {Status} → {Kind}", Id, (int)response.StatusCode, error.Kind);
            throw error;
        }

        return body;
    }

    private async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        var client = _httpFactory.CreateClient(HttpClientName);

        try
        {
            return await client.SendAsync(request, HttpCompletionOption.ResponseContentRead, timeoutCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw; // kullanıcı iptal etti
        }
        catch (Exception ex)
        {
            var mapped = ProviderErrorClassifier.FromException(ex, Id);
            Logger.LogWarning("{Provider} ağ hatası: {Type} → {Kind}", Id, ex.GetType().Name, mapped.Kind);
            throw mapped;
        }
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken ct)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return "";
        }
    }

    private ProviderStatus Status(ProviderState state, string message) =>
        new(Id, DisplayName, state, message, DateTimeOffset.Now);
}
