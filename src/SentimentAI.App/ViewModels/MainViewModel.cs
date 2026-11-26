using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SentimentAI.App.Charts;
using SentimentAI.App.Mvvm;
using SentimentAI.App.Services;
using SentimentAI.App.Views;
using SentimentAI.Core.Analysis;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Files;
using SentimentAI.Core.Models;
using SentimentAI.Core.Text;

namespace SentimentAI.App.ViewModels;

/// <summary>İroni/sarkazm/alay/mizah kartındaki tek satır.</summary>
public sealed record ToneItem(string Name, bool Detected, string BadgeText, Color BadgeColor, string ScoreText, string Note)
{
    public bool HasNote => Note.Length > 0;
}

/// <summary>
/// Ana ekranın tüm durumu. Analiz metni ve sonucu yalnızca bellekte tutulur:
/// yeni analizde, Temizle'de ve uygulama kapanırken silinir; hiçbir zaman diske yazılmaz.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private readonly AnalysisService _analysis;
    private readonly MauiNetworkStatus _network;
    private readonly IDialogService _dialogs;
    private readonly SessionCleaner _cleaner;
    private readonly IServiceProvider _services;
    private readonly ILogger<MainViewModel> _logger;

    private string _inputText = "";
    private bool _isBusy;
    private bool _isOnline;
    private UserError? _error;
    private string? _loadedFileInfo;
    private CancellationTokenSource? _cts;

    private AnalysisOutcome? _outcome;
    private IDrawable? _polarityChart, _emotionChart, _intensityChart, _wordCloud;
    private IReadOnlyList<ToneItem> _toneItems = [];

    public MainViewModel(
        AnalysisService analysis,
        MauiNetworkStatus network,
        IDialogService dialogs,
        SessionCleaner cleaner,
        IServiceProvider services,
        ILogger<MainViewModel> logger)
    {
        _analysis = analysis;
        _network = network;
        _dialogs = dialogs;
        _cleaner = cleaner;
        _services = services;
        _logger = logger;

        _isOnline = network.HasInternet;
        network.Changed += (_, online) => MainThread.BeginInvokeOnMainThread(() => IsOnline = online);

        AnalyzeCommand = new AsyncCommand(AnalyzeAsync, () => CanAnalyze);
        PickFileCommand = new AsyncCommand(PickFileAsync, () => !IsBusy);
        OpenSettingsCommand = new AsyncCommand(OpenSettingsAsync);
        ClearCommand = new Command(Clear, () => !IsBusy);
        UseCorrectedTextCommand = new Command(UseCorrectedText, () => CanUseCorrectedText && !IsBusy);
        DismissErrorCommand = new Command(() => Error = null);
    }

    // =================================================================== komutlar

    public AsyncCommand AnalyzeCommand { get; }
    public AsyncCommand PickFileCommand { get; }
    public AsyncCommand OpenSettingsCommand { get; }
    public Command ClearCommand { get; }
    public Command UseCorrectedTextCommand { get; }
    public Command DismissErrorCommand { get; }

    // =================================================================== giriş

    public string InputText
    {
        get => _inputText;
        set
        {
            if (!Set(ref _inputText, value ?? "")) return;
            OnPropertiesChanged(nameof(CharCount), nameof(CharCounterText), nameof(IsOverLimit), nameof(CharCounterColor), nameof(CanAnalyze), nameof(HasInput));
            RefreshCommands();
        }
    }

    public bool HasInput => _inputText.Length > 0;

    public int CharCount => TextValidator.CountChars(_inputText);

    public string CharCounterText => $"{CharCount:N0} / {AppLimits.MaxTextChars:N0}";

    public bool IsOverLimit => CharCount > AppLimits.MaxTextChars;

    public Color CharCounterColor => IsOverLimit ? Palette.Danger : CharCount > AppLimits.MaxTextChars * 0.9 ? Palette.Warning : Palette.TextMuted;

    public string LimitHint => $"En fazla {AppLimits.MaxTextChars:N0} karakter · her seferinde tek metin analiz edilir";

    public string Placeholder => "Analiz etmek istediğin Türkçe metni buraya yaz veya yapıştır…\n\nÖrn: “Sipariş bir günde geldi ama kutu ezikti, yine de ürün harika.”";

    public string? LoadedFileInfo
    {
        get => _loadedFileInfo;
        private set { if (Set(ref _loadedFileInfo, value)) OnPropertyChanged(nameof(HasLoadedFile)); }
    }

    public bool HasLoadedFile => !string.IsNullOrEmpty(_loadedFileInfo);

    public string SupportedFilesText => $"Desteklenen: {FileTextExtractor.SupportedListText} · en fazla {AppLimits.MaxFileBytes / (1024 * 1024)} MB";

    // =================================================================== durum

    public bool IsOnline
    {
        get => _isOnline;
        private set
        {
            if (!Set(ref _isOnline, value)) return;
            OnPropertiesChanged(nameof(ConnectionText), nameof(ConnectionColor), nameof(CanAnalyze), nameof(IsOffline));
            RefreshCommands();
        }
    }

    public bool IsOffline => !_isOnline;

    public string ConnectionText => _isOnline ? "Çevrimiçi" : "İnternet yok";

    public Color ConnectionColor => _isOnline ? Palette.Positive : Palette.Danger;

    public string ProviderText
    {
        get
        {
            var configured = _analysis.Providers.Where(p => p.IsConfigured).ToList();
            if (configured.Count == 0) return "Sağlayıcı yok";
            return configured.Count == 1 ? configured[0].DisplayName : $"{configured[0].DisplayName} + {configured.Count - 1} yedek";
        }
    }

    public bool NeedsApiKey => !_analysis.HasConfiguredProvider;

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!Set(ref _isBusy, value)) return;
            OnPropertiesChanged(nameof(CanAnalyze), nameof(AnalyzeButtonText), nameof(IsEmptyState));
            RefreshCommands();
        }
    }

    public bool CanAnalyze => !_isBusy && _isOnline && CharCount >= AppLimits.MinTextChars && !IsOverLimit;

    public string AnalyzeButtonText => _isBusy ? "Analiz ediliyor…" : "Analiz Et";

    public bool IsEmptyState => !_isBusy && _outcome is null && _error is null;

    // =================================================================== hata

    private UserError? Error
    {
        get => _error;
        set
        {
            _error = value;
            OnPropertiesChanged(nameof(HasError), nameof(ErrorTitle), nameof(ErrorReason), nameof(ErrorAction), nameof(IsEmptyState));
        }
    }

    public bool HasError => _error is not null;
    public string ErrorTitle => _error?.Title ?? "";
    public string ErrorReason => _error?.Reason ?? "";
    public string ErrorAction => _error?.Action ?? "";

    // =================================================================== sonuç

    private AnalysisResult? R => _outcome?.Result;

    public bool HasResult => _outcome is not null;

    public string DominantEmotion => R?.DominantEmotion ?? "";
    public string PolarityText => R is null ? "" : AnalysisResult.PolarityLabel(R.Polarity);
    public Color PolarityColor => R?.Polarity switch
    {
        Polarity.Positive => Palette.Positive,
        Polarity.Negative => Palette.Negative,
        _ => Palette.Neutral,
    };

    public string ConfidenceText => R is null ? "" : $"%{R.Confidence:0}";
    public bool IsLowConfidence => R?.IsLowConfidence ?? false;
    public string LowConfidenceText =>
        "Düşük güven: metin kısa, belirsiz veya karışık duygular içeriyor olabilir. Sonucu dikkatli yorumla.";

    public string IntensityText => R is null ? "" : AnalysisResult.IntensityLabel(R.Intensity);

    public IDrawable? PolarityChart => _polarityChart;
    public IDrawable? EmotionChart => _emotionChart;
    public IDrawable? IntensityChart => _intensityChart;
    public IDrawable? WordCloud => _wordCloud;
    public double EmotionChartHeight => EmotionBarsDrawable.DesiredHeight(R?.Emotions.Count ?? 0);

    public string Topic => R?.Topic ?? "";
    public string Purpose => R?.Purpose ?? "";
    public string Audience => R?.Audience ?? "";
    public string Summary => R?.Summary ?? "";

    public IReadOnlyList<KeywordWeight> Keywords => R?.Keywords ?? [];
    public bool HasKeywords => Keywords.Count > 0;

    public IReadOnlyList<ToneItem> ToneItems => _toneItems;

    public IReadOnlyList<SpellingIssue> SpellingIssues => R?.SpellingIssues ?? [];
    public bool HasSpellingIssues => SpellingIssues.Count > 0;
    public bool NoSpellingIssues => R is not null && SpellingIssues.Count == 0;
    public string SpellingSummary => HasSpellingIssues ? $"{SpellingIssues.Count} öneri bulundu" : "Yazım ve dilbilgisi hatası bulunamadı";

    public string CorrectedText => R?.CorrectedText ?? "";
    public bool HasCorrectedText =>
        R is not null && CorrectedText.Length > 0 &&
        !string.Equals(CorrectedText.Trim(), TextValidator.Normalize(_outcome!.Anonymization.Text), StringComparison.Ordinal);

    /// <summary>
    /// Düzeltilmiş metin, anonimleştirilmiş metin üzerinden üretilir. Maskelenmiş veri varsa
    /// kullanıcının orijinal bilgileri yer tutucularla değişmesin diye metne otomatik uygulanmaz.
    /// </summary>
    public bool CanUseCorrectedText => HasCorrectedText && !HasAnonymization;

    public IReadOnlyList<string> Improvements => R?.Improvements ?? [];
    public bool HasImprovements => Improvements.Count > 0;
    public IReadOnlyList<string> Repetitions => R?.Repetitions ?? [];
    public bool HasRepetitions => Repetitions.Count > 0;
    public string RepetitionsText => string.Join(" · ", Repetitions);

    public string ProviderUsedText => R is null ? "" :
        $"{R.ProviderName} · {R.ModelName}{(_outcome!.UsedFallback ? " · yedek sağlayıcı" : "")}";

    public bool HasAnonymization => _outcome?.Anonymization.Any ?? false;
    public string AnonymizationText => _outcome is null ? "" :
        $"Gönderilmeden önce cihazında maskelendi: {_outcome.Anonymization.Describe()}.";

    // =================================================================== işlemler

    private async Task AnalyzeAsync()
    {
        if (_isBusy) return; // çift tıklamaya karşı

        ClearResult();
        Error = null;
        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            var outcome = await _analysis.AnalyzeAsync(_inputText, ConfirmFallbackAsync, _cts.Token);
            ApplyOutcome(outcome);
        }
        catch (AnalysisException ex)
        {
            _logger.LogInformation("Analiz hatası: {Kind}", ex.Kind);
            Error = ErrorMessages.For(ex);
        }
        catch (OperationCanceledException)
        {
            // kullanıcı iptal etti / uygulama kapanıyor
        }
        catch (Exception ex)
        {
            _logger.LogError("Beklenmeyen analiz hatası: {Type}", ex.GetType().Name);
            Error = ErrorMessages.For(ErrorKind.AnalysisFailed);
        }
        finally
        {
            _cts?.Dispose();
            _cts = null;
            IsBusy = false;
            OnPropertyChanged(nameof(NeedsApiKey));
        }
    }

    private Task<bool> ConfirmFallbackAsync(FallbackRequest request)
    {
        var reason = ErrorMessages.For(request.Reason).Title;
        var message =
            $"{request.From.DisplayName} şu anda kullanılamıyor: {reason}.\n\n" +
            $"Metnin (anonimleştirilmiş hali) analiz için {request.To.DisplayName} hizmetine gönderilmesini onaylıyor musun?\n\n" +
            request.To.PrivacyNote;
        return _dialogs.ConfirmAsync("Yedek sağlayıcı kullanılsın mı?", message, "Gönder", "Vazgeç");
    }

    private void ApplyOutcome(AnalysisOutcome outcome)
    {
        var r = outcome.Result;
        _outcome = outcome;

        // Grafikler yalnızca burada, sonuç başına bir kez oluşturulur.
        _polarityChart = new DonutChartDrawable(r);
        _emotionChart = new EmotionBarsDrawable(r.Emotions);
        _intensityChart = new IntensityGaugeDrawable(r.IntensityScore, AnalysisResult.IntensityLabel(r.Intensity));
        _wordCloud = new WordCloudDrawable(r.Keywords);
        _toneItems = r.ToneFlags.Select(t => new ToneItem(
            t.Name,
            t.Detected,
            t.Detected ? "Olası" : "Belirgin değil",
            t.Detected ? Palette.Accent : Palette.Surface3,
            $"%{t.Score:0}",
            t.Note)).ToList();

        NotifyAllResultProperties();
    }

    private void ClearResult()
    {
        _outcome = null;
        _polarityChart = _emotionChart = _intensityChart = _wordCloud = null;
        _toneItems = [];
        NotifyAllResultProperties();
    }

    private void NotifyAllResultProperties()
    {
        OnPropertiesChanged(
            nameof(HasResult), nameof(IsEmptyState), nameof(DominantEmotion), nameof(PolarityText), nameof(PolarityColor),
            nameof(ConfidenceText), nameof(IsLowConfidence), nameof(IntensityText),
            nameof(PolarityChart), nameof(EmotionChart), nameof(IntensityChart), nameof(WordCloud), nameof(EmotionChartHeight),
            nameof(Topic), nameof(Purpose), nameof(Audience), nameof(Summary),
            nameof(Keywords), nameof(HasKeywords), nameof(ToneItems),
            nameof(SpellingIssues), nameof(HasSpellingIssues), nameof(NoSpellingIssues), nameof(SpellingSummary),
            nameof(CorrectedText), nameof(HasCorrectedText), nameof(CanUseCorrectedText),
            nameof(Improvements), nameof(HasImprovements), nameof(Repetitions), nameof(HasRepetitions), nameof(RepetitionsText),
            nameof(ProviderUsedText), nameof(HasAnonymization), nameof(AnonymizationText));
        RefreshCommands();
    }

    private void Clear()
    {
        _cts?.Cancel();
        InputText = "";
        LoadedFileInfo = null;
        Error = null;
        ClearResult();
    }

    /// <summary>Uygulama kapanırken bellekteki tüm metin ve sonuçları siler.</summary>
    public void ClearSession()
    {
        _cts?.Cancel();
        _inputText = "";
        _loadedFileInfo = null;
        _error = null;
        _outcome = null;
        _polarityChart = _emotionChart = _intensityChart = _wordCloud = null;
        _toneItems = [];
    }

    private void UseCorrectedText()
    {
        if (!CanUseCorrectedText) return;
        InputText = CorrectedText;
        LoadedFileInfo = null;
    }

    private async Task PickFileAsync()
    {
        Error = null;
        try
        {
            var picked = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = "Metin içeren bir dosya seç",
                FileTypes = SupportedFileTypes,
            });
            if (picked is null) return;

            ExtractedFile file;
            await using (var stream = await picked.OpenReadAsync())
            {
                file = await FileTextExtractor.ExtractAsync(stream, picked.FileName);
            }

            TextSegment? segment = file.Segments.Count == 1
                ? file.Segments[0]
                : await SegmentPickerPage.PickAsync(file, _services);

            if (segment is null) return;

            ClearResult();
            InputText = segment.Text;
            LoadedFileInfo = $"{file.FileName} · {segment.Label}";
        }
        catch (AnalysisException ex)
        {
            Error = ErrorMessages.For(ex);
        }
        catch (PermissionException)
        {
            Error = new UserError("Dosyaya erişilemedi", "Dosya seçmek için gereken izin verilmedi.", "İzni verip tekrar dene.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Dosya seçimi başarısız: {Type}", ex.GetType().Name);
            Error = ErrorMessages.For(ErrorKind.FileReadFailed);
        }
        finally
        {
            // Dosya seçicinin önbelleğe kopyaladığı dosya varsa hemen silinir.
            _cleaner.Clean("dosya okundu");
        }
    }

    private async Task OpenSettingsAsync()
    {
        var nav = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (nav is null) return;
        await nav.PushAsync(_services.GetRequiredService<SettingsPage>());
    }

    /// <summary>Ayarlar ekranından dönünce sağlayıcı bilgisini yeniler.</summary>
    public void RefreshProviderInfo()
    {
        IsOnline = _network.HasInternet;
        OnPropertiesChanged(nameof(ProviderText), nameof(NeedsApiKey));
    }

    private void RefreshCommands()
    {
        AnalyzeCommand.RaiseCanExecuteChanged();
        PickFileCommand.RaiseCanExecuteChanged();
        ClearCommand.ChangeCanExecute();
        UseCorrectedTextCommand.ChangeCanExecute();
    }

    private static readonly FilePickerFileType SupportedFileTypes = new(new Dictionary<DevicePlatform, IEnumerable<string>>
    {
        [DevicePlatform.Android] =
        [
            "text/*",
            "application/pdf",
            "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
            "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            "application/csv",
            "application/vnd.ms-excel",
        ],
        [DevicePlatform.WinUI] = [".txt", ".pdf", ".docx", ".csv", ".xlsx"],
    });
}

/// <summary>Kod tarafında kullanılan tema renkleri (Colors.xaml ile aynı).</summary>
public static class Palette
{
    public static readonly Color Positive = Color.FromArgb("#34D399");
    public static readonly Color Negative = Color.FromArgb("#FF4D8D");
    public static readonly Color Neutral = Color.FromArgb("#8E8EA8");
    public static readonly Color Warning = Color.FromArgb("#FBBF24");
    public static readonly Color Danger = Color.FromArgb("#F87171");
    public static readonly Color TextMuted = Color.FromArgb("#7C7C92");
    public static readonly Color Accent = Color.FromArgb("#FF2E97");
    public static readonly Color Surface3 = Color.FromArgb("#2A2A3A");
}
