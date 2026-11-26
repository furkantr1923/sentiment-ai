using System.Text;

namespace SentimentAI.Core.Files;

/// <summary>RFC 4180 uyumlu, hafif CSV ayrıştırıcı. Ayırıcıyı (; , sekme |) otomatik tanır.</summary>
public static class CsvParser
{
    private const int MaxRows = 100_000;

    public static IReadOnlyList<IReadOnlyList<string>> Parse(string text)
    {
        var rows = new List<IReadOnlyList<string>>();
        if (string.IsNullOrEmpty(text)) return rows;

        char delimiter = DetectDelimiter(text);
        var row = new List<string>();
        var field = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            if (c == '"' && field.Length == 0) { inQuotes = true; continue; }

            if (c == delimiter)
            {
                row.Add(field.ToString().Trim());
                field.Clear();
            }
            else if (c is '\n' or '\r')
            {
                if (c == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                row.Add(field.ToString().Trim());
                field.Clear();
                if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row);
                row = [];
                if (rows.Count >= MaxRows) return rows;
            }
            else field.Append(c);
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString().Trim());
            if (!(row.Count == 1 && row[0].Length == 0)) rows.Add(row);
        }

        return rows;
    }

    /// <summary>İlk satırlarda tırnak dışında en sık geçen ayırıcıyı seçer.</summary>
    public static char DetectDelimiter(string text)
    {
        ReadOnlySpan<char> candidates = [';', ',', '\t', '|'];
        Span<int> counts = stackalloc int[candidates.Length];
        bool inQuotes = false;
        int lines = 0;

        for (int i = 0; i < text.Length && lines < 5; i++)
        {
            char c = text[i];
            if (c == '"') inQuotes = !inQuotes;
            else if (!inQuotes && c == '\n') lines++;
            else if (!inQuotes)
            {
                int idx = candidates.IndexOf(c);
                if (idx >= 0) counts[idx]++;
            }
        }

        int best = 1; // varsayılan: virgül
        for (int k = 0; k < candidates.Length; k++)
            if (counts[k] > counts[best]) best = k;
        return candidates[best];
    }
}
