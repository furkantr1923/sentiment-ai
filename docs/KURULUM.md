# 💿 Kurulum Rehberi

## Windows 10 / 11

**Seçenek A — Setup.exe (önerilen)**
1. `SENTIMENT-AI-Setup.exe` dosyasını çalıştırın.
2. SmartScreen uyarısı çıkarsa *Ek bilgi → Yine de çalıştır* (dosya kod imzalı değildir).
3. Kurulum varsayılan olarak yalnızca sizin kullanıcınız için yapılır; yönetici izni gerekmez.
4. Kaldırma: *Ayarlar → Uygulamalar → SENTIMENT AI → Kaldır*.

**Seçenek B — Portable.exe**
Kurulum yok. `SENTIMENT-AI-Portable.exe` dosyasını çift tıklayın. İlk açılışta uygulama `%LOCALAPPDATA%\SENTIMENT-AI\Portable\` altına bir kez açılır (birkaç saniye), sonraki açılışlar anında olur.

**Seçenek C — Klasör (zip)**
`SENTIMENT-AI-Windows-x64.zip` arşivini açın ve `SENTIMENT-AI\SentimentAI.exe` dosyasını çalıştırın.

Gereksinimler: Windows 10 1809+ (x64). .NET veya Windows App SDK ayrıca kurulmaz; uygulama bağımsız (self-contained) paketlenmiştir.

## Android

1. `SENTIMENT-AI.apk` dosyasını telefona indirin.
2. *Bilinmeyen kaynaklardan yükleme* iznini ilgili uygulama (Dosyalar/Chrome) için açın.
3. APK'yı açıp **Yükle**'ye dokunun.

Gereksinimler: Android 7.0 (API 24) ve üzeri, arm64 veya arm işlemci.

> CI'da kalıcı imza anahtarı (secret) tanımlı değilse her sürüm farklı anahtarla imzalanır; yeni sürümü kurmadan önce eskisini kaldırmanız gerekir.

## Geliştiriciler için derleme

```bash
# Gerekenler: .NET 10 SDK, (Android için) JDK 17 + Android SDK
dotnet workload install maui

# Testler
dotnet test tests/SentimentAI.Core.Tests

# Android APK
dotnet publish src/SentimentAI.App -f net10.0-android -c Release

# Windows (Windows'ta)
dotnet publish src/SentimentAI.App -c Release -f net10.0-windows10.0.19041.0 \
  -p:RuntimeIdentifierOverride=win-x64 -p:WindowsPackageType=None \
  -p:WindowsAppSDKSelfContained=true -p:SelfContained=true -o out/win
iscc /DSourceDir=%CD%\out\win installer\SentimentAI.iss
```

Otomatik derleme: `.github/workflows/build.yml` her `main` gönderiminde testleri çalıştırır, Setup/Portable/zip/APK üretir, Windows ve Android emülatörde duman testleri yapar ve hepsini **GitHub Release**'e ekler.
