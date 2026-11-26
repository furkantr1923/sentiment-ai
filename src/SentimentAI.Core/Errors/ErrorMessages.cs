using SentimentAI.Core.Configuration;

namespace SentimentAI.Core.Errors;

/// <summary>Kullanıcıya gösterilen hata: ne oldu, neden olmuş olabilir, ne yapılabilir.</summary>
public sealed record UserError(string Title, string Reason, string Action);

/// <summary>Hata kategorilerini sade Türkçe açıklamalara dönüştürür.</summary>
public static class ErrorMessages
{
    public static UserError For(AnalysisException ex)
    {
        var baseError = For(ex.Kind);
        return string.IsNullOrWhiteSpace(ex.Detail)
            ? baseError
            : baseError with { Reason = ex.Detail! };
    }

    public static UserError For(ErrorKind kind) => kind switch
    {
        ErrorKind.NoInternet => new(
            "İnternet bağlantısı yok",
            "Cihaz şu anda internete bağlı görünmüyor. Analiz çevrimiçi yapay zekâ servisiyle yapıldığı için bağlantı gerekir.",
            "Wi-Fi veya mobil veri bağlantını kontrol edip tekrar dene."),

        ErrorKind.MissingApiKey => new(
            "API anahtarı gerekli",
            "Henüz hiçbir yapay zekâ sağlayıcısı için API anahtarı girilmemiş.",
            "Ayarlar → API Yapılandırması bölümünden ücretsiz bir Gemini veya Groq anahtarı ekle."),

        ErrorKind.InvalidApiKey => new(
            "API anahtarı geçersiz",
            "Sağlayıcı girilen anahtarı kabul etmedi. Anahtar hatalı kopyalanmış, silinmiş ya da kısıtlanmış olabilir.",
            "Ayarlar'dan anahtarı kontrol et, gerekirse sağlayıcının panelinden yeni anahtar oluştur."),

        ErrorKind.QuotaExceeded => new(
            "API kotası doldu",
            "Ücretsiz kullanım sınırına (dakikalık veya günlük) ulaşıldı.",
            "Bir süre bekleyip tekrar dene veya Ayarlar'dan diğer sağlayıcı için anahtar ekle."),

        ErrorKind.ProviderUnavailable => new(
            "Sağlayıcıya ulaşılamıyor",
            "Yapay zekâ servisi şu anda yanıt vermiyor veya geçici olarak yoğun.",
            "Birkaç dakika sonra tekrar dene."),

        ErrorKind.Timeout => new(
            "İstek zaman aşımına uğradı",
            "Servis belirlenen sürede yanıt vermedi. Bağlantı yavaş olabilir veya servis yoğun olabilir.",
            "Bağlantını kontrol edip tekrar dene."),

        ErrorKind.UnsupportedText => new(
            "Metin analiz edilemiyor",
            "Metin boş, çok kısa ya da harf içermiyor.",
            $"En az {AppLimits.MinTextChars} karakterlik, harf içeren bir metin gir."),

        ErrorKind.TextTooLong => new(
            "Metin çok uzun",
            $"Tek seferde en fazla {AppLimits.MaxTextChars:N0} karakter analiz edilebilir.",
            "Metni kısalt veya dosyadan daha kısa bir bölüm seç."),

        ErrorKind.FileReadFailed => new(
            "Dosya okunamadı",
            "Dosya bozuk, şifreli ya da desteklenmeyen bir biçimde olabilir.",
            "TXT, PDF, DOCX, CSV veya XLSX biçiminde, şifresiz bir dosya seç."),

        ErrorKind.InvalidResponse => new(
            "Geçersiz yanıt alındı",
            "Yapay zekâ servisi beklenen biçimde bir yanıt döndürmedi.",
            "Tekrar dene. Sorun sürerse metni biraz değiştirip yeniden analiz et."),

        _ => new(
            "Analiz başarısız",
            "Beklenmeyen bir sorun oluştu.",
            "Tekrar dene. Sorun sürerse uygulamayı yeniden başlat."),
    };
}
