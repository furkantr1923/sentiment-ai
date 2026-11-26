using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SentimentAI.App.Services;
using SentimentAI.App.ViewModels;
using SentimentAI.App.Views;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Providers;

namespace SentimentAI.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder.UseMauiApp<App>();

#if DEBUG
        builder.Logging.AddDebug();
        builder.Logging.SetMinimumLevel(LogLevel.Debug);
#else
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
#endif

        var services = builder.Services;

        // --- Yönetilen HTTP istemcileri (HttpClientFactory)
        foreach (var name in new[] { "provider-" + GeminiProvider.ProviderId, "provider-" + GroqProvider.ProviderId })
        {
            services.AddHttpClient(name, c =>
                {
                    c.Timeout = Timeout.InfiniteTimeSpan; // zaman aşımı sağlayıcı katmanında yönetilir
                    c.DefaultRequestHeaders.UserAgent.ParseAdd("SentimentAI/1.0");
                });
            // Birincil işleyici platformun yerel HTTP yığınıdır (Android: AndroidMessageHandler, Windows: SocketsHttpHandler).
        }

        // --- Çekirdek servisler
        services.AddSingleton<IApiKeyStore, SecureApiKeyStore>();
        services.AddSingleton<MauiNetworkStatus>();
        services.AddSingleton<INetworkStatus>(sp => sp.GetRequiredService<MauiNetworkStatus>());
        services.AddSingleton<IDialogService, DialogService>();
        services.AddSingleton<SessionCleaner>();

        // --- Sağlayıcılar (kayıt sırası = öncelik: Gemini ana, Groq yedek)
        services.AddSingleton<GeminiProvider>();
        services.AddSingleton<GroqProvider>();
        services.AddSingleton<IAIProvider>(sp => sp.GetRequiredService<GeminiProvider>());
        services.AddSingleton<IAIProvider>(sp => sp.GetRequiredService<GroqProvider>());
        services.AddSingleton(sp => new AnalysisService(
            sp.GetServices<IAIProvider>(),
            sp.GetRequiredService<INetworkStatus>(),
            sp.GetRequiredService<ILogger<AnalysisService>>()));

        // --- MVVM
        services.AddSingleton<MainViewModel>();
        services.AddTransient<SettingsViewModel>();
        services.AddSingleton<DesktopMainPage>();
        services.AddSingleton<MobileMainPage>();
        services.AddTransient<SettingsPage>();

        var app = builder.Build();

        // API anahtarlarını güvenli depodan belleğe al (ilk ekran açılmadan önce).
        try
        {
            // Arka plan iş parçacığında çalıştırılır: ana iş parçacığında bekleyip kilitlenmeyi (deadlock) önler.
            var store = app.Services.GetRequiredService<IApiKeyStore>();
            Task.Run(() => store.LoadAsync()).GetAwaiter().GetResult();
        }
        catch (Exception)
        {
            // Depo okunamazsa kullanıcı anahtarı Ayarlar'dan yeniden girer.
        }

        return app;
    }
}
