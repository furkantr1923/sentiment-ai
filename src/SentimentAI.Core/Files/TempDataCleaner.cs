namespace SentimentAI.Core.Files;

/// <summary>
/// Uygulamanın kendi önbellek klasöründeki geçici dosyaları siler.
/// (Android dosya seçici, seçilen dosyanın kopyasını uygulama önbelleğine yazabilir.)
/// </summary>
public static class TempDataCleaner
{
    /// <summary>Klasörün içeriğini siler (klasörün kendisini değil). Silinen öğe sayısını döndürür.</summary>
    /// <param name="directory">Temizlenecek klasör.</param>
    /// <param name="keepNames">Silinmeyecek alt klasör/dosya adları (ör. görsel önbelleği).</param>
    public static int CleanDirectory(string? directory, IReadOnlyCollection<string>? keepNames = null)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
            return 0;

        int removed = 0;
        bool Keep(string path) => keepNames is not null && keepNames.Contains(Path.GetFileName(path));

        foreach (var file in SafeEnumerate(() => Directory.EnumerateFiles(directory)))
        {
            if (Keep(file)) continue;
            try { File.Delete(file); removed++; }
            catch (IOException) { /* kullanımda: bir sonraki temizlikte silinir */ }
            catch (UnauthorizedAccessException) { }
        }

        foreach (var dir in SafeEnumerate(() => Directory.EnumerateDirectories(directory)))
        {
            if (Keep(dir)) continue;
            try { Directory.Delete(dir, recursive: true); removed++; }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        return removed;
    }

    private static IEnumerable<string> SafeEnumerate(Func<IEnumerable<string>> source)
    {
        try { return source().ToList(); }
        catch (Exception) { return []; }
    }
}
