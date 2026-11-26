using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Errors;

namespace SentimentAI.Core.Providers;

/// <summary>
/// Google Gemini API (generateContent). Ücretsiz katman Google AI Studio anahtarıyla, kart gerekmeden kullanılabilir.
/// Model adları için Google'ın "-latest" takma adları kullanılır; böylece model sürümü emekliye ayrıldığında
/// uygulama güncellenmeden en güncel Flash modeline geçilir.
/// </summary>
public sealed class GeminiProvider : HttpAIProviderBase
{
    public const string ProviderId = "gemini";
    public const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/";

    private static readonly string[] ModelChain = ["gemini-flash-latest", "gemini-flash-lite-latest"];

    public GeminiProvider(IHttpClientFactory httpFactory, IApiKeyStore keys, ILogger<GeminiProvider>? logger = null, TimeSpan? timeout = null)
        : base(httpFactory, keys, logger, timeout)
    {
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Google Gemini";
    public override string KeyHelpUrl => "https://aistudio.google.com/app/apikey";

    public override string PrivacyNote =>
        "Metin, Google Gemini API'ye (generativelanguage.googleapis.com) gönderilir. " +
        "Google'ın ücretsiz katman koşullarına göre bu katmanda gönderilen içerik Google ürünlerini geliştirmek için kullanılabilir. " +
        "Hassas bilgi içeren metinleri analiz etmeyin.";

    protected override IReadOnlyList<string> Models => ModelChain;

    protected override async Task<string> RequestAnalysisAsync(string apiKey, string model, string anonymizedText, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["systemInstruction"] = new JsonObject
            {
                ["parts"] = new JsonArray(new JsonObject { ["text"] = PromptBuilder.SystemInstruction }),
            },
            ["contents"] = new JsonArray(new JsonObject
            {
                ["role"] = "user",
                ["parts"] = new JsonArray(new JsonObject { ["text"] = PromptBuilder.BuildUserMessage(anonymizedText) }),
            }),
            ["generationConfig"] = new JsonObject
            {
                ["temperature"] = 0.2,
                ["maxOutputTokens"] = 8192,
                ["responseMimeType"] = "application/json",
            },
        };

        using var request = JsonPost($"{BaseUrl}models/{model}:generateContent", body);
        request.Headers.Add("x-goog-api-key", apiKey);

        var responseBody = await SendForBodyAsync(request, ct).ConfigureAwait(false);
        return ExtractText(responseBody, Id);
    }

    protected override HttpRequestMessage BuildStatusRequest(string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}models?pageSize=1");
        request.Headers.Add("x-goog-api-key", apiKey);
        return request;
    }

    /// <summary>generateContent yanıtından modelin metin çıktısını alır.</summary>
    internal static string ExtractText(string responseBody, string providerId)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;

            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array || candidates.GetArrayLength() == 0)
            {
                if (root.TryGetProperty("promptFeedback", out var fb) && fb.TryGetProperty("blockReason", out _))
                    throw new AnalysisException(ErrorKind.UnsupportedText,
                        "Metin, sağlayıcının içerik güvenliği filtresine takıldı ve analiz edilmedi.", providerId);
                throw new AnalysisException(ErrorKind.InvalidResponse, "Sağlayıcı boş yanıt döndürdü.", providerId);
            }

            var first = candidates[0];
            var finish = first.TryGetProperty("finishReason", out var fr) ? fr.GetString() : null;
            if (finish is "SAFETY" or "PROHIBITED_CONTENT" or "BLOCKLIST")
                throw new AnalysisException(ErrorKind.UnsupportedText,
                    "Metin, sağlayıcının içerik güvenliği filtresine takıldı ve analiz edilmedi.", providerId);

            var sb = new StringBuilder();
            if (first.TryGetProperty("content", out var content) &&
                content.TryGetProperty("parts", out var parts) && parts.ValueKind == JsonValueKind.Array)
            {
                foreach (var part in parts.EnumerateArray())
                {
                    if (part.TryGetProperty("thought", out var th) && th.ValueKind == JsonValueKind.True) continue;
                    if (part.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String)
                        sb.Append(t.GetString());
                }
            }

            if (sb.Length == 0)
                throw new AnalysisException(ErrorKind.InvalidResponse,
                    finish == "MAX_TOKENS" ? "Yanıt uzunluk sınırına takıldı." : "Sağlayıcı metin içermeyen bir yanıt döndürdü.",
                    providerId);

            return sb.ToString();
        }
        catch (JsonException ex)
        {
            throw new AnalysisException(ErrorKind.InvalidResponse, "Sağlayıcı yanıtı çözümlenemedi.", providerId, inner: ex);
        }
    }
}
