using System.Diagnostics;
using System.Text;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;
using SentimentAI.Core.Providers;

// SENTIMENT AI — canlı API entegrasyon testleri
// Kullanım: dotnet run --project tests/SentimentAI.LiveTests -- [rapor-dosyası]

var reportPath = args.Length > 0 ? args[0] : "live-api-report.txt";
var geminiKey = Environment.GetEnvironmentVariable("GEMINI_API_KEY");
var groqKey = Environment.GetEnvironmentVariable("GROQ_API_KEY");

var http = new SimpleHttpClientFactory();
var results = new List<(string Name, string Status, string Note)>();

const string sample = "Siparişim söz verilenden iki gün önce geldi, paketleme de çok özenliydi. " +
                      "Tek sorun kutunun biraz ezik olmasıydı ama ürün sapasağlam. Teşekkürler!";
const string ironic = "Harika, yine tam sınav sabahı internet kesildi. Bu şirketin zamanlamasına hayranım doğrusu.";

async Task Run(string name, Func<Task<string>> body)
{
    var sw = Stopwatch.StartNew();
    try
    {
        var note = await body();
        results.Add((name, "GEÇTİ", $"{note} ({sw.ElapsedMilliseconds} ms)"));
    }
    catch (SkipException s)
    {
        results.Add((name, "ATLANDI", s.Message));
    }
    catch (Exception ex)
    {
        var detail = ex is AnalysisException a ? $"{a.Kind} {a.StatusCode}" : ex.GetType().Name;
        results.Add((name, "BAŞARISIZ", detail));
    }
}

GeminiProvider Gemini(string? key, TimeSpan? timeout = null) => new(http, new EnvKeyStore(GeminiProvider.ProviderId, key), timeout: timeout);
GroqProvider Groq(string? key) => new(http, new EnvKeyStore(GroqProvider.ProviderId, key));

static string Describe(AnalysisResult r) =>
    $"{AnalysisResult.PolarityLabel(r.Polarity)} · {r.DominantEmotion} · güven %{r.Confidence} · {r.Emotions.Count} duygu · {r.Keywords.Count} anahtar kelime · model {r.ModelName}";

static void RequireKey(string? key, string env)
{
    if (string.IsNullOrWhiteSpace(key)) throw new SkipException($"{env} tanımlı değil; gerçek analiz testi çalıştırılmadı.");
}

static AnalysisException ExpectError(Func<Task> action)
{
    try { action().GetAwaiter().GetResult(); }
    catch (AnalysisException ex) { return ex; }
    throw new InvalidOperationException("Hata bekleniyordu ama istek başarılı oldu.");
}

// 1–2: Gerçek analiz
await Run("Gemini: gerçek Türkçe analiz", async () =>
{
    RequireKey(geminiKey, "GEMINI_API_KEY");
    var r = await Gemini(geminiKey).AnalyzeAsync(sample, CancellationToken.None);
    if (r.Emotions.Count < 5) throw new InvalidOperationException("Duygu listesi eksik.");
    return Describe(r);
});

await Run("Groq: gerçek Türkçe analiz", async () =>
{
    RequireKey(groqKey, "GROQ_API_KEY");
    var r = await Groq(groqKey).AnalyzeAsync(sample, CancellationToken.None);
    if (r.Emotions.Count < 5) throw new InvalidOperationException("Duygu listesi eksik.");
    return Describe(r);
});

await Run("İroni tespiti (gerçek API)", async () =>
{
    var (provider, key, env) = geminiKey is { Length: > 0 } ? ((IAIProvider)Gemini(geminiKey), geminiKey, "GEMINI_API_KEY")
                                                          : (Groq(groqKey), groqKey, "GROQ_API_KEY");
    RequireKey(key, env);
    var r = await provider.AnalyzeAsync(ironic, CancellationToken.None);
    return $"ironi {(r.Irony.Detected ? "var" : "yok")} (%{r.Irony.Score}), sarkazm %{r.Sarcasm.Score}, temel duygu {AnalysisResult.PolarityLabel(r.Polarity)}";
});

// Anahtarsız ücretsiz sağlayıcı: her derlemede gerçek analiz
await Run("Pollinations (anahtarsız): gerçek Türkçe analiz", async () =>
{
    var r = await new PollinationsProvider(http, new EnvKeyStore("yok", null)).AnalyzeAsync(sample, CancellationToken.None);
    if (r.Emotions.Count < 5) throw new InvalidOperationException("Duygu listesi eksik.");
    return Describe(r);
});

await Task.Delay(TimeSpan.FromSeconds(20)); // anonim katman: ~15 sn'de 1 istek

await Run("Pollinations (anahtarsız): ironi tespiti", async () =>
{
    // Uygulamadaki akışın aynısı: doğrulama + anonimleştirme + sınırlı yeniden deneme
    var service = new AnalysisService([new PollinationsProvider(http, new EnvKeyStore("yok", null))], new AlwaysOnline());
    var r = (await service.AnalyzeAsync(ironic, _ => Task.FromResult(false), CancellationToken.None)).Result;
    return $"ironi {(r.Irony.Detected ? "var" : "yok")} (%{r.Irony.Score}), sarkazm %{r.Sarcasm.Score}, temel duygu {AnalysisResult.PolarityLabel(r.Polarity)}, yazım önerisi {r.SpellingIssues.Count}";
});

