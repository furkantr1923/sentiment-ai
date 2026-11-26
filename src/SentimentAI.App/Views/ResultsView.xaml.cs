namespace SentimentAI.App.Views;

/// <summary>Analiz sonuç kartları. Geniş ekranda grafikler yan yana, dar ekranda alt alta dizilir.</summary>
public partial class ResultsView : ContentView
{
    private const double WideBreakpoint = 620;
    private bool? _isWide;

    public ResultsView()
    {
        InitializeComponent();
        SizeChanged += (_, _) => ApplyLayout();
    }

    private void ApplyLayout()
    {
        if (Width <= 0) return;
        bool wide = Width >= WideBreakpoint;
        if (_isWide == wide) return;
        _isWide = wide;

        ChartsGrid.ColumnDefinitions.Clear();
        ChartsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));
        if (wide) ChartsGrid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Star));

        Grid.SetRow(PolarityCard, 0);
        Grid.SetColumn(PolarityCard, 0);
        Grid.SetRow(IntensityCard, wide ? 0 : 1);
        Grid.SetColumn(IntensityCard, wide ? 1 : 0);
    }
}
