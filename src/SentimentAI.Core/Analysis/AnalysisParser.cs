using System.Globalization;
using System.Text.Json;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Models;

namespace SentimentAI.Core.Analysis;

/// <summary>
/// Sağlayıcıdan gelen JSON metnini <see cref="AnalysisResult"/> nesnesine dönüştürür.
/// Yansıma (reflection) kullanmaz; Android'de kırpma (trimming) güvenlidir.
/// Zorunlu alanlar eksikse sonuç uydurulmaz, <see cref="ErrorKind.InvalidResponse"/> fırlatılır.
/// </summary>
public static class AnalysisParser
{
    private const int MaxKeywords = 20;
    private const int MaxIssues = 40;
    private const int MaxSuggestions = 8;
    private const int MaxEmotions = 16;

    public static AnalysisResult Parse(string rawModelOutput, string providerName, string modelName)
    {
        var json = ExtractJsonObject(rawModelOutput)
                   ?? throw Invalid(providerName, "Yanıtta JSON nesnesi bulunamadı.");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 32,
            });
        }
        catch (JsonException ex)
        {
            throw new AnalysisException(ErrorKind.InvalidResponse, "Yanıt geçerli bir JSON değil.", providerName, inner: ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Invalid(providerName, "Yanıtın kök öğesi bir nesne değil.");

            // --- Temel duygu (zorunlu)
            var polarityText = GetString(root, "temel_duygu");
            if (string.IsNullOrWhiteSpace(polarityText))
                throw Invalid(providerName, "Yanıtta temel duygu bilgisi yok.");
            var polarity = ParsePolarity(polarityText);

            // --- Dağılım (zorunlu)
            if (!root.TryGetProperty("dagilim", out var dist) || dist.ValueKind != JsonValueKind.Object)
                throw Invalid(providerName, "Yanıtta pozitif/negatif/nötr dağılımı yok.");
            double pos = Clamp(GetNumber(dist, "pozitif") ?? 0);
            double neg = Clamp(GetNumber(dist, "negatif") ?? 0);
            double neu = Clamp(GetNumber(dist, "notr") ?? GetNumber(dist, "nötr") ?? 0);
            double sum = pos + neg + neu;
            if (sum <= 0)
                throw Invalid(providerName, "Dağılım değerleri geçersiz.");
            if (sum <= 1.0001) { pos *= 100; neg *= 100; neu *= 100; sum *= 100; } // 0–1 ölçeği
            (pos, neg, neu) = NormalizeTo100(pos, neg, neu);

            // --- Güven (zorunlu)
            var confidenceRaw = GetNumber(root, "guven")
                                ?? throw Invalid(providerName, "Yanıtta güven değeri yok.");
            double confidence = confidenceRaw is > 0 and <= 1 ? confidenceRaw * 100 : confidenceRaw;
            confidence = Math.Round(Clamp(confidence), 0);

            // --- Duygular
            var emotions = ParseEmotions(root);
            var dominant = GetString(root, "baskin_duygu")?.Trim();
            if (string.IsNullOrWhiteSpace(dominant))
                dominant = emotions.Count > 0 ? emotions[0].Name : null;
            if (string.IsNullOrWhiteSpace(dominant))
                throw Invalid(providerName, "Yanıtta baskın duygu bilgisi yok.");

            // --- Yoğunluk
            var intensityScore = GetNumber(root, "yogunluk_puani");
            var intensityText = GetString(root, "yogunluk");
            var intensity = ParseIntensity(intensityText, intensityScore);
            double intensityValue = intensityScore is { } s
                ? Math.Round(Clamp(s is > 0 and <= 1 ? s * 100 : s), 0)
                : intensity switch { Intensity.Low => 25, Intensity.High => 85, _ => 55 };

            return new AnalysisResult
            {
                Polarity = polarity,
                Positive = pos,
                Negative = neg,
                Neutral = neu,
                DominantEmotion = Capitalize(dominant!),
                Confidence = confidence,
                Emotions = emotions,
                Intensity = intensity,
                IntensityScore = intensityValue,
                Topic = Clean(GetString(root, "konu")),
                Purpose = Clean(GetString(root, "amac")),
                Audience = Clean(GetString(root, "hedef_kitle")),
                Summary = Clean(GetString(root, "ozet")),
                Keywords = ParseKeywords(root),
                Irony = ParseTone(root, "ironi", "İroni"),
                Sarcasm = ParseTone(root, "sarkazm", "Sarkazm"),
                Mockery = ParseTone(root, "alay", "Alay"),
                Humor = ParseTone(root, "mizah", "Mizah"),
                SpellingIssues = ParseIssues(root),
                CorrectedText = (GetString(root, "duzeltilmis_metin") ?? "").Trim(),
                Improvements = ParseStringList(root, "gelistirme_onerileri", MaxSuggestions),
                Repetitions = ParseStringList(root, "tekrarlar", MaxSuggestions),
                ProviderName = providerName,
                ModelName = modelName,
            };
        }
    }

    /// <summary>Model çıktısındaki ilk dengeli JSON nesnesini bulur (```json bloklarını da tolere eder).</summary>
    public static string? ExtractJsonObject(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;

        int start = raw.IndexOf('{');
        if (start < 0) return null;

        int depth = 0;
        bool inString = false, escape = false;
        for (int i = start; i < raw.Length; i++)
        {
            char c = raw[i];
            if (inString)
            {
                if (escape) escape = false;
                else if (c == '\\') escape = true;
                else if (c == '"') inString = false;
                continue;
            }

            switch (c)
            {
                case '"': inString = true; break;
                case '{': depth++; break;
                case '}':
                    depth--;
                    if (depth == 0) return raw.Substring(start, i - start + 1);
                    break;
            }
        }

        return null; // kapanmamış (kesilmiş) yanıt
    }

    public static Polarity ParsePolarity(string text)
    {
        var t = Fold(text);
        if (t.Contains("poz") || t.Contains("olumlu") || t.Contains("positive")) return Polarity.Positive;
        if (t.Contains("neg") || t.Contains("olumsuz")) return Polarity.Negative;
        return Polarity.Neutral;
    }

    public static Intensity ParseIntensity(string? text, double? score)
    {
        var t = Fold(text ?? "");
        if (t.Contains("dusuk") || t.Contains("low") || t.Contains("zayif")) return Intensity.Low;
        if (t.Contains("yuksek") || t.Contains("high") || t.Contains("guclu")) return Intensity.High;
        if (t.Contains("orta") || t.Contains("medium")) return Intensity.Medium;

        if (score is { } s)
        {
            if (s is > 0 and <= 1) s *= 100;
            return s < 34 ? Intensity.Low : s < 67 ? Intensity.Medium : Intensity.High;
        }

        return Intensity.Medium;
    }

    private static IReadOnlyList<EmotionScore> ParseEmotions(JsonElement root)
    {
        var list = new List<EmotionScore>();
        if (root.TryGetProperty("duygular", out var arr))
        {
            if (arr.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in arr.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object) continue;
                    var name = Clean(GetString(item, "ad") ?? GetString(item, "duygu"));
                    var score = GetNumber(item, "puan") ?? GetNumber(item, "skor");
                    if (name.Length == 0 || score is null) continue;
                    list.Add(new EmotionScore(Capitalize(name), score.Value));
                }
            }
            else if (arr.ValueKind == JsonValueKind.Object) // { "Mutluluk": 80, ... } biçimi
            {
                foreach (var p in arr.EnumerateObject())
                {
                    var score = AsNumber(p.Value);
                    if (score is null) continue;
                    list.Add(new EmotionScore(Capitalize(p.Name.Trim()), score.Value));
                }
            }
        }

        if (list.Count == 0) return list;

        // 0–1 ölçeğinde döndüyse 0–100'e çevir
        bool unitScale = list.All(e => e.Score <= 1.0);
        return list
            .Select(e => e with { Score = Math.Round(Clamp(unitScale ? e.Score * 100 : e.Score), 0) })
            .GroupBy(e => Fold(e.Name))
            .Select(g => g.OrderByDescending(e => e.Score).First())
            .OrderByDescending(e => e.Score)
            .Take(MaxEmotions)
            .ToList();
    }

    private static IReadOnlyList<KeywordWeight> ParseKeywords(JsonElement root)
    {
        var list = new List<KeywordWeight>();
        if (!root.TryGetProperty("anahtar_kelimeler", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            string word;
            double weight;
            if (item.ValueKind == JsonValueKind.String)
            {
                word = Clean(item.GetString());
                weight = 0.5;
            }
            else if (item.ValueKind == JsonValueKind.Object)
            {
                word = Clean(GetString(item, "kelime"));
                weight = GetNumber(item, "agirlik") ?? 0.5;
                if (weight > 1) weight /= 100;
            }
            else continue;

            if (word.Length == 0) continue;
            list.Add(new KeywordWeight(word, Math.Clamp(weight, 0.05, 1)));
        }

        return list
            .GroupBy(k => Fold(k.Word))
            .Select(g => g.OrderByDescending(k => k.Weight).First())
            .OrderByDescending(k => k.Weight)
            .Take(MaxKeywords)
            .ToList();
    }

    private static ToneFlag ParseTone(JsonElement root, string key, string label)
    {
        if (!root.TryGetProperty(key, out var el))
            return new ToneFlag(label, false, 0, "");

        if (el.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return new ToneFlag(label, el.GetBoolean(), el.GetBoolean() ? 60 : 0, "");

        if (el.ValueKind != JsonValueKind.Object)
            return new ToneFlag(label, false, 0, "");

        var score = GetNumber(el, "puan") ?? 0;
        if (score is > 0 and <= 1) score *= 100;
        score = Math.Round(Clamp(score), 0);
        var detected = GetBool(el, "var") ?? score >= 50;
        return new ToneFlag(label, detected, score, Clean(GetString(el, "aciklama")));
    }

    private static IReadOnlyList<SpellingIssue> ParseIssues(JsonElement root)
    {
        var list = new List<SpellingIssue>();
        if (!root.TryGetProperty("yazim_hatalari", out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object) continue;
            var original = Clean(GetString(item, "hatali"));
            var suggestion = Clean(GetString(item, "oneri"));
            if (original.Length == 0 && suggestion.Length == 0) continue;
            if (string.Equals(original, suggestion, StringComparison.Ordinal)) continue;
            list.Add(new SpellingIssue(original, suggestion, NormalizeIssueType(GetString(item, "tur")), Clean(GetString(item, "aciklama"))));
            if (list.Count >= MaxIssues) break;
        }

        return list;
    }

    private static string NormalizeIssueType(string? type)
    {
        var t = Fold(type ?? "");
        if (t.Contains("nokta")) return "Noktalama";
        if (t.Contains("dil")) return "Dilbilgisi";
        return "Yazım";
    }

    private static IReadOnlyList<string> ParseStringList(JsonElement root, string key, int max)
    {
        var list = new List<string>();
        if (!root.TryGetProperty(key, out var arr) || arr.ValueKind != JsonValueKind.Array)
            return list;

        foreach (var item in arr.EnumerateArray())
        {
            var s = item.ValueKind == JsonValueKind.String ? Clean(item.GetString()) : "";
            if (s.Length > 0 && !list.Contains(s, StringComparer.OrdinalIgnoreCase)) list.Add(s);
            if (list.Count >= max) break;
        }

        return list;
    }

    // ---------------------------------------------------------------- yardımcılar

    private static string? GetString(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? v.ValueKind switch
        {
            JsonValueKind.String => v.GetString(),
            JsonValueKind.Number => v.GetRawText(),
            _ => null,
        } : null;

    private static double? GetNumber(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var v) ? AsNumber(v) : null;

    private static double? AsNumber(JsonElement v)
    {
        if (v.ValueKind == JsonValueKind.Number && v.TryGetDouble(out var d)) return SafeDouble(d);
        if (v.ValueKind == JsonValueKind.String)
        {
            var s = (v.GetString() ?? "").Trim().TrimEnd('%').Replace(',', '.');
            if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var p)) return SafeDouble(p);
        }
        return null;
    }

    private static double? SafeDouble(double d) => double.IsFinite(d) ? d : null;

    private static bool? GetBool(JsonElement obj, string name)
    {
        if (!obj.TryGetProperty(name, out var v)) return null;
        return v.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String => Fold(v.GetString() ?? "") switch
            {
                "true" or "evet" or "var" => true,
                "false" or "hayir" or "yok" => false,
                _ => null,
            },
            _ => null,
        };
    }

    private static double Clamp(double v) => Math.Clamp(v, 0, 100);

    internal static (double, double, double) NormalizeTo100(double a, double b, double c)
    {
        double sum = a + b + c;
        double ra = Math.Round(a / sum * 100, 0);
        double rb = Math.Round(b / sum * 100, 0);
        double rc = 100 - ra - rb;
        if (rc < 0) { rb += rc; rc = 0; }
        return (ra, rb, rc);
    }

    private static string Clean(string? s) => (s ?? "").Trim();

    private static string Capitalize(string s)
    {
        s = s.Trim();
        if (s.Length == 0) return s;
        var tr = CultureInfo.GetCultureInfo("tr-TR");
        return char.ToUpper(s[0], tr) + s[1..];
    }

    /// <summary>Türkçe karakterleri sadeleştirip küçük harfe çevirir (karşılaştırma için).</summary>
    internal static string Fold(string s)
    {
        var lower = s.Trim().ToLower(CultureInfo.GetCultureInfo("tr-TR"));
        return lower
            .Replace('ı', 'i').Replace('ğ', 'g').Replace('ü', 'u')
            .Replace('ş', 's').Replace('ö', 'o').Replace('ç', 'c')
            .Replace("i̇", "i");
    }

    private static AnalysisException Invalid(string provider, string detail) =>
        new(ErrorKind.InvalidResponse, detail, provider);
}