// 3–4: Geçersiz anahtar (anahtar gerektirmez, gerçek sunucuya gider)
await Run("Gemini: geçersiz API anahtarı", () =>
{
    var ex = ExpectError(() => Gemini("gecersiz-anahtar-sentimentai-test").AnalyzeAsync(sample, CancellationToken.None));
    if (ex.Kind != ErrorKind.InvalidApiKey) throw new InvalidOperationException($"Beklenen InvalidApiKey, gelen {ex.Kind} (HTTP {ex.StatusCode}).");
    return Task.FromResult($"sunucu yanıtı HTTP {ex.StatusCode} → {ex.Kind}");
});

await Run("Groq: geçersiz API anahtarı", () =>
{
    var ex = ExpectError(() => Groq("gsk_gecersiz_sentimentai_test").AnalyzeAsync(sample, CancellationToken.None));
    if (ex.Kind != ErrorKind.InvalidApiKey) throw new InvalidOperationException($"Beklenen InvalidApiKey, gelen {ex.Kind} (HTTP {ex.StatusCode}).");
    return Task.FromResult($"sunucu yanıtı HTTP {ex.StatusCode} → {ex.Kind}");
});

await Run("Durum kontrolü: geçersiz anahtar raporlanır", async () =>
{
    var s = await Gemini("gecersiz-anahtar-sentimentai-test").GetProviderStatusAsync(CancellationToken.None);
    if (s.State != ProviderState.InvalidKey) throw new InvalidOperationException($"Beklenen InvalidKey, gelen {s.State}.");
    return $"{s.DisplayName}: {s.State}";
});

// 5: Zaman aşımı (gerçek sunucuya, imkânsız kısa süreyle)
await Run("Zaman aşımı", () =>
{
    var ex = ExpectError(() => Gemini("gecersiz-anahtar", TimeSpan.FromMilliseconds(1)).AnalyzeAsync(sample, CancellationToken.None));
    if (ex.Kind != ErrorKind.Timeout) throw new InvalidOperationException($"Beklenen Timeout, gelen {ex.Kind}.");
    return Task.FromResult("1 ms sınırında istek iptal edildi → Timeout");
});

// 6: Yedek sağlayıcı (Gemini anahtarı geçersiz → onayla Groq)
await Run("Yedek sağlayıcıya geçiş (gerçek API)", async () =>
{
    RequireKey(groqKey, "GROQ_API_KEY");
    var service = new AnalysisService(
        [Gemini("gecersiz-anahtar-sentimentai-test"), Groq(groqKey)],
        new AlwaysOnline());
    string? asked = null;
    var outcome = await service.AnalyzeAsync(sample, req => { asked = $"{req.From.DisplayName} → {req.To.DisplayName} ({req.Reason})"; return Task.FromResult(true); }, CancellationToken.None);
    if (!outcome.UsedFallback) throw new InvalidOperationException("Yedek sağlayıcı kullanılmadı.");
    return $"onay soruldu: {asked}; sonuç {outcome.Result.ProviderName}";
});

// ---------------------------------------------------------------- rapor
var sb = new StringBuilder();
sb.AppendLine("SENTIMENT AI — Canlı API Entegrasyon Testleri");
sb.AppendLine($"Tarih (UTC): {DateTime.UtcNow:yyyy-MM-dd HH:mm}");
sb.AppendLine($"GEMINI_API_KEY: {(string.IsNullOrWhiteSpace(geminiKey) ? "yok" : "tanımlı")} · GROQ_API_KEY: {(string.IsNullOrWhiteSpace(groqKey) ? "yok" : "tanımlı")}");
sb.AppendLine(new string('-', 72));
foreach (var (name, status, note) in results)
    sb.AppendLine($"[{status}] {name} — {note}");
sb.AppendLine(new string('-', 72));
int passed = results.Count(r => r.Status == "GEÇTİ");
int failed = results.Count(r => r.Status == "BAŞARISIZ");
int skipped = results.Count(r => r.Status == "ATLANDI");
sb.AppendLine($"Toplam {results.Count} · Geçti {passed} · Başarısız {failed} · Atlandı {skipped}");

var report = sb.ToString();
Console.Write(report);
File.WriteAllText(reportPath, report, new UTF8Encoding(false));
return failed == 0 ? 0 : 1;

sealed class SkipException(string message) : Exception(message);

sealed class EnvKeyStore(string id, string? key) : IApiKeyStore
{
    public string? Get(string providerId) => providerId == id ? key : null;
    public Task SetAsync(string providerId, string apiKey) => Task.CompletedTask;
    public Task RemoveAsync(string providerId) => Task.CompletedTask;
    public Task LoadAsync() => Task.CompletedTask;
}

sealed class AlwaysOnline : INetworkStatus
{
    public bool HasInternet => true;
}

sealed class SimpleHttpClientFactory : IHttpClientFactory
{
    private readonly HttpClient _client = new();
    public HttpClient CreateClient(string name) => _client;
}
