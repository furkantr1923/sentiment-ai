# ⚠️ Bilinen Sınırlamalar

1. **İnternet ve API anahtarı gerekir.** Çevrimdışı analiz yoktur; kullanıcı kendi ücretsiz Gemini veya Groq anahtarını girmelidir.
2. **Ücretsiz kotalar değişebilir.** Sağlayıcılar ücretsiz katman sınırlarını veya modelleri önceden haber vermeden değiştirebilir. Uygulama "-latest" model takma adları ve model yedeği ile buna dayanıklı olacak şekilde tasarlandı, ancak garanti verilemez.
3. **Sonuçlar tahmindir.** Güven yüzdesi modelin kendi tahminidir, kalibre edilmiş istatistiksel kesinlik değildir. Hedef kitle, amaç, ironi/sarkazm tespitleri yorumdur.
4. **Anonimleştirme kusursuz değildir.** İsimler, serbest biçimli adresler ve bağlamdan anlaşılan kişisel bilgiler yakalanamayabilir.
5. **Gemini ücretsiz katmanı veri kullanımı.** Ücretsiz katmanda gönderilen içerik Google tarafından ürün geliştirme amacıyla kullanılabilir.
6. **Taranmış PDF'ler.** Görüntü tabanlı PDF'lerden metin çıkarılamaz (OCR yoktur).
7. **Eski Office biçimleri.** `.doc` / `.xls` ve parola korumalı belgeler desteklenmez (anlaşılır hata verilir).
8. **Tek metin, kısa metin.** Bilinçli tasarım: tek seferde en fazla 2.000 karakter; toplu analiz ve dışa aktarma yoktur.
9. **Kod imzası.** Windows dosyaları kod imzalı değildir; SmartScreen uyarısı görülebilir. APK, kalıcı anahtar secret'ı tanımlı değilse her derlemede farklı anahtarla imzalanır.
10. **Test kapsamı.** Otomatik testler GitHub Actions'ta (Windows Server sanal makinesi ve Android emülatörü) yapılmıştır; **gerçek fiziksel Android cihazda ve kişisel Windows bilgisayarda test edilmemiştir**. Gerçek API analiz testleri yalnızca CI'da API anahtarı secret'ı tanımlıyken çalışır.
11. **Android hedef SDK.** Klavye davranışını korumak için `targetSdkVersion 34` kullanılır; Google Play'e yüklemek için 35+ gerekir (uygulama doğrudan APK olarak dağıtılır).
