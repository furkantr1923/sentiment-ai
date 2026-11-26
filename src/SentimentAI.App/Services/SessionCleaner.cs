using Microsoft.Extensions.Logging;
using SentimentAI.Core.Files;

namespace SentimentAI.App.Services;

/// <summary>
/// Geçici veri yönetimi: uygulama açılırken, dosya okunduktan sonra ve uygulama kapanırken
/// uygulamanın önbellek klasörünü temizler. Analiz geçmişi, metin veya sonuç diske hiç yazılmaz.
/// </summary>
public sealed class SessionCleaner(ILogger<SessionCleaner> logger)
{
    private const string SelfTestVariable = "SENTIMENTAI_SELFTEST";

    // Android'de MAUI'nin görsel önbelleği (Glide) uygulama çalışırken silinmez; kişisel veri içermez.
    private static readonly string[] KeepNames = ["image_manager_disk_cache"];

    public string CacheDirectory => FileSystem.CacheDirectory;

    public void Clean(string reason)
    {
        var removed = TempDataCleaner.CleanDirectory(CacheDirectory, KeepNames);
        if (removed > 0) logger.LogInformation("Geçici veriler temizlendi ({Reason}): {Count} öğe", reason, removed);
    }

    /// <summary>
    /// Otomatik Windows testleri için: SENTIMENTAI_SELFTEST=1 ise önbelleğe bir işaret dosyası yazar ve
    /// yolunu %TEMP%\sentimentai-selftest.txt dosyasına bildirir. Test, uygulama kapandıktan sonra
    /// işaretin silindiğini doğrular. Normal kullanımda hiçbir şey yapmaz.
    /// </summary>
    public void WriteSelfTestMarkerIfRequested()
    {
        if (Environment.GetEnvironmentVariable(SelfTestVariable) != "1") return;
        try
        {
            Directory.CreateDirectory(CacheDirectory);
            var marker = Path.Combine(CacheDirectory, "selftest-marker.tmp");
            File.WriteAllText(marker, "geçici");
            File.WriteAllText(Path.Combine(Path.GetTempPath(), "sentimentai-selftest.txt"), marker);
        }
        catch (Exception ex)
        {
            logger.LogWarning("Self-test işareti yazılamadı: {Type}", ex.GetType().Name);
        }
    }
}
