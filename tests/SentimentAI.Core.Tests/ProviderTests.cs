using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;
using SentimentAI.Core.Providers;
using SentimentAI.Core.Tests.Fakes;

namespace SentimentAI.Core.Tests;

/// <summary>
/// Sağlayıcı istemcilerinin sahte bir HTTP sunucusuna karşı entegrasyon testleri:
/// gerçek istek gövdesi üretilir, gerçek yanıt zarfları ayrıştırılır.
/// </summary>
public class ProviderHttpTests
{
    private static GeminiProvider Gemini(StubHandler h, string? key = "test-key", TimeSpan? timeout = null) =>
        new(new StubHttpClientFactory(h), key is null ? new InMemoryKeyStore() : new InMemoryKeyStore(("gemini", key)), timeout: timeout);

    private static GroqProvider Groq(StubHandler h, string? key = "test-key") =>
        new(new StubHttpClientFactory(h), key is null ? new InMemoryKeyStore() : new InMemoryKeyStore(("groq", key)));

    [Fact]
    public async Task Gemini_basarili_yanit_ayristirilir_ve_istek_dogru_kurulur()
    {
        var h = new StubHandler().Respond(HttpStatusCode.OK, Samples.GeminiEnvelope(Samples.ValidJson));
        var p = Gemini(h);

        var r = await p.AnalyzeAsync("Ürün çok hızlı geldi.", CancellationToken.None);

        Assert.Equal(Polarity.Positive, r.Polarity);
        Assert.Equal("Google Gemini", r.ProviderName);
        var req = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, req.Method);
        Assert.Contains("models/gemini-flash-latest:generateContent", req.RequestUri!.ToString());
        Assert.Equal("test-key", req.Headers.GetValues("x-goog-api-key").Single());
        Assert.DoesNotContain("test-key", req.RequestUri.ToString()); // anahtar URL'de taşınmaz

