# 🔑 API Yapılandırma Rehberi

SENTIMENT AI analizleri gerçek yapay zekâ API'leriyle yapar. **Hiçbir ayar gerekmez:** uygulama varsayılan olarak anahtarsız ve ücretsiz **Pollinations.ai** servisini kullanır. Uygulamanın içinde gizli bir API anahtarı gömülü değildir (çalınabilecek bir anahtar yoktur).

## Varsayılan: Pollinations.ai (anahtarsız)

| | |
|---|---|
| Uç nokta | `text.pollinations.ai/openai` → yedek `gen.pollinations.ai/v1/chat/completions` |
| Anahtar / hesap | Gerekmez |
| Sınır | Anonim kullanımda yaklaşık 15 saniyede 1 istek; yoğunlukta 503 → 5 sn sonra 1 yeniden deneme |
| Not | Ücretsiz topluluk servisidir, erişilebilirlik garantisi yoktur |

## İsteğe bağlı: kendi anahtarınız

Daha yüksek kota ve kararlılık için kendi ücretsiz anahtarınızı ekleyebilirsiniz; girildiğinde önce bu sağlayıcı kullanılır.

## Desteklenen sağlayıcılar

| | Google Gemini (ana) | Groq (yedek) |
|---|---|---|
| Anahtar adresi | https://aistudio.google.com/app/apikey | https://console.groq.com/keys |
| Kredi kartı | Gerekmez (ücretsiz katman) | Gerekmez (Free plan) |
| Modeller | `gemini-flash-latest` → yedek `gemini-flash-lite-latest` | `openai/gpt-oss-120b` → yedek `llama-3.3-70b-versatile` |
| Uç nokta | `generativelanguage.googleapis.com/v1beta` | `api.groq.com/openai/v1` |
| Sınır aşılınca | HTTP 429 | HTTP 429 (ücret kesilmez) |
| Veri kullanımı | Ücretsiz katmanda içerik Google ürünlerini geliştirmek için kullanılabilir | Groq müşteri verisini model eğitiminde kullanmaz |

> ⚠️ Ücretsiz kotalar sağlayıcı tarafından her an değiştirilebilir; "sonsuza kadar ücretsiz" varsayılmamalıdır. Güncel sınırlar sağlayıcı panelinde görünür.

## Uygulamada anahtar girme

1. **Ayarlar** (⚙️) → **API yapılandırması** bölümünü açın.
2. "Ücretsiz anahtar al ↗" ile sağlayıcı sayfasına gidip anahtar oluşturun.
3. Anahtarı alana yapıştırıp **Kaydet ve test et**'e basın. Durum "Hazır · anahtar geçerli" olmalıdır.

Anahtarlar cihazın güvenli deposunda tutulur: **Android Keystore** / **Windows DPAPI** (MAUI `SecureStorage`). Düz metin dosyasına veya loga yazılmaz.

## Yedek sağlayıcıya geçiş

Ana sağlayıcı **kota**, **geçersiz anahtar**, **erişilemezlik** veya **zaman aşımı** hatası verirse ve diğer sağlayıcının anahtarı da girilmişse:

1. Geçici ağ hatalarında aynı sağlayıcı **en fazla 1 kez** yeniden denenir (429'da yeniden deneme yapılmaz).
2. Kullanıcıya, metnin hangi servise gönderileceği ve o servisin veri politikası **gösterilip onay istenir**.
3. Onay verilirse yedek sağlayıcı kullanılır; verilmezse gerçek hata gösterilir.

Aynı metin hiçbir zaman aynı anda birden fazla servise gönderilmez. Hiçbir sağlayıcı yoksa **sahte sonuç üretilmez**.

## CI'da canlı API testleri (isteğe bağlı)

Repo → **Settings → Secrets and variables → Actions** altına ekleyin:

| Secret | Açıklama |
|---|---|
| `GEMINI_API_KEY` | Gerçek Gemini analiz testi |
| `GROQ_API_KEY` | Gerçek Groq analizi + yedek geçiş testi |
| `ANDROID_KEYSTORE_BASE64`, `ANDROID_KEYSTORE_PASSWORD` | Kalıcı APK imzası (yoksa her derlemede yeni anahtar) |

Secret tanımlı değilse ilgili testler raporda **ATLANDI** olarak görünür; asla "geçti" sayılmaz.
