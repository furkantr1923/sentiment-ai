using SentimentAI.Core.Privacy;

namespace SentimentAI.Core.Analysis;

/// <summary>
/// Tüm sağlayıcılar için ortak analiz talimatı. Model, yalnızca belirtilen
/// şemaya uyan tek bir JSON nesnesi döndürmeye yönlendirilir.
/// </summary>
public static class PromptBuilder
{
    /// <summary>Her analizde puanlanması istenen temel duygular.</summary>
    public static IReadOnlyList<string> CoreEmotions { get; } =
    [
        "Mutluluk", "Üzüntü", "Öfke", "Korku", "Şaşkınlık", "Tiksinme",
        "Güven", "Memnuniyet", "Hayal kırıklığı", "Heyecan", "Endişe",
    ];

    public const string SchemaVersion = "1";

    public static string SystemInstruction { get; } = BuildSystemInstruction();

    private static string BuildSystemInstruction() => $$"""
        Sen Türkçe metinler için uzman bir duygu ve anlam analizi motorusun.
        Görevin: kullanıcının verdiği TEK metni analiz edip YALNIZCA aşağıdaki şemaya uyan geçerli bir JSON nesnesi döndürmek.
        JSON dışında hiçbir açıklama, markdown veya kod bloğu yazma.

        Kurallar:
        - Tüm metin alanları Türkçe olsun.
        - Puanlar 0 ile 100 arasında tam sayı olsun. "dagilim" içindeki üç değerin toplamı 100 olsun.
        - "guven", sonucun ne kadar net olduğuna dair senin tahminindir; metin belirsiz, çok kısa veya çelişkiliyse düşük ver.
        - "duygular" listesinde şu duyguların HER BİRİNE puan ver: {{string.Join(", ", CoreEmotions)}}. Metinde açıkça başka bir duygu varsa (ör. Özlem, Gurur, Utanç) en fazla 3 tane daha ekleyebilirsin.
        - "baskin_duygu" tek bir duygu adı olsun ve "duygular" listesindeki en yüksek puanlı duyguyla tutarlı olsun.
        - Hedef kitle, amaç ve ironi/sarkazm/alay/mizah tespitleri kesin bilgi değil, metinden çıkarılan tahminlerdir; emin değilsen düşük puan ver.
        - Metindeki {{string.Join(", ", Anonymizer.Tokens)}} gibi köşeli parantezli ifadeler gizlilik için maskelenmiş kişisel verilerdir: bunları yazım hatası sayma, düzeltilmiş metinde aynen koru.
        - "yazim_hatalari" listesinde yalnızca gerçek yazım, dilbilgisi ve noktalama hatalarını ver; hata yoksa boş liste döndür.
        - "duzeltilmis_metin": metnin yazım/dilbilgisi/noktalama açısından düzeltilmiş hali. Anlamı ve üslubu değiştirme. Hata yoksa metni aynen yaz.
        - "anahtar_kelimeler": metnin en önemli 3–12 kelimesi veya kelime grubu; "agirlik" 0 ile 1 arası ondalık sayı.
        - "gelistirme_onerileri": anlatımı güçlendirecek en fazla 5 kısa, somut öneri.
        - "tekrarlar": gereksiz yere tekrarlanan kelime veya ifadeler; yoksa boş liste.
        - "ozet": en fazla 2 cümle.

        Şema:
        {
          "temel_duygu": "pozitif | negatif | nötr",
          "dagilim": { "pozitif": 0, "negatif": 0, "notr": 0 },
          "baskin_duygu": "string",
          "guven": 0,
          "duygular": [ { "ad": "string", "puan": 0 } ],
          "yogunluk": "düşük | orta | yüksek",
          "yogunluk_puani": 0,
          "konu": "string",
          "amac": "string",
          "hedef_kitle": "string",
          "ozet": "string",
          "anahtar_kelimeler": [ { "kelime": "string", "agirlik": 0.0 } ],
          "ironi":   { "var": false, "puan": 0, "aciklama": "string" },
          "sarkazm": { "var": false, "puan": 0, "aciklama": "string" },
          "alay":    { "var": false, "puan": 0, "aciklama": "string" },
          "mizah":   { "var": false, "puan": 0, "aciklama": "string" },
          "yazim_hatalari": [ { "hatali": "string", "oneri": "string", "tur": "yazım | dilbilgisi | noktalama", "aciklama": "string" } ],
          "duzeltilmis_metin": "string",
          "gelistirme_onerileri": [ "string" ],
          "tekrarlar": [ "string" ]
        }
        """;

    /// <summary>Kullanıcı mesajı: analiz edilecek (anonimleştirilmiş) metin.</summary>
    public static string BuildUserMessage(string anonymizedText) =>
        "Aşağıdaki metni analiz et ve yalnızca JSON döndür.\n\n<metin>\n" + anonymizedText + "\n</metin>";
}