        using var body = JsonDocument.Parse(h.Bodies[0]);
        Assert.Equal("application/json", body.RootElement.GetProperty("generationConfig").GetProperty("responseMimeType").GetString());
        var userText = body.RootElement.GetProperty("contents")[0].GetProperty("parts")[0].GetProperty("text").GetString();
        Assert.Contains("Ürün çok hızlı geldi.", userText);
    }

    [Fact]
    public async Task Groq_basarili_yanit_ayristirilir_ve_JSON_modu_istenir()
    {
        var h = new StubHandler().Respond(HttpStatusCode.OK, Samples.GroqEnvelope(Samples.ValidJson));
        var r = await Groq(h).AnalyzeAsync("Harika bir gün!", CancellationToken.None);

        Assert.Equal("Groq", r.ProviderName);
        Assert.Equal("Bearer", h.Requests[0].Headers.Authorization!.Scheme);
        using var body = JsonDocument.Parse(h.Bodies[0]);
        Assert.Equal("json_object", body.RootElement.GetProperty("response_format").GetProperty("type").GetString());
        Assert.Equal("system", body.RootElement.GetProperty("messages")[0].GetProperty("role").GetString());
    }

    [Fact]
    public async Task Anahtar_yoksa_istek_gonderilmez()
    {
        var h = new StubHandler();
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Gemini(h, key: null).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.MissingApiKey, ex.Kind);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Gecersiz_anahtar_InvalidApiKey_olarak_siniflandirilir()
    {
        var h = new StubHandler().Respond(HttpStatusCode.BadRequest,
            """{"error":{"code":400,"message":"API key not valid. Please pass a valid API key.","status":"INVALID_ARGUMENT","details":[{"reason":"API_KEY_INVALID"}]}}""");
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Gemini(h).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.InvalidApiKey, ex.Kind);

        var g = new StubHandler().Respond(HttpStatusCode.Unauthorized, """{"error":{"message":"Invalid API Key","code":"invalid_api_key"}}""");
        var ex2 = await Assert.ThrowsAsync<AnalysisException>(() => Groq(g).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.InvalidApiKey, ex2.Kind);
    }

    [Fact]
    public async Task Kota_dolmasi_QuotaExceeded_ve_yeniden_denenmez()
    {
        var h = new StubHandler().Respond((HttpStatusCode)429, """{"error":{"code":429,"status":"RESOURCE_EXHAUSTED"}}""");
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Gemini(h).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.QuotaExceeded, ex.Kind);
        Assert.False(ex.IsTransient);
        Assert.Single(h.Requests);
    }

    [Fact]
    public async Task Model_bulunamazsa_ayni_saglayicinin_yedek_modeli_denenir()
    {
        var h = new StubHandler()
            .Respond(HttpStatusCode.NotFound, """{"error":{"code":404,"status":"NOT_FOUND"}}""")
            .Respond(HttpStatusCode.OK, Samples.GeminiEnvelope(Samples.ValidJson));
        var p = Gemini(h);

        var r = await p.AnalyzeAsync("metin", CancellationToken.None);

        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("gemini-flash-lite-latest", h.Requests[1].RequestUri!.ToString());
        Assert.Equal("gemini-flash-lite-latest", r.ModelName);
        Assert.Equal("gemini-flash-lite-latest", p.ModelName);
    }

    [Fact]
    public async Task Zaman_asimi_Timeout_olarak_siniflandirilir()
    {
        var h = new StubHandler().Hang();
        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Gemini(h, timeout: TimeSpan.FromMilliseconds(200)).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.Timeout, ex.Kind);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Kullanici_iptali_zaman_asimi_sayilmaz()
    {
        var h = new StubHandler().Hang();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Gemini(h).AnalyzeAsync("metin", cts.Token));
    }

    [Fact]
    public async Task Ag_kesilmesi_ProviderUnavailable_gecici_hata_olur()
    {
        var h = new StubHandler().Throw(new HttpRequestException("Bağlantı yok", new SocketException((int)SocketError.NetworkUnreachable)));
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Groq(h).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.ProviderUnavailable, ex.Kind);
        Assert.True(ex.IsTransient);
    }

    [Fact]
    public async Task Bozuk_JSON_yaniti_InvalidResponse_verir()
    {
        var h = new StubHandler().Respond(HttpStatusCode.OK, "{ bu json değil ");
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Gemini(h).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.InvalidResponse, ex.Kind);

        var h2 = new StubHandler().Respond(HttpStatusCode.OK, Samples.GroqEnvelope("Üzgünüm, yardımcı olamam."));
        var ex2 = await Assert.ThrowsAsync<AnalysisException>(() => Groq(h2).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.InvalidResponse, ex2.Kind);
    }

    [Fact]
    public async Task Guvenlik_filtresine_takilan_icerik_UnsupportedText_verir()
    {
        var h = new StubHandler().Respond(HttpStatusCode.OK, """{"promptFeedback":{"blockReason":"SAFETY"}}""");
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Gemini(h).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.UnsupportedText, ex.Kind);
    }

    [Fact]
    public async Task Durum_kontrolu_anahtar_gecerliligini_raporlar()
    {
        var ok = await Gemini(new StubHandler().Respond(HttpStatusCode.OK, "{\"models\":[]}")).GetProviderStatusAsync(CancellationToken.None);
        Assert.Equal(ProviderState.Ready, ok.State);

        var bad = await Groq(new StubHandler().Respond(HttpStatusCode.Unauthorized, "{}")).GetProviderStatusAsync(CancellationToken.None);
        Assert.Equal(ProviderState.InvalidKey, bad.State);

        var none = await Groq(new StubHandler(), key: null).GetProviderStatusAsync(CancellationToken.None);
        Assert.Equal(ProviderState.NotConfigured, none.State);
    }

    [Theory]
    [InlineData(401, ErrorKind.InvalidApiKey, false)]
    [InlineData(403, ErrorKind.InvalidApiKey, false)]
    [InlineData(429, ErrorKind.QuotaExceeded, false)]
    [InlineData(500, ErrorKind.ProviderUnavailable, true)]
    [InlineData(503, ErrorKind.ProviderUnavailable, true)]
    [InlineData(504, ErrorKind.ProviderUnavailable, true)]
    [InlineData(408, ErrorKind.Timeout, true)]
    [InlineData(413, ErrorKind.TextTooLong, false)]
    [InlineData(418, ErrorKind.AnalysisFailed, false)]
    public void HTTP_kodlari_dogru_siniflandirilir(int status, ErrorKind expected, bool transient)
    {
        var ex = ProviderErrorClassifier.FromStatus(status, null, "x");
        Assert.Equal(expected, ex.Kind);
        Assert.Equal(transient, ex.IsTransient);
    }
}

/// <summary>Sağlayıcı geçiş (failover) mantığı.</summary>
public class AnalysisServiceTests
{
    private const string Text = "Kargo çok hızlı geldi, teşekkürler! Bilgi için ali@ornek.com";

    private static Task<bool> Approve(FallbackRequest _) => Task.FromResult(true);
    private static Task<bool> Deny(FallbackRequest _) => Task.FromResult(false);

