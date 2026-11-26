using Microsoft.Extensions.Logging;
using SentimentAI.App.Mvvm;
using SentimentAI.App.Services;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Providers;

namespace SentimentAI.App.ViewModels;

/// <summary>Tek bir sağlayıcının ayar satırı: anahtar girişi, durum, test ve silme.</summary>
public sealed class ProviderItemViewModel : ObservableObject
{
    private readonly IAIProvider _provider;
    private readonly IApiKeyStore _keys;
    private readonly MauiNetworkStatus _network;
    private readonly IDialogService _dialogs;
    private readonly ILogger _logger;

    private string _keyInput = "";
    private string _statusText = "";
    private Color _statusColor = Palette.TextMuted;
    private bool _isChecking;

    public ProviderItemViewModel(IAIProvider provider, bool isPrimary, IApiKeyStore keys, MauiNetworkStatus network, IDialogService dialogs, ILogger logger)
    {
        _provider = provider;
        _keys = keys;
        _network = network;
        _dialogs = dialogs;
        _logger = logger;
        IsPrimary = isPrimary;

        SaveCommand = new AsyncCommand(SaveAsync, () => !_isChecking && _keyInput.Trim().Length >= 10);
        TestCommand = new AsyncCommand(TestAsync, () => !_isChecking && _provider.IsConfigured);
        RemoveCommand = new AsyncCommand(RemoveAsync, () => !_isChecking && _provider.IsConfigured);
        GetKeyCommand = new AsyncCommand(() => Launcher.Default.OpenAsync(new Uri(_provider.KeyHelpUrl)));

        SetStatusFromConfig();
    }

    public AsyncCommand SaveCommand { get; }
    public AsyncCommand TestCommand { get; }
    public AsyncCommand RemoveCommand { get; }
    public AsyncCommand GetKeyCommand { get; }

    public string Name => _provider.DisplayName;
    public string RoleText => IsPrimary ? "Ana sağlayıcı" : "Yedek sağlayıcı";
    public bool IsPrimary { get; }
    public string ModelText => $"Model: {_provider.ModelName}";
    public string PrivacyNote => _provider.PrivacyNote;
    public bool IsConfigured => _provider.IsConfigured;
    public string KeyPlaceholder => IsConfigured ? "Kayıtlı anahtar var · değiştirmek için yenisini yapıştır" : "API anahtarını buraya yapıştır";

    public string KeyInput
    {
        get => _keyInput;
        set { if (Set(ref _keyInput, value ?? "")) SaveCommand.RaiseCanExecuteChanged(); }
    }

    public string StatusText { get => _statusText; private set => Set(ref _statusText, value); }
    public Color StatusColor { get => _statusColor; private set => Set(ref _statusColor, value); }

    public bool IsChecking
    {
        get => _isChecking;
        private set
        {
            if (!Set(ref _isChecking, value)) return;
            SaveCommand.RaiseCanExecuteChanged();
            TestCommand.RaiseCanExecuteChanged();
            RemoveCommand.RaiseCanExecuteChanged();
        }
    }

    private void SetStatusFromConfig()
    {
        StatusText = IsConfigured ? "Anahtar kayıtlı · durumu görmek için Test et" : "Anahtar girilmemiş";
        StatusColor = IsConfigured ? Palette.Warning : Palette.TextMuted;
    }

    private async Task SaveAsync()
    {
        var key = _keyInput.Trim();
        if (key.Length < 10) return;

        try
        {
            await _keys.SetAsync(_provider.Id, key);
            KeyInput = ""; // anahtar ekranda tutulmaz
            OnPropertiesChanged(nameof(IsConfigured), nameof(KeyPlaceholder));
            TestCommand.RaiseCanExecuteChanged();
            RemoveCommand.RaiseCanExecuteChanged();
            await TestAsync();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Anahtar kaydedilemedi: {Type}", ex.GetType().Name);
            await _dialogs.AlertAsync("Anahtar kaydedilemedi", "Cihazın güvenli deposuna yazılamadı. Tekrar dene.");
        }
    }

    public async Task TestAsync()
    {
        if (!IsConfigured) { SetStatusFromConfig(); return; }
        if (!_network.HasInternet)
        {
            StatusText = "İnternet yok · durum kontrol edilemedi";
            StatusColor = Palette.Danger;
            return;
        }

        IsChecking = true;
        StatusText = "Kontrol ediliyor…";
        StatusColor = Palette.TextMuted;
        try
        {
            var status = await _provider.GetProviderStatusAsync(CancellationToken.None);
            StatusText = status.State switch
            {
                ProviderState.Ready => "Hazır · anahtar geçerli",
                ProviderState.InvalidKey => "Anahtar geçersiz",
                ProviderState.QuotaExceeded => "Kota doldu · daha sonra tekrar dene",
                ProviderState.Unreachable => "Servise ulaşılamıyor",
                ProviderState.NotConfigured => "Anahtar girilmemiş",
                _ => status.Message,
            };
            StatusColor = status.State switch
            {
                ProviderState.Ready => Palette.Positive,
                ProviderState.QuotaExceeded or ProviderState.Unreachable => Palette.Warning,
                ProviderState.NotConfigured => Palette.TextMuted,
                _ => Palette.Danger,
            };
        }
        finally
        {
            IsChecking = false;
        }
    }

