using System.Text;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;

namespace SentimentAI.Core.Text;

/// <summary>Analize gönderilecek metni doğrular ve normalleştirir.</summary>
public static class TextValidator
{
    /// <summary>Metni temizler; geçersizse <see cref="AnalysisException"/> fırlatır.</summary>
    public static string Validate(string? raw)
    {
        var text = Normalize(raw);

        if (text.Length == 0)
            throw new AnalysisException(ErrorKind.UnsupportedText, "Analiz için önce bir metin gir.");

        if (text.Length > AppLimits.MaxTextChars)
            throw new AnalysisException(ErrorKind.TextTooLong,
                $"Metin {text.Length:N0} karakter. Tek seferde en fazla {AppLimits.MaxTextChars:N0} karakter analiz edilebilir.");

        if (text.Length < AppLimits.MinTextChars || !ContainsLetter(text))
            throw new AnalysisException(ErrorKind.UnsupportedText);

        return text;
    }

    /// <summary>Satır sonlarını birleştirir, baştaki/sondaki boşlukları ve fazla boş satırları atar.</summary>
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
            return string.Empty;

        var s = raw.Replace("\r\n", "\n").Replace('\r', '\n').Replace(' ', ' ').Trim();

        var sb = new StringBuilder(s.Length);
        int newlineRun = 0;
        foreach (var ch in s)
        {
            if (ch == '\n')
            {
                newlineRun++;
                if (newlineRun <= 2) sb.Append(ch);
                continue;
            }

            newlineRun = 0;
            // Görünmeyen kontrol karakterlerini at (sekme hariç)
            if (char.IsControl(ch) && ch != '\t') continue;
            sb.Append(ch);
        }

        return sb.ToString();
    }

    public static bool ContainsLetter(string text)
    {
        foreach (var ch in text)
            if (char.IsLetter(ch)) return true;
        return false;
    }

    /// <summary>Karakter sayacı için normalleştirilmiş uzunluk.</summary>
    public static int CountChars(string? raw) => Normalize(raw).Length;
}