    private static AnalysisService Service(FakeNetwork net, params IAIProvider[] providers) =>
        new(providers, net, retryDelay: TimeSpan.Zero);

    [Fact]
    public async Task Ana_saglayici_basariliysa_yedege_gidilmez()
    {
        var a = new FakeProvider("a").Succeeds();
        var b = new FakeProvider("b").Succeeds();

        var outcome = await Service(new FakeNetwork(), a, b).AnalyzeAsync(Text, Approve, CancellationToken.None);

        Assert.False(outcome.UsedFallback);
        Assert.Equal(1, a.Calls);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task Saglayiciya_anonimlestirilmis_metin_gider()
    {
        var a = new FakeProvider("a").Succeeds();
        var outcome = await Service(new FakeNetwork(), a).AnalyzeAsync(Text, Approve, CancellationToken.None);

        Assert.DoesNotContain("ali@ornek.com", a.ReceivedTexts[0]);
        Assert.Contains("[E-POSTA]", a.ReceivedTexts[0]);
        Assert.Equal(1, outcome.Anonymization.Total);
    }

    [Fact]
    public async Task Kota_dolunca_onayla_yedek_saglayiciya_gecilir()
    {
        var a = new FakeProvider("a").Fails(ErrorKind.QuotaExceeded);
        var b = new FakeProvider("b").Succeeds();
        FallbackRequest? asked = null;

        var outcome = await Service(new FakeNetwork(), a, b).AnalyzeAsync(Text, r => { asked = r; return Task.FromResult(true); }, CancellationToken.None);

        Assert.True(outcome.UsedFallback);
        Assert.Equal("B", outcome.Result.ProviderName);
        Assert.NotNull(asked);
        Assert.Equal(ErrorKind.QuotaExceeded, asked!.Reason);
        Assert.Equal("b", asked.To.Id);
    }

    [Fact]
    public async Task Kullanici_onaylamazsa_veri_yedek_saglayiciya_gonderilmez()
    {
        var a = new FakeProvider("a").Fails(ErrorKind.QuotaExceeded);
        var b = new FakeProvider("b").Succeeds();

        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Service(new FakeNetwork(), a, b).AnalyzeAsync(Text, Deny, CancellationToken.None));

        Assert.Equal(ErrorKind.QuotaExceeded, ex.Kind);
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task Gecici_hata_sinirli_sayida_yeniden_denenir()
    {
        var a = new FakeProvider("a").Fails(ErrorKind.ProviderUnavailable, transient: true).Succeeds();
        var outcome = await Service(new FakeNetwork(), a).AnalyzeAsync(Text, Approve, CancellationToken.None);
        Assert.Equal(2, a.Calls);
        Assert.False(outcome.UsedFallback);
    }

    [Fact]
    public async Task Sonsuz_deneme_yapilmaz()
    {
        var a = new FakeProvider("a")
            .Fails(ErrorKind.ProviderUnavailable, transient: true)
            .Fails(ErrorKind.ProviderUnavailable, transient: true)
            .Fails(ErrorKind.ProviderUnavailable, transient: true);

        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Service(new FakeNetwork(), a).AnalyzeAsync(Text, Approve, CancellationToken.None));

        Assert.Equal(ErrorKind.ProviderUnavailable, ex.Kind);
        Assert.Equal(2, a.Calls); // 1 deneme + 1 yeniden deneme
    }

    [Fact]
    public async Task Icerik_hatasinda_yedek_saglayici_denenmez()
    {
        var a = new FakeProvider("a").Fails(ErrorKind.UnsupportedText);
        var b = new FakeProvider("b").Succeeds();

        await Assert.ThrowsAsync<AnalysisException>(() =>
            Service(new FakeNetwork(), a, b).AnalyzeAsync(Text, Approve, CancellationToken.None));
        Assert.Equal(0, b.Calls);
    }

