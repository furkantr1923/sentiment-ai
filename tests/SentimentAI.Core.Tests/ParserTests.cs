using SentimentAI.Core.Analysis;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;
using SentimentAI.Core.Tests.Fakes;

namespace SentimentAI.Core.Tests;

public class AnalysisParserTests
{
    [Fact]
    public void Gecerli_yanit_modele_donusturulur()
    {
        var r = AnalysisParser.Parse(Samples.ValidJson, "Test", "m1");

        Assert.Equal(Polarity.Positive, r.Polarity);
        Assert.Equal(100, r.Positive + r.Negative + r.Neutral);
        Assert.Equal("Memnuniyet", r.DominantEmotion);
        Assert.Equal(86, r.Confidence);
        Assert.False(r.IsLowConfidence);
        Assert.Equal(Intensity.Medium, r.Intensity);
        Assert.Equal(58, r.IntensityScore);
        Assert.Equal("Memnuniyet", r.Emotions[0].Name); // en yüksek puan başta
        Assert.Equal(2, r.Keywords.Count);
        Assert.True(r.Humor.Detected);
        Assert.False(r.Irony.Detected);
        Assert.Single(r.SpellingIssues);
        Assert.Equal("Noktalama", r.SpellingIssues[0].Type);
        Assert.Equal("Ürün çok hızlı geldi. Çok memnunum.", r.CorrectedText);
        Assert.Equal("Test", r.ProviderName);
        Assert.Equal("m1", r.ModelName);
    }

    [Fact]
    public void Markdown_kod_blogu_icindeki_JSON_bulunur()
    {
        var wrapped = "İşte sonuç:\n```json\n" + Samples.ValidJson + "\n```\nUmarım yardımcı olur.";
        var r = AnalysisParser.Parse(wrapped, "Test", "m1");
        Assert.Equal(Polarity.Positive, r.Polarity);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Bu bir JSON değil")]
    [InlineData("{ \"temel_duygu\": \"pozitif\", ")] // kesilmiş yanıt
    [InlineData("[1,2,3]")]
    public void Bozuk_yanit_InvalidResponse_hatasi_verir(string raw)
    {
        var ex = Assert.Throws<AnalysisException>(() => AnalysisParser.Parse(raw, "Test", "m"));
        Assert.Equal(ErrorKind.InvalidResponse, ex.Kind);
    }

    [Fact]
    public void Zorunlu_alan_eksikse_sonuc_uydurulmaz()
    {
        const string noDistribution = """{ "temel_duygu": "negatif", "guven": 70, "baskin_duygu": "Öfke" }""";
        var ex = Assert.Throws<AnalysisException>(() => AnalysisParser.Parse(noDistribution, "Test", "m"));
        Assert.Equal(ErrorKind.InvalidResponse, ex.Kind);

        const string noConfidence = """{ "temel_duygu": "negatif", "dagilim": {"pozitif":10,"negatif":80,"notr":10}, "baskin_duygu": "Öfke" }""";
        Assert.Throws<AnalysisException>(() => AnalysisParser.Parse(noConfidence, "Test", "m"));
    }

    [Fact]
    public void Sifir_bir_olcegi_ve_yuzde_metinleri_tolere_edilir()
    {
        const string json = """
            {
              "temel_duygu": "Negative",
              "dagilim": { "pozitif": 0.1, "negatif": 0.7, "notr": 0.2 },
              "guven": 0.42,
              "duygular": [ { "ad": "öfke", "puan": "0.8" }, { "ad": "Üzüntü", "puan": 0.5 } ],
              "yogunluk_puani": "90%"
            }
            """;
        var r = AnalysisParser.Parse(json, "Test", "m");
        Assert.Equal(Polarity.Negative, r.Polarity);
        Assert.Equal(70, r.Negative);
        Assert.Equal(42, r.Confidence);
        Assert.True(r.IsLowConfidence);
        Assert.Equal("Öfke", r.DominantEmotion); // baskın duygu yoksa en yüksek puanlı duygu
        Assert.Equal(80, r.Emotions[0].Score);
        Assert.Equal(Intensity.High, r.Intensity);
    }

    [Theory]
    [InlineData("pozitif", Polarity.Positive)]
    [InlineData("Olumlu", Polarity.Positive)]
    [InlineData("NEGATİF", Polarity.Negative)]
    [InlineData("olumsuz", Polarity.Negative)]
    [InlineData("nötr", Polarity.Neutral)]
    [InlineData("karışık", Polarity.Neutral)]
    public void Temel_duygu_etiketleri_eslenir(string label, Polarity expected) =>
        Assert.Equal(expected, AnalysisParser.ParsePolarity(label));

    [Fact]
    public void Dagilim_100e_normallestirilir()
    {
        var (a, b, c) = AnalysisParser.NormalizeTo100(1, 1, 1);
        Assert.Equal(100, a + b + c);
    }
}
