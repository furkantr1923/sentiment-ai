using System.Text;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Text;

namespace SentimentAI.Core.Files;

/// <summary>
/// Uzun metinleri, her biri analiz sınırına sığan kısa parçalara böler.
/// Böylece dosyanın tamamı asla otomatik olarak API'ye gönderilmez; kullanıcı tek bir parça seçer.
/// </summary>
internal sealed class SegmentBuilder
{
    private readonly List<TextSegment> _segments = [];
    private int _totalChars;

    public bool Truncated { get; private set; }

    public bool IsFull => _segments.Count >= AppLimits.MaxSegments || _totalChars >= AppLimits.MaxExtractedChars;

    public IReadOnlyList<TextSegment> Segments => _segments;

    /// <summary>Tek bir alan (hücre, satır) ekler; sınırı aşıyorsa cümle sınırından böler.</summary>
    public void AddField(string label, string? text)
    {
        var clean = TextValidator.Normalize(text);
        if (clean.Length < 2 || !TextValidator.ContainsLetter(clean)) return;

        var parts = SplitToFit(clean, AppLimits.MaxTextChars);
        for (int i = 0; i < parts.Count; i++)
        {
            if (IsFull) { Truncated = true; return; }
            var l = parts.Count == 1 ? label : $"{label} ({i + 1}/{parts.Count})";
            _segments.Add(new TextSegment(_segments.Count, l, parts[i]));
            _totalChars += parts[i].Length;
        }
    }

    /// <summary>Uzun bir metni paragraflara ayırır; çok kısa ardışık paragrafları birleştirir.</summary>
    public void AddDocumentText(string labelPrefix, string? text)
    {
        var clean = TextValidator.Normalize(text);
        if (clean.Length == 0) return;

        var paragraphs = clean.Split("\n\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Paragraf ayrımı yoksa tek satır sonlarından böl
        if (paragraphs.Length <= 1 && clean.Length > AppLimits.MaxTextChars)
            paragraphs = clean.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        AddParagraphs(labelPrefix, paragraphs);
    }

    public void AddParagraphs(string labelPrefix, IEnumerable<string> paragraphs)
    {
        var buffer = new StringBuilder();
        int n = 0;

        void Flush()
        {
            if (buffer.Length == 0) return;
            n++;
            AddField($"{labelPrefix} · Bölüm {n}", buffer.ToString());
            buffer.Clear();
        }

        foreach (var raw in paragraphs)
        {
            if (IsFull) { Truncated = true; return; }
            var p = TextValidator.Normalize(raw);
            if (p.Length == 0) continue;

            // Kısa paragraflar (başlık, tek satır) bir sonrakiyle birleştirilir;
            // yeterince uzun paragraflar kendi başına bir parça olur.
            if (buffer.Length > 0 && (buffer.Length >= 80 || buffer.Length + p.Length + 1 > 600)) Flush();
            if (buffer.Length > 0) buffer.Append('\n');
            buffer.Append(p);
        }

        Flush();
    }

    /// <summary>Metni en fazla <paramref name="max"/> karakterlik parçalara, mümkünse cümle sonlarından böler.</summary>
    internal static List<string> SplitToFit(string text, int max)
    {
        var result = new List<string>();
        if (text.Length <= max) { result.Add(text); return result; }

        int start = 0;
        while (start < text.Length)
        {
            int remaining = text.Length - start;
            if (remaining <= max)
            {
                result.Add(text[start..].Trim());
                break;
            }

            int window = start + max;
            int cut = LastBoundary(text, start, window);
            if (cut <= start) cut = window; // sınır bulunamadı: sert kes

            var piece = text[start..cut].Trim();
            if (piece.Length > 0) result.Add(piece);
            start = cut;
        }

        return result;
    }

    private static int LastBoundary(string text, int start, int end)
    {
        int minCut = start + (end - start) / 2; // parçalar çok küçük olmasın
        for (int i = end - 1; i >= minCut; i--)
        {
            char c = text[i];
            if (c is '.' or '!' or '?' or '…' or '\n')
                return i + 1;
        }
        for (int i = end - 1; i >= minCut; i--)
            if (char.IsWhiteSpace(text[i])) return i + 1;
        return -1;
    }
}
