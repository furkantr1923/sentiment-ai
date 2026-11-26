using System.Text.RegularExpressions;

namespace SentimentAI.Core.Privacy;

/// <summary>Anonimleştirme sonucunda hangi türden kaç değer maskelendiği.</summary>
public sealed record AnonymizationReport(string Text, IReadOnlyDictionary<string, int> Counts)
{
    public int Total => Counts.Values.Sum();
    public bool Any => Total > 0;

    /// <summary>"2 e-posta, 1 telefon" gibi kısa özet.</summary>
    public string Describe() => string.Join(", ", Counts.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}"));
}

/// <summary>
/// API'ye gönderilmeden önce metindeki açık kişisel veri kalıplarını cihaz üzerinde maskeler.
/// Otomatik anonimleştirme her kişisel bilgiyi yakalayamaz (ör. isimler, serbest biçimli adresler);
/// bu sınırlama kullanıcıya gizlilik açıklamasında bildirilir.
/// </summary>
public static partial class Anonymizer
{
    public const string EmailToken = "[E-POSTA]";
    public const string PhoneToken = "[TELEFON]";
    public const string NationalIdToken = "[TC KİMLİK NO]";
    public const string IbanToken = "[IBAN]";
    public const string CardToken = "[KART NO]";
    public const string AddressToken = "[ADRES]";
    public const string UrlToken = "[KİŞİSEL BAĞLANTI]";

    public static IReadOnlyList<string> Tokens { get; } =
        [EmailToken, PhoneToken, NationalIdToken, IbanToken, CardToken, AddressToken, UrlToken];

    public static AnonymizationReport Anonymize(string text)
    {
        var counts = new Dictionary<string, int>
        {
            ["e-posta"] = 0,
            ["IBAN"] = 0,
            ["kart numarası"] = 0,
            ["TC kimlik no"] = 0,
            ["telefon"] = 0,
            ["adres"] = 0,
            ["kişisel bağlantı"] = 0,
        };

        if (string.IsNullOrEmpty(text))
            return new AnonymizationReport(text ?? "", counts);

        // Sıra önemlidir: önce en belirgin kalıplar (e-posta, IBAN), sonra sayısal kimlikler.
        text = EmailRegex().Replace(text, _ => { counts["e-posta"]++; return EmailToken; });
        text = PersonalUrlRegex().Replace(text, _ => { counts["kişisel bağlantı"]++; return UrlToken; });
        text = IbanRegex().Replace(text, _ => { counts["IBAN"]++; return IbanToken; });

        text = CardRegex().Replace(text, m =>
        {
            var digits = DigitsOnly(m.Value);
            if (digits.Length is < 13 or > 19 || !PassesLuhn(digits)) return m.Value;
            counts["kart numarası"]++;
            return CardToken;
        });

        text = NationalIdRegex().Replace(text, m =>
        {
            if (!IsValidTurkishNationalId(m.Value)) return m.Value;
            counts["TC kimlik no"]++;
            return NationalIdToken;
        });

        text = PhoneRegex().Replace(text, m =>
        {
            var digits = DigitsOnly(m.Value);
            // 10 haneli yerel (5xx..., 2xx...) veya başında 0/90 olan numaralar
            if (digits.StartsWith("90", StringComparison.Ordinal) && digits.Length == 12) digits = digits[2..];
            else if (digits.StartsWith('0') && digits.Length == 11) digits = digits[1..];
            if (digits.Length != 10 || digits[0] is not ('2' or '3' or '4' or '5' or '8')) return m.Value;
            counts["telefon"]++;
            return PhoneToken;
        });

        text = AddressRegex().Replace(text, _ => { counts["adres"]++; return AddressToken; });
        text = StreetAddressRegex().Replace(text, _ => { counts["adres"]++; return AddressToken; });

        return new AnonymizationReport(text, counts);
    }

