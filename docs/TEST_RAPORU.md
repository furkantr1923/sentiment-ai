# 🧪 Test Raporu — SENTIMENT AI v1.0.0

Tüm testler **GitHub Actions** üzerinde, her `main` gönderiminde otomatik çalışır. Ham çıktılar her sürümün `test-raporlari.zip` dosyasındadır.

## Özet

| Test grubu | Ortam | Sonuç |
|---|---|---|
| Birim + entegrasyon (sahte HTTP sunucusu) | Ubuntu, .NET 10 | ✅ **94 / 94 geçti** |
| Canlı API (gerçek sunucular) | Ubuntu | ✅ 6 geçti (2'si anahtarsız gerçek analiz) · ⏭️ 4 atlandı (Gemini/Groq anahtar secret'ı yok) · ❌ 0 |
| Windows duman testleri | Windows Server 2025 VM | ✅ **10 / 10 geçti** |
| Android emülatör testleri | API 34, x86_64 | ✅ **6 / 6 geçti** |

## Birim ve entegrasyon testleri (94)

| Konu | Kapsam |
|---|---|
| Metin doğrulama | Boş/harfsiz/kısa metin, 2.000 karakter sınırı, normalleştirme |
| Anonimleştirme | E-posta, 5 telefon biçimi, T.C. kimlik no (geçerli/geçersiz), IBAN, kart no (Luhn), 3 adres kalıbı, yanlış pozitif kontrolü |
| Yanıt ayrıştırma | Geçerli yanıt, markdown içinde JSON, bozuk/kesilmiş yanıt, zorunlu alan eksikliği (uydurma yok), 0–1 ölçeği, etiket eşleme |
| Sağlayıcı HTTP | Gemini ve Groq istek gövdesi, anahtarın URL'de taşınmaması, geçersiz anahtar (400/401), kota (429, yeniden deneme yok), model 404 → yedek model, zaman aşımı, kullanıcı iptali, ağ kesilmesi, bozuk JSON, güvenlik filtresi, durum kontrolü, HTTP kod sınıflandırma |
| Sağlayıcı geçişi | Ana başarılıysa yedeğe gidilmez, anonim metin gönderimi, onaylı geçiş, onaysız geçiş yok, sınırlı yeniden deneme, sonsuz deneme yok, içerik hatasında geçiş yok, internetsiz istek yok, istek sırasında bağlantı kopması, anahtar yok |
| Dosya okuma | TXT (UTF-8, Windows-1254), uzun metin bölme, CSV (ayırıcı, tırnak, çok satırlı alan), DOCX, XLSX (paylaşılan + satır içi metin), PDF, desteklenmeyen tür, bozuk/şifreli dosya, boyut sınırı, boş dosya |
| Geçici veri | Önbellek temizliği, olmayan klasör |

## Canlı API testleri (gerçek sunucular)

| Test | Sonuç |
|---|---|
| **Pollinations (anahtarsız): gerçek Türkçe analiz** | ✅ Pozitif · Mutluluk · güven %80 · 11 duygu · 5 anahtar kelime |
| **Pollinations (anahtarsız): ironi örneği** | ✅ istek başarılı; ⚠️ ironik cümlede ironi tespit edilmedi (ücretsiz modelin kalitesi sınırlı) |
| Gemini: geçersiz API anahtarı | ✅ HTTP 400 → `InvalidApiKey` |
| Groq: geçersiz API anahtarı | ✅ HTTP 401 → `InvalidApiKey` |
| Durum kontrolü geçersiz anahtarı raporlar | ✅ |
| Zaman aşımı | ✅ `Timeout` |
| Gemini / Groq gerçek Türkçe analiz, ironi, yedek geçiş | ⏭️ **Atlandı**: `GEMINI_API_KEY` / `GROQ_API_KEY` secret'ları tanımlı değil |

> Gerçek analiz testlerini çalıştırmak için repo secret'larına anahtar ekleyin ([API rehberi](API_YAPILANDIRMA.md)). Atlanan testler hiçbir zaman "geçti" sayılmaz.

## Windows testleri

SentimentAI.exe üretildi · Setup.exe üretildi · Portable.exe üretildi · uygulama açılıyor · açılışta geçici dosya oluştu · **kapanışta geçici veriler temizlendi** · **Setup.exe sessiz kurulum** · kurulan uygulama açılıyor · **kaldırma** · **Portable EXE kurulumsuz açılıyor**.

## Android testleri (emülatör)

APK kuruluyor · uygulama açılıyor · ana ekran görünüyor · Analiz Et butonu görünüyor · çökme yok · **klavye açıkken Analiz Et butonu ekranda**.

APK: `com.furkan.sentimentai` 1.0.0 · minSdk 24 · targetSdk 34 · arm64-v8a + armeabi-v7a · APK İmza Şeması v3.

## Test edilmeyenler (açıkça)

- ❗ **Gerçek fiziksel Android cihazda** test yapılmadı (yalnızca emülatör).
- ❗ **Kişisel Windows bilgisayarda** test yapılmadı (yalnızca GitHub Actions sanal makinesi).
- ❗ Arayüz üzerinden uçtan uca gerçek analiz (anahtar girip metin analiz etme) otomatik test edilmedi; analiz akışı Core katmanında sahte sunucu ve gerçek sunucu testleriyle doğrulandı.
- ❗ Gemini/Groq ile gerçek analiz, CI'da anahtar secret'ı olmadığı için çalıştırılmadı (anahtarsız Pollinations ile gerçek analiz çalıştı).
- ⚠️ Anahtarsız ücretsiz modelin ironi/sarkazm tespiti zayıf kaldı; daha iyi sonuç için Gemini anahtarı önerilir.
