using Microsoft.Maui.Controls.Shapes;

namespace SentimentAI.App.Controls;

/// <summary>
/// Belirsiz süreli (indeterminate) mor/neon pembe ilerleme çubuğu.
/// Gerçek ilerleme bilgisi olmadığı için sahte yüzde göstermez; yalnızca işlem sürerken çalışır
/// ve işlem bitince (başarı ya da hata) animasyon tamamen durdurulur.
/// </summary>
public sealed class NeonProgressBar : ContentView
{
    private const string AnimationName = "NeonSweep";

    public static readonly BindableProperty IsRunningProperty = BindableProperty.Create(
        nameof(IsRunning), typeof(bool), typeof(NeonProgressBar), false,
        propertyChanged: (b, _, n) => ((NeonProgressBar)b).Update((bool)n));

    private readonly BoxView _glow;

    public NeonProgressBar()
    {
        HeightRequest = 6;
        IsVisible = false;

        _glow = new BoxView
        {
            HorizontalOptions = LayoutOptions.Start,
            VerticalOptions = LayoutOptions.Fill,
            WidthRequest = 120,
            CornerRadius = 3,
            Background = new LinearGradientBrush(
                [
                    new GradientStop(Color.FromArgb("#008B5CF6"), 0f),
                    new GradientStop(Color.FromArgb("#8B5CF6"), 0.35f),
                    new GradientStop(Color.FromArgb("#FF2E97"), 0.7f),
                    new GradientStop(Color.FromArgb("#00FF2E97"), 1f),
                ],
                new Point(0, 0.5), new Point(1, 0.5)),
        };

        Content = new Border
        {
            StrokeThickness = 0,
            BackgroundColor = Color.FromArgb("#1D1D29"),
            StrokeShape = new RoundRectangle { CornerRadius = 3 },
            Content = new Grid { Children = { _glow } },
        };

        SizeChanged += (_, _) => _glow.WidthRequest = Math.Max(60, Width * 0.35);
    }

    public bool IsRunning
    {
        get => (bool)GetValue(IsRunningProperty);
        set => SetValue(IsRunningProperty, value);
    }

    private void Update(bool running)
    {
        IsVisible = running;
        this.AbortAnimation(AnimationName);
        if (!running)
        {
            _glow.TranslationX = 0;
            return;
        }

        var sweep = new Animation(v =>
        {
            var glowWidth = _glow.Width > 0 ? _glow.Width : _glow.WidthRequest;
            _glow.TranslationX = -glowWidth + v * (Width + glowWidth);
        }, 0, 1, Easing.SinInOut);

        sweep.Commit(this, AnimationName, rate: 16, length: 1300, repeat: () => IsRunning);
    }
}