    /// <summary>T.C. kimlik numarası algoritma kontrolü (11 hane, ilk hane 0 olamaz, iki kontrol hanesi).</summary>
    public static bool IsValidTurkishNationalId(string value)
    {
        if (value.Length != 11 || value[0] == '0') return false;
        Span<int> d = stackalloc int[11];
        for (int i = 0; i < 11; i++)
        {
            if (!char.IsAsciiDigit(value[i])) return false;
            d[i] = value[i] - '0';
        }

        int odd = d[0] + d[2] + d[4] + d[6] + d[8];
        int even = d[1] + d[3] + d[5] + d[7];
        int c10 = (((odd * 7) - even) % 10 + 10) % 10;
        if (c10 != d[9]) return false;

        int sum = 0;
        for (int i = 0; i < 10; i++) sum += d[i];
        return sum % 10 == d[10];
    }

    /// <summary>Kart numaraları için Luhn kontrolü.</summary>
    public static bool PassesLuhn(string digits)
    {
        int sum = 0;
        bool dbl = false;
        for (int i = digits.Length - 1; i >= 0; i--)
        {
            int n = digits[i] - '0';
            if (n is < 0 or > 9) return false;
            if (dbl) { n *= 2; if (n > 9) n -= 9; }
            sum += n;
            dbl = !dbl;
        }
        return sum % 10 == 0;
    }

    private static string DigitsOnly(string s)
    {
        Span<char> buf = stackalloc char[s.Length];
        int n = 0;
        foreach (var ch in s)
            if (char.IsAsciiDigit(ch)) buf[n++] = ch;
        return new string(buf[..n]);
    }

    [GeneratedRegex(@"[A-Za-z0-9._%+\-]+@[A-Za-z0-9.\-]+\.[A-Za-z]{2,}", RegexOptions.CultureInvariant)]
    private static partial Regex EmailRegex();

    // Kişiyi doğrudan tanımlayan profil bağlantıları (sosyal medya kullanıcı sayfaları)
    [GeneratedRegex(@"https?://(?:www\.)?(?:instagram\.com|facebook\.com|x\.com|twitter\.com|linkedin\.com/in|tiktok\.com/@)[^\s]*",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PersonalUrlRegex();

    [GeneratedRegex(@"\bTR\s?\d{2}(?:\s?\d{4}){5}\s?\d{2}\b", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex IbanRegex();

    [GeneratedRegex(@"(?<!\d)\d(?:[ \-]?\d){12,18}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex CardRegex();

    [GeneratedRegex(@"(?<!\d)[1-9]\d{10}(?!\d)", RegexOptions.CultureInvariant)]
    private static partial Regex NationalIdRegex();

    [GeneratedRegex(@"(?<![\d\w])(?:\+?90[\s\-.]?)?(?:\(0?\d{3}\)|0?\d{3})[\s\-.]?\d{3}[\s\-.]?\d{2}[\s\-.]?\d{2}(?!\d)",
        RegexOptions.CultureInvariant)]
    private static partial Regex PhoneRegex();

    // "... Mahallesi / Mah. / Mh. ... No: 12/3" biçimindeki açık adresler
    [GeneratedRegex(@"(?:[\p{L}\d]+\s+){1,3}(?:mahallesi|mah\.|mh\.)[^\n]{0,90}?(?:no\s*[:.]?\s*\d+[\p{L}]?(?:\s*/\s*\d+)?|daire\s*[:.]?\s*\d+)",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex AddressRegex();

    // "... Sokak / Cadde / Bulvarı No: 5" biçimindeki adresler
    [GeneratedRegex(@"(?:[\p{L}\d]+\s+){1,3}(?:sokak|sokağı|sok\.|sk\.|cadde|caddesi|cad\.|cd\.|bulvarı|blv\.)\s*,?\s*no\s*[:.]?\s*\d+[\p{L}]?(?:\s*/\s*\d+)?",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex StreetAddressRegex();
}
