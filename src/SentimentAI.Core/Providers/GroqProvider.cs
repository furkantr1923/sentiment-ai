using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Errors;

namespace SentimentAI.Core.Providers;

/// <summary>
/// Groq (OpenAI uyumlu Chat Completions). Ücretsiz plan kredi kartı istemez; sınır aşılırsa 429 döner, ücret kesilmez.
/// </summary>
public sealed class GroqProvider : HttpAIProviderBase
{
    public const string ProviderId = "groq";
    public const string BaseUrl = "https://api.groq.com/openai/v1/";

    private static readonly string[] ModelChain = ["openai/gpt-oss-120b", "llama-3.3-70b-versatile"];

    public GroqProvider(IHttpClientFactory httpFactory, IApiKeyStore keys, ILogger<GroqProvider>? logger = null, TimeSpan? timeout = null)
        : base(httpFactory, keys, logger, timeout)
    {
    }

    public override string Id => ProviderId;
    public override string DisplayName => "Groq";
    public override string KeyHelpUrl => "https://console.groq.com/keys";

    public override string PrivacyNote =>
        "Metin, Groq API'ye (api.groq.com) gönderilir. Groq hizmet sözleşmesine göre müşteri verileri model eğitimi için kullanılmaz.";

    protected override IReadOnlyList<string> Models => ModelChain;

    protected override async Task<string> RequestAnalysisAsync(string apiKey, string model, string anonymizedText, CancellationToken ct)
    {
        var body = new JsonObject
        {
            ["model"] = model,
            ["temperature"] = 0.2,
            ["max_completion_tokens"] = 4096,
            ["response_format"] = new JsonObject { ["type"] = "json_object" },
            ["messages"] = new JsonArray(
                new JsonObject { ["role"] = "system", ["content"] = PromptBuilder.SystemInstruction },
                new JsonObject { ["role"] = "user", ["content"] = PromptBuilder.BuildUserMessage(anonymizedText) }),
        };

        if (model.StartsWith("openai/gpt-oss", StringComparison.Ordinal))
            body["reasoning_effort"] = "low";

        using var request = JsonPost($"{BaseUrl}chat/completions", body);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);

        var responseBody = await SendForBodyAsync(request, ct).ConfigureAwait(false);
        return ExtractText(responseBody, Id);
    }

    protected override HttpRequestMessage BuildStatusRequest(string apiKey)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}models");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        return request;
    }

    /// <summary>Chat Completions yanıtından asistan mesajını alır.</summary>
    internal static string ExtractText(string responseBody, string providerId)
    {
        try
        {
            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            if (root.TryGetProperty("choices", out var choices) &&
                choices.ValueKind == JsonValueKind.Array && choices.GetArrayLength() > 0 &&
                choices[0].TryGetProperty("message", out var message) &&
                message.TryGetProperty("content", out var content) &&
                content.ValueKind == JsonValueKind.String)
            {
                var text = content.GetString();
                if (!string.IsNullOrWhiteSpace(text)) return text!;
            }

            throw new AnalysisException(ErrorKind.InvalidResponse, "Sağlayıcı metin içermeyen bir yanıt döndürdü.", providerId);
        }
        catch (JsonException ex)
        {
            throw new AnalysisException(ErrorKind.InvalidResponse, "Sağlayıcı yanıtı çözümlenemedi.", providerId, inner: ex);
        }
    }
}
