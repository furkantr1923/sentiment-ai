using Microsoft.Extensions.DependencyInjection;
using SentimentAI.App.Services;
using SentimentAI.App.ViewModels;
using SentimentAI.App.Views;

namespace SentimentAI.App;

public partial class App : Application
{
    private readonly IServiceProvider _services;

    public App(IServiceProvider services)
    {
        InitializeComponent();
        _services = services;
        UserAppTheme = AppTheme.Dark; // tasarım tamamen koyu temaya göre yapıldı
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var cleaner = _services.GetRequiredService<SessionCleaner>();
        cleaner.Clean("açılış"); // önceki oturumdan kalmış olabilecek geçici dosyalar
        cleaner.WriteSelfTestMarkerIfRequested();

        // Platforma özgü ayrı arayüz düzenleri, ortak ViewModel
        Page root = DeviceInfo.Current.Idiom == DeviceIdiom.Desktop
            ? _services.GetRequiredService<DesktopMainPage>()
            : _services.GetRequiredService<MobileMainPage>();

        var window = new Window(new NavigationPage(root))
        {
            Title = "SENTIMENT AI",
        };

        if (DeviceInfo.Current.Idiom == DeviceIdiom.Desktop)
        {
            window.MinimumWidth = 980;
            window.MinimumHeight = 640;
            window.Width = 1320;
            window.Height = 860;
        }

        // Uygulama kapanınca bellekteki metin/sonuçlar ve geçici dosyalar silinir.
        window.Destroying += (_, _) =>
        {
            _services.GetRequiredService<MainViewModel>().ClearSession();
            cleaner.Clean("kapanış");
        };

        return window;
    }
}
