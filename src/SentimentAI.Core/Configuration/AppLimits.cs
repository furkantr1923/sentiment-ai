namespace SentimentAI.Core.Configuration;

/// <summary>
/// Uygulamanın tüm sınırları tek bir yerden yönetilir.
/// Değerler; ücretsiz API kotaları (dakikalık token sınırları), yanıt süresi
/// ve mobil cihazlardaki bellek kullanımı dikkate alınarak seçilmiştir.
/// </summary>
public static class AppLimits
{
    /// <summary>Tek analizde gönderilebilecek en fazla karakter.</summary>
    /// <remarks>
    /// 2.000 karakter ≈ 500–700 token. İstem + yanıt ile birlikte Groq ücretsiz
    /// katmanındaki dakikalık token sınırının (≈8K TPM) altında kalır.
    /// </remarks>
    public const int MaxTextChars = 2000;

    /// <summary>Anlamlı bir analiz için gereken en az karakter.</summary>
    public const int MinTextChars = 3;

    /// <summary>Yüklenebilecek en büyük dosya (10 MB).</summary>
    public const long MaxFileBytes = 10L * 1024 * 1024;

    /// <summary>Bir dosyadan çıkarılacak en fazla toplam metin.</summary>
    public const int MaxExtractedChars = 2_000_000;

    /// <summary>Seçim listesinde gösterilecek en fazla metin parçası.</summary>
    public const int MaxSegments = 1000;

    /// <summary>Arşiv (DOCX/XLSX) içindeki tek bir XML parçasının açılmış en büyük boyutu.</summary>
    public const long MaxArchiveEntryBytes = 64L * 1024 * 1024;

    /// <summary>Tek bir API isteğinin zaman aşımı.</summary>
    public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(45);

    /// <summary>Durum kontrolü isteklerinin zaman aşımı.</summary>
    public static readonly TimeSpan StatusTimeout = TimeSpan.FromSeconds(12);

    /// <summary>Geçici ağ hatalarında yapılacak en fazla yeniden deneme.</summary>
    public const int MaxTransientRetries = 1;

    /// <summary>Bu değerin altındaki model güveni "düşük güven" olarak işaretlenir.</summary>
    public const double LowConfidenceThreshold = 55;
}