    private async Task RemoveAsync()
    {
        var ok = await _dialogs.ConfirmAsync("Anahtar silinsin mi?", $"{Name} API anahtarı bu cihazdan silinecek.", "Sil", "Vazgeç");
        if (!ok) return;
        await _keys.RemoveAsync(_provider.Id);
        OnPropertiesChanged(nameof(IsConfigured), nameof(KeyPlaceholder));
        TestCommand.RaiseCanExecuteChanged();
        RemoveCommand.RaiseCanExecuteChanged();
        SetStatusFromConfig();
    }
}

/// <summary>Ayarlar ekranı: sağlayıcı durumu, API yapılandırması, bağlantı, gizlilik, sürüm ve hakkında.</summary>
public sealed class SettingsViewModel : ObservableObject
{
    private readonly MauiNetworkStatus _network;

    public SettingsViewModel(AnalysisService analysis, IApiKeyStore keys, MauiNetworkStatus network, IDialogService dialogs, ILogger<SettingsViewModel> logger)
    {
        _network = network;
        Providers = analysis.Providers
            .Select((p, i) => new ProviderItemViewModel(p, i == 0, keys, network, dialogs, logger))
            .ToList();
    }

    public IReadOnlyList<ProviderItemViewModel> Providers { get; }

    public string ConnectionText => _network.HasInternet ? "Çevrimiçi · analiz yapılabilir" : "İnternet bağlantısı yok · analiz yapılamaz";
    public Color ConnectionColor => _network.HasInternet ? Palette.Positive : Palette.Danger;

    public string VersionText => $"Sürüm {AppInfo.Current.VersionString} ({AppInfo.Current.BuildString}) · {DeviceInfo.Current.Platform}";

    public string LimitsText =>
        $"Tek analizde en fazla {AppLimits.MaxTextChars:N0} karakter · dosya en fazla {AppLimits.MaxFileBytes / (1024 * 1024)} MB · istek zaman aşımı {AppLimits.RequestTimeout.TotalSeconds:0} sn";

    public string FallbackText =>
        "Ana sağlayıcı kota, anahtar veya erişim hatası verirse ve yedek sağlayıcı için de anahtar girdiysen, " +
        "metin yedek sağlayıcıya gönderilmeden önce her seferinde onayın istenir. Aynı metin asla aynı anda iki servise gönderilmez.";

    public string PrivacyText =>
        "• Analiz için metin, seçtiğin yapay zekâ sağlayıcısına (üçüncü taraf) internet üzerinden gönderilir.\n" +
        "• Göndermeden önce cihazında otomatik anonimleştirme yapılır: e-posta, telefon, T.C. kimlik no, IBAN, kart numarası, açık adres kalıpları ve kişisel profil bağlantıları maskelenir.\n" +
        "• Otomatik anonimleştirme her kişisel bilgiyi yakalayamaz (ör. isimler, serbest yazılmış adresler). Hassas bilgileri metinden kendin çıkar.\n" +
        "• Analiz geçmişi tutulmaz. Metinler ve sonuçlar yalnızca bellekte tutulur, diske yazılmaz; yeni analizde, Temizle'de ve uygulama kapanınca silinir.\n" +
        "• Dosyalar cihazında okunur; yalnızca seçtiğin kısa metin parçası gönderilir. Geçici dosyalar işlemden sonra temizlenir.\n" +
        "• Metin içerikleri ve API istek gövdeleri hiçbir loga yazılmaz. Dışa aktarma ve toplu analiz yoktur.\n" +
        "• API anahtarların cihazın güvenli deposunda (Android Keystore / Windows DPAPI) saklanır.\n" +
        "• Google Gemini ücretsiz katmanında gönderilen içerik Google ürünlerini geliştirmek için kullanılabilir. Groq, müşteri verisini model eğitiminde kullanmadığını belirtir. Sağlayıcıların güncel koşullarını kendi sitelerinden kontrol et.\n" +
        "• Güven yüzdesi, modelin tahmini güvenidir; istatistiksel olarak kalibre edilmiş bir kesinlik değildir. Hedef kitle, amaç ve ironi tespitleri tahmindir.";

    public string AboutText => "SENTIMENT AI, Türkçe metinlerde duygu, anlam, ironi ve yazım analizini yapay zekâ ile yapan hafif ve hızlı bir araçtır.";

    public async Task RefreshAsync()
    {
        OnPropertiesChanged(nameof(ConnectionText), nameof(ConnectionColor));
        foreach (var p in Providers)
            if (p.IsConfigured) await p.TestAsync();
    }
}
