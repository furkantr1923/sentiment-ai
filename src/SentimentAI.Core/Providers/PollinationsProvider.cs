using System.Net.Http.Headers;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Errors;

namespace SentimentAI.Core.Providers;

/// <summary>
/// Pollinations.ai — açık kaynaklı, ücretsiz ve <b>anahtarsız</b> (anonim) metin API'si (OpenAI uyumlu).
/// Uygulama ilk kurulumda hiçbir ayar yapılmadan bu sağlayıcıyla çalışır. Anonim katman kısa aralıklarla
/// sınırlıdır (yaklaşık 15 saniyede 1 istek); kullanıcı isterse Ayarlar'dan kendi Gemini/Groq anahtarını ekler.
/// Uygulamaya gömülü bir API anahtarı yoktur; çalınabilecek bir gizli bilgi bulunmaz.
/// </summary>
public sealed class PollinationsProvider : HttpAIProviderBase
{
    public const string ProviderId = "pollinations";

    /// <summary>Denenecek uç noktalar (eski ve yeni ağ geçidi). İlki kapanırsa sıradakine geçilir.</summary>
    private static readonly string[] Endpoints =
    [
        "https://text.pollinations.ai/openai",
        "https://gen.pollinations.ai/v1/chat/completions",
    ];

    private static readonly string[] ModelChain = ["openai"];

    public PollinationsProvider(IHttpClientFactory httpFactory, IApiKeyStore keys, ILogger<PollinationsProvider>? logger = null, TimeSpan? timeout = null)
        : base(httpFactory, keys, logger, timeout)
    {
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Pollinations (ücretsiz)";
    public override string KeyHelpUrl => "https://pollinations.ai";
    public override bool RequiresApiKey => false;

    public override string PrivacyNote =>
        "Anahtar veya hesap gerekmez. Metin anonim olarak Pollinations.ai hizmetine (text.pollinations.ai) gönderilir; " +
        "Pollinations isteği kendi seçtiği bir dil modeline iletir. Ücretsiz anonim kullanım kısa aralıklarla sınırlıdır.";

    protected override IReadOnlyList<string> Models => ModelChain;

    protected override async Task<string> RequestAnalysisAsync(string apiKey, string model, string anonymizedText, CancellationToken ct)
    {
        AnalysisException? last = null;

        foreach (var endpoint in Endpoints)
        {
            var body = new JsonObject
            {
                ["model"] = model,
                ["temperature"] = 0.2,
                ["private"] = true, // herkese açık akışta gösterilmesin
                ["response_format"] = new JsonObject { ["type"] = "json_object" },
                ["messages"] = new JsonArray(
                    new JsonObject { ["role"] = "system", ["content"] = PromptBuilder.SystemInstruction },
                    new JsonObject { ["role"] = "user", ["content"] = PromptBuilder.BuildUserMessage(anonymizedText) }),
            };

            using var request = JsonPost(endpoint, body);
            if (!string.IsNullOrWhiteSpace(apiKey))
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

            try
            {
                var responseBody = await SendForBodyAsync(request, ct).ConfigureAwait(false);
                return GroqProvider.ExtractText(responseBody, Id); // aynı OpenAI yanıt biçimi
            }
            catch (AnalysisException ex) when (IsEndpointGone(ex))
            {
                // Bu uç nokta kapalı veya anonim erişime kapatılmış: sıradaki ağ geçidini dene.
                last = ex;
            }
            catch (AnalysisException ex) when (ex.Kind == ErrorKind.QuotaExceeded)
            {
                throw new AnalysisException(ErrorKind.QuotaExceeded,
                    "Ücretsiz anahtarsız servis kısa süreli istek sınırına ulaştı (yaklaşık 15 saniyede 1 analiz). Biraz bekleyip tekrar dene ya da Ayarlar'dan kendi ücretsiz anahtarını ekle.",
                    Id, statusCode: ex.StatusCode);
            }
        }

        throw last is null
            ? new AnalysisException(ErrorKind.ProviderUnavailable, null, Id)
            : new AnalysisException(ErrorKind.ProviderUnavailable,
                "Ücretsiz anahtarsız servis şu anda anonim istek kabul etmiyor. Ayarlar'dan ücretsiz bir Gemini veya Groq anahtarı ekleyebilirsin.",
                Id, statusCode: last.StatusCode);
    }

    protected override HttpRequestMessage BuildStatusRequest(string apiKey) =>
        new(HttpMethod.Get, "https://text.pollinations.ai/models");

    private static bool IsEndpointGone(AnalysisException ex) =>
        ex.StatusCode is 401 or 402 or 403 or 404 or 405 or 410 || ex.IsModelNotFound;
}
