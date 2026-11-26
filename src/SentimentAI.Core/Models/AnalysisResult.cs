using SentimentAI.Core.Configuration;

namespace SentimentAI.Core.Models;

public enum Polarity { Positive, Negative, Neutral }

public enum Intensity { Low, Medium, High }

/// <summary>Bir duygunun 0–100 arası tahmini puanı.</summary>
public sealed record EmotionScore(string Name, double Score);

/// <summary>Anahtar kelime ve 0–1 arası önem ağırlığı.</summary>
public sealed record KeywordWeight(string Word, double Weight);

/// <summary>İroni, sarkazm, alay veya mizah tespiti (tahmin).</summary>
public sealed record ToneFlag(string Name, bool Detected, double Score, string Note);

/// <summary>Yazım, dilbilgisi veya noktalama önerisi.</summary>
public sealed record SpellingIssue(string Original, string Suggestion, string Type, string Note);

/// <summary>
/// Tek bir metnin analiz sonucu. Tüm alanlar yapay zekâ sağlayıcısının
/// döndürdüğü gerçek yanıttan üretilir; sabit veya varsayılan grafik verisi yoktur.
/// </summary>
public sealed class AnalysisResult
{
    public required Polarity Polarity { get; init; }

    /// <summary>Pozitif / negatif / nötr dağılımı (toplam 100).</summary>
    public required double Positive { get; init; }
    public required double Negative { get; init; }
    public required double Neutral { get; init; }

    public required string DominantEmotion { get; init; }

    /// <summary>Modelin tahmini güveni (0–100). İstatistiksel olarak kalibre edilmiş kesinlik değildir.</summary>
    public required double Confidence { get; init; }

    public required IReadOnlyList<EmotionScore> Emotions { get; init; }

    public required Intensity Intensity { get; init; }

    /// <summary>Duygu yoğunluğu (0–100).</summary>
    public required double IntensityScore { get; init; }

    public string Topic { get; init; } = "";
    public string Purpose { get; init; } = "";
    public string Audience { get; init; } = "";
    public string Summary { get; init; } = "";

    public IReadOnlyList<KeywordWeight> Keywords { get; init; } = [];

    public required ToneFlag Irony { get; init; }
    public required ToneFlag Sarcasm { get; init; }
    public required ToneFlag Mockery { get; init; }
    public required ToneFlag Humor { get; init; }

    public IReadOnlyList<SpellingIssue> SpellingIssues { get; init; } = [];
    public string CorrectedText { get; init; } = "";
    public IReadOnlyList<string> Improvements { get; init; } = [];
    public IReadOnlyList<string> Repetitions { get; init; } = [];

    public string ProviderName { get; init; } = "";
    public string ModelName { get; init; } = "";

    public bool IsLowConfidence => Confidence < AppLimits.LowConfidenceThreshold;

    public IReadOnlyList<ToneFlag> ToneFlags => [Irony, Sarcasm, Mockery, Humor];

    public static string PolarityLabel(Polarity p) => p switch
    {
        Polarity.Positive => "Pozitif",
        Polarity.Negative => "Negatif",
        _ => "Nötr",
    };

    public static string IntensityLabel(Intensity i) => i switch
    {
        Intensity.Low => "Düşük",
        Intensity.High => "Yüksek",
        _ => "Orta",
    };
}
