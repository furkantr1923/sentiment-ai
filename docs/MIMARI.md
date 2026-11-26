# 🏛️ Mimari Açıklama

## Katmanlar

```
src/
├── SentimentAI.Core/            # Arayüzden bağımsız iş mantığı (net10.0, kırpma/AOT uyumlu)
│   ├── Analysis/                # AnalysisService (akış + yedek geçiş), PromptBuilder, AnalysisParser
│   ├── Providers/               # IAIProvider, HttpAIProviderBase, GeminiProvider, GroqProvider, hata sınıflandırma
│   ├── Privacy/                 # Anonymizer (kaynak üretilmiş Regex)
│   ├── Files/                   # FileTextExtractor (TXT/PDF/DOCX/CSV/XLSX), CsvParser, TempDataCleaner
│   ├── Text/                    # TextValidator
│   ├── Errors/                  # ErrorKind, AnalysisException, Türkçe ErrorMessages
│   └── Configuration/AppLimits  # Tüm sınırlar tek yerde
└── SentimentAI.App/             # .NET MAUI (Windows + Android)
    ├── Views/                   # DesktopMainPage (Windows), MobileMainPage (Android), ResultsView, SettingsPage, SegmentPickerPage
    ├── ViewModels/              # MainViewModel, SettingsViewModel (MVVM)
    ├── Charts/                  # Halka, çubuk, gösterge, kelime bulutu (Microsoft.Maui.Graphics)
    ├── Services/                # SecureApiKeyStore, MauiNetworkStatus, DialogService, SessionCleaner
    └── Controls/NeonProgressBar # Belirsiz süreli ilerleme animasyonu
```

## Analiz akışı

1. **Doğrulama** — boş, harfsiz veya 2.000 karakteri aşan metin reddedilir.
2. **Bağlantı** — internet yoksa istek yapılmaz.
3. **Anonimleştirme (cihazda)** — e-posta, telefon, T.C. kimlik no (algoritma kontrolü), IBAN, kart no (Luhn), açık adres kalıpları, sosyal profil bağlantıları yer tutucuya çevrilir.
4. **Sağlayıcı** — `IAIProvider.AnalyzeAsync`; ortak sistem talimatı modeli katı bir JSON şemasına yönlendirir (`responseMimeType: application/json` / `response_format: json_object`).
5. **Model yedeği** — model emekliye ayrılmışsa (404) aynı sağlayıcının sıradaki modeli denenir.
6. **Hata sınıflandırma** — HTTP kodu ve gövde → `ErrorKind` (geçersiz anahtar, kota, erişilemez, zaman aşımı…).
7. **Yeniden deneme / yedek sağlayıcı** — geçici hatada 1 yeniden deneme; kota/anahtar/erişim hatasında kullanıcı onayıyla yedek sağlayıcı.
8. **Ayrıştırma** — `AnalysisParser` yansıma kullanmadan `JsonDocument` ile okur; zorunlu alan yoksa sonuç **uydurulmaz**, `InvalidResponse` döner.
9. **Sunum** — grafikler sonuç başına bir kez oluşturulur; yeni analizde eski sonuç temizlenir.

## Tasarım kararları

| Karar | Neden |
|---|---|
| .NET MAUI + tek proje | Ortak C# iş mantığı, Windows ve Android için yerel arayüz |
| Platforma özel sayfalar, ortak ViewModel | Masaüstünde iki sütun; mobilde başparmak bölgesinde sabit eylem çubuğu |
| Ek MVVM/grafik kütüphanesi yok | Hafiflik; `ObservableObject` + `AsyncCommand` ve `Maui.Graphics` yeterli |
| Yalnızca 3 NuGet paketi | `Microsoft.Extensions.Http` (HttpClientFactory), `UglyToad.PdfPig` (PDF), MAUI |
| DOCX/XLSX için kendi OOXML okuyucu | ZIP + akışlı `XmlReader`; Open XML SDK'nın ~6 MB'ını taşımamak için. DTD kapalı (XXE koruması), zip bombasına karşı boyut sınırı |
| Kaynak üretilmiş Regex | Android'de hızlı açılış, kırpma uyumu |
| `HttpClientFactory` + adlandırılmış istemciler | Bağlantı havuzu yönetimi, DNS yenileme |
| Zaman aşımı sağlayıcı katmanında | Kullanıcı iptali ile zaman aşımını ayırt edebilmek |
| Android `targetSdkVersion 34` + `adjustResize` | Klavye açıldığında giriş alanı ve alt çubuğun görünür kalması |
| Windows paketsiz + bağımsız | MSIX/Store gerekmeden Setup, Portable ve zip dağıtımı |
| Portable başlatıcı .NET Framework 4.8 | Windows 10/11'de yerleşik; ek çalışma zamanı olmadan tek dosya |

## Gizlilik ve veri yaşam döngüsü

- Metin ve sonuçlar yalnızca `MainViewModel` belleğinde tutulur; diske, tercihlere veya loga yazılmaz.
- Loglarda yalnızca sağlayıcı kimliği, HTTP durum kodu ve hata türü bulunur.
- `SessionCleaner` uygulama önbelleğini açılışta, dosya okunduktan sonra ve kapanışta temizler.
- Dosyalar bellekte okunur; yalnızca kullanıcının seçtiği tek parça analiz alanına alınır.
