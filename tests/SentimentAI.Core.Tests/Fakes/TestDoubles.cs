using System.Net;
using System.Text;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;
using SentimentAI.Core.Providers;

namespace SentimentAI.Core.Tests.Fakes;

public sealed class InMemoryKeyStore : IApiKeyStore
{
    private readonly Dictionary<string, string> _keys = new();

    public InMemoryKeyStore(params (string Id, string Key)[] keys)
    {
        foreach (var (id, key) in keys) _keys[id] = key;
    }

    public string? Get(string providerId) => _keys.TryGetValue(providerId, out var k) ? k : null;
    public Task SetAsync(string providerId, string apiKey) { _keys[providerId] = apiKey; return Task.CompletedTask; }
    public Task RemoveAsync(string providerId) { _keys.Remove(providerId); return Task.CompletedTask; }
    public Task LoadAsync() => Task.CompletedTask;
}

public sealed class FakeNetwork(bool online = true) : INetworkStatus
{
    public bool HasInternet { get; set; } = online;
}

/// <summary>Önceden tanımlanmış yanıtları sırayla döndüren HTTP işleyici (sahte sunucu).</summary>
public sealed class StubHandler : HttpMessageHandler
{
    private readonly Queue<Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>> _responses = new();

    public List<HttpRequestMessage> Requests { get; } = [];
    public List<string> Bodies { get; } = [];

    public StubHandler Respond(HttpStatusCode code, string body)
    {
        _responses.Enqueue((_, _) => Task.FromResult(new HttpResponseMessage(code)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        }));
        return this;
    }

    public StubHandler Throw(Exception ex)
    {
        _responses.Enqueue((_, _) => Task.FromException<HttpResponseMessage>(ex));
        return this;
    }

    /// <summary>Hiç yanıt vermeyen sunucu (zaman aşımı testi).</summary>
    public StubHandler Hang()
    {
        _responses.Enqueue(async (_, ct) =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            throw new InvalidOperationException();
        });
        return this;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        Bodies.Add(request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken));
        if (_responses.Count == 0) throw new InvalidOperationException("Beklenmeyen ek istek.");
        return await _responses.Dequeue()(request, cancellationToken);
    }
}

public sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
{
    public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
}

/// <summary>Sağlayıcı geçiş mantığını test etmek için ağ kullanmayan sahte sağlayıcı.</summary>
public sealed class FakeProvider(string id, bool configured = true) : IAIProvider
{
    public Queue<Func<AnalysisResult>> Script { get; } = new();
    public int Calls { get; private set; }
    public List<string> ReceivedTexts { get; } = [];

    public string Id => id;
    public string DisplayName => id.ToUpperInvariant();
    public string ModelName => "fake-model";
    public string PrivacyNote => "";
    public string KeyHelpUrl => "";
    public bool IsConfigured => configured;

    public FakeProvider Fails(ErrorKind kind, bool transient = false)
    {
        Script.Enqueue(() => throw new AnalysisException(kind, null, id, transient));
        return this;
    }

    public FakeProvider Succeeds()
    {
        Script.Enqueue(() => Samples.Result(DisplayName));
        return this;
    }

    public Task<AnalysisResult> AnalyzeAsync(string anonymizedText, CancellationToken cancellationToken)
    {
        Calls++;
        ReceivedTexts.Add(anonymizedText);
        if (Script.Count == 0) throw new InvalidOperationException("Senaryo bitti.");
        return Task.FromResult(Script.Dequeue()());
    }

    public Task<bool> IsAvailableAsync(CancellationToken cancellationToken) => Task.FromResult(configured);

    public Task<ProviderStatus> GetProviderStatusAsync(CancellationToken cancellationToken) =>
        Task.FromResult(new ProviderStatus(id, DisplayName, ProviderState.Ready, "", DateTimeOffset.Now));
}

public static class Samples
{
    /// <summary>Modelin döndürebileceği tipik, geçerli bir analiz JSON'u.</summary>
    public const string ValidJson = """
        {
          "temel_duygu": "pozitif",
          "dagilim": { "pozitif": 78, "negatif": 7, "notr": 15 },
          "baskin_duygu": "memnuniyet",
          "guven": 86,
          "duygular": [
            { "ad": "Mutluluk", "puan": 64 },
            { "ad": "Memnuniyet", "puan": 82 },
            { "ad": "Öfke", "puan": 3 },
            { "ad": "Heyecan", "puan": 40 }
          ],
          "yogunluk": "orta",
          "yogunluk_puani": 58,
          "konu": "Ürün deneyimi",
          "amac": "Memnuniyet bildirmek",
          "hedef_kitle": "Diğer müşteriler",
          "ozet": "Kullanıcı ürünün hızlı geldiğini ve kaliteli olduğunu söylüyor.",
          "anahtar_kelimeler": [ { "kelime": "kargo", "agirlik": 0.9 }, { "kelime": "kalite", "agirlik": 0.7 } ],
          "ironi":   { "var": false, "puan": 5,  "aciklama": "" },
          "sarkazm": { "var": false, "puan": 3,  "aciklama": "" },
          "alay":    { "var": false, "puan": 2,  "aciklama": "" },
          "mizah":   { "var": true,  "puan": 61, "aciklama": "Hafif espri var." },
          "yazim_hatalari": [ { "hatali": "geldi.cok", "oneri": "geldi. Çok", "tur": "noktalama", "aciklama": "Nokta sonrası boşluk" } ],
          "duzeltilmis_metin": "Ürün çok hızlı geldi. Çok memnunum.",
          "gelistirme_onerileri": [ "Ürünün hangi özelliğini beğendiğini belirt." ],
          "tekrarlar": [ "çok" ]
        }
        """;

    public static AnalysisResult Result(string provider) =>
        Analysis.AnalysisParser.Parse(ValidJson, provider, "fake-model");

    public static string GeminiEnvelope(string modelText) =>
        "{\"candidates\":[{\"content\":{\"role\":\"model\",\"parts\":[{\"text\":" +
        System.Text.Json.JsonSerializer.Serialize(modelText) + "}]},\"finishReason\":\"STOP\"}]}";

    public static string GroqEnvelope(string modelText) =>
        "{\"choices\":[{\"index\":0,\"message\":{\"role\":\"assistant\",\"content\":" +
        System.Text.Json.JsonSerializer.Serialize(modelText) + "},\"finish_reason\":\"stop\"}]}";
}
