using SentimentAI.Core.Files;

namespace SentimentAI.App.Views;

/// <summary>Dosyadan çıkarılan parçalardan birini seçtiren modal sayfa.</summary>
public partial class SegmentPickerPage : ContentPage
{
    private readonly ExtractedFile _file;
    private readonly TaskCompletionSource<TextSegment?> _result = new();
    private bool _closing;

    private SegmentPickerPage(ExtractedFile file)
    {
        InitializeComponent();
        _file = file;
        FileInfoLabel.Text = $"{file.FileName} · {file.KindLabel} · {file.Segments.Count:N0} parça";
        TruncatedLabel.IsVisible = file.Truncated;
        SegmentList.ItemsSource = file.Segments;
    }

    /// <summary>Sayfayı açar ve kullanıcının seçtiği parçayı (veya vazgeçtiyse null) döndürür.</summary>
    public static async Task<TextSegment?> PickAsync(ExtractedFile file, IServiceProvider services)
    {
        _ = services;
        var navigation = Application.Current?.Windows.FirstOrDefault()?.Page?.Navigation;
        if (navigation is null) return null;

        var page = new SegmentPickerPage(file);
        await navigation.PushModalAsync(page);
        return await page._result.Task;
    }

    private async void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.CurrentSelection.FirstOrDefault() is TextSegment segment)
            await CloseAsync(segment);
    }

    private async void OnCancelClicked(object? sender, EventArgs e) => await CloseAsync(null);

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        var q = (e.NewTextValue ?? "").Trim();
        SegmentList.ItemsSource = q.Length == 0
            ? _file.Segments
            : _file.Segments.Where(s => s.Text.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                     || s.Label.Contains(q, StringComparison.CurrentCultureIgnoreCase)).ToList();
    }

    private async Task CloseAsync(TextSegment? segment)
    {
        if (_closing) return;
        _closing = true;
        _result.TrySetResult(segment);
        try
        {
            await Navigation.PopModalAsync();
        }
        catch (Exception)
        {
            // Modal zaten kapanmış olabilir.
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _result.TrySetResult(null); // geri tuşu ile kapatıldıysa
    }

    protected override bool OnBackButtonPressed()
    {
        _ = CloseAsync(null);
        return true;
    }
}
