using SentimentAI.App.ViewModels;

namespace SentimentAI.App.Views;

public partial class DesktopMainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public DesktopMainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshProviderInfo();
    }
}
