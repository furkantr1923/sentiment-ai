using System.ComponentModel;
using SentimentAI.App.ViewModels;

namespace SentimentAI.App.Views;

public partial class MobileMainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public MobileMainPage(MainViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
        vm.PropertyChanged += OnViewModelChanged;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _vm.RefreshProviderInfo();
    }

    private async void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
    {
        try
        {
            // Sonuç gelince klavyeyi kapat ve sonuçlara kaydır
            if (e.PropertyName == nameof(MainViewModel.HasResult) && _vm.HasResult)
            {
                InputEditor.Unfocus();
                await Task.Delay(120);
                await Scroller.ScrollToAsync(Results, ScrollToPosition.Start, true);
            }
            else if (e.PropertyName == nameof(MainViewModel.HasError) && _vm.HasError)
            {
                InputEditor.Unfocus();
            }
        }
        catch (Exception)
        {
            // Kaydırma yalnızca kolaylıktır; başarısız olması akışı etkilemez.
        }
    }
}
