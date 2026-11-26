using SentimentAI.App.ViewModels;

namespace SentimentAI.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _vm;
    private bool _refreshed;

    public SettingsPage(SettingsViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        if (_refreshed) return;
        _refreshed = true;
        try
        {
            await _vm.RefreshAsync();
        }
        catch (Exception)
        {
            // Durum kontrolü başarısız olsa da ayarlar kullanılabilir kalır.
        }
    }
}