    [Fact]
    public async Task Internet_yoksa_hicbir_istek_yapilmaz()
    {
        var a = new FakeProvider("a").Succeeds();
        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Service(new FakeNetwork(online: false), a).AnalyzeAsync(Text, Approve, CancellationToken.None));
        Assert.Equal(ErrorKind.NoInternet, ex.Kind);
        Assert.Equal(0, a.Calls);
    }

    [Fact]
    public async Task Istek_sirasinda_baglanti_koparsa_NoInternet_bildirilir()
    {
        var net = new FakeNetwork();
        var a = new FakeProvider("a");
        a.Script.Enqueue(() => { net.HasInternet = false; throw new AnalysisException(ErrorKind.ProviderUnavailable, isTransient: true); });

        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Service(net, a).AnalyzeAsync(Text, Approve, CancellationToken.None));
        Assert.Equal(ErrorKind.NoInternet, ex.Kind);
    }

    [Fact]
    public async Task Hicbir_anahtar_yoksa_MissingApiKey()
    {
        var a = new FakeProvider("a", configured: false);
        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Service(new FakeNetwork(), a).AnalyzeAsync(Text, Approve, CancellationToken.None));
        Assert.Equal(ErrorKind.MissingApiKey, ex.Kind);
    }

    [Fact]
    public async Task Yalnizca_yedek_saglayici_yapilandirilmissa_dogrudan_o_kullanilir()
    {
        var a = new FakeProvider("a", configured: false);
        var b = new FakeProvider("b").Succeeds();
        var outcome = await Service(new FakeNetwork(), a, b).AnalyzeAsync(Text, Deny, CancellationToken.None);
        Assert.Equal("B", outcome.Result.ProviderName);
        Assert.False(outcome.UsedFallback);
    }

    [Fact]
    public async Task Gecersiz_metin_saglayiciya_gonderilmez()
    {
        var a = new FakeProvider("a").Succeeds();
        var ex = await Assert.ThrowsAsync<AnalysisException>(() =>
            Service(new FakeNetwork(), a).AnalyzeAsync("   ", Approve, CancellationToken.None));
        Assert.Equal(ErrorKind.UnsupportedText, ex.Kind);
        Assert.Equal(0, a.Calls);
    }
}

/// <summary>Anahtarsız ücretsiz sağlayıcı (Pollinations).</summary>
public class PollinationsProviderTests
{
    private static PollinationsProvider Create(StubHandler h) => new(new StubHttpClientFactory(h), new InMemoryKeyStore());

    [Fact]
    public async Task Anahtar_olmadan_hazir_ve_analiz_yapar()
    {
        var h = new StubHandler().Respond(HttpStatusCode.OK, Samples.GroqEnvelope(Samples.ValidJson));
        var p = Create(h);

        Assert.True(p.IsConfigured);
        Assert.False(p.RequiresApiKey);
        var r = await p.AnalyzeAsync("Harika bir gün!", CancellationToken.None);

        Assert.Equal(Polarity.Positive, r.Polarity);
        Assert.Null(h.Requests[0].Headers.Authorization); // gömülü anahtar yok
        Assert.Contains("text.pollinations.ai", h.Requests[0].RequestUri!.ToString());
    }

    [Fact]
    public async Task Ilk_uc_nokta_kapaliysa_ikinci_ag_gecidi_denenir()
    {
        var h = new StubHandler()
            .Respond(HttpStatusCode.NotFound, "{}")
            .Respond(HttpStatusCode.OK, Samples.GroqEnvelope(Samples.ValidJson));
        var r = await Create(h).AnalyzeAsync("metin", CancellationToken.None);

        Assert.Equal(2, h.Requests.Count);
        Assert.Contains("gen.pollinations.ai", h.Requests[1].RequestUri!.ToString());
        Assert.Equal(Polarity.Positive, r.Polarity);
    }

    [Fact]
    public async Task Anonim_sinir_anlasilir_kota_hatasi_verir()
    {
        var h = new StubHandler().Respond((HttpStatusCode)429, "{}");
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Create(h).AnalyzeAsync("metin", CancellationToken.None));
        Assert.Equal(ErrorKind.QuotaExceeded, ex.Kind);
        Assert.Contains("15 saniye", ex.Detail);
    }

    [Fact]
    public async Task Hicbir_anahtar_yokken_anahtarsiz_saglayici_dogrudan_kullanilir()
    {
        var h = new StubHandler().Respond(HttpStatusCode.OK, Samples.GroqEnvelope(Samples.ValidJson));
        var keys = new InMemoryKeyStore();
        var http = new StubHttpClientFactory(h);
        var service = new AnalysisService(
            [new GeminiProvider(http, keys), new GroqProvider(http, keys), new PollinationsProvider(http, keys)],
            new FakeNetwork(), retryDelay: TimeSpan.Zero);

        var outcome = await service.AnalyzeAsync("Ürün çok güzel, teşekkürler.", _ => Task.FromResult(false), CancellationToken.None);

        Assert.False(outcome.UsedFallback); // onay sorulmadan, ilk ve tek hazır sağlayıcı
        Assert.Equal("Pollinations (ücretsiz)", outcome.Result.ProviderName);
    }
}
