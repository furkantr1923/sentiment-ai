using SentimentAI.Core.Models;
using Font = Microsoft.Maui.Graphics.Font;

namespace SentimentAI.App.Charts;

/// <summary>
/// Hafif, bağımlılıksız grafikler (Microsoft.Maui.Graphics). Her grafik yalnızca yeni bir analiz
/// geldiğinde bir kez oluşturulur; sürekli çalışan animasyon veya yeniden hesaplama yoktur.
/// Tüm değerler o anki analiz sonucundan gelir.
/// </summary>
internal static class ChartPalette
{
    public static readonly Color Text = Color.FromArgb("#F4F4F8");
    public static readonly Color TextMuted = Color.FromArgb("#8A8AA2");
    public static readonly Color Track = Color.FromArgb("#22222F");
    public static readonly Color Purple = Color.FromArgb("#8B5CF6");
    public static readonly Color Pink = Color.FromArgb("#FF2E97");
    public static readonly Color Positive = Color.FromArgb("#34D399");
    public static readonly Color Negative = Color.FromArgb("#FF4D8D");
    public static readonly Color Neutral = Color.FromArgb("#8E8EA8");

    /// <summary>
    /// Ekran koordinatlarında yay çizer: 0° = sağ, pozitif açı = saat yönü.
    /// (Platformlar arası açı yönü farklarından etkilenmemek için yay noktaları elle hesaplanır.)
    /// </summary>
    public static void StrokeArc(ICanvas canvas, float cx, float cy, float radius, float startDeg, float sweepDeg)
    {
        if (Math.Abs(sweepDeg) < 0.01f) return;
        int steps = Math.Max(2, (int)(Math.Abs(sweepDeg) / 2f));
        var path = new PathF();
        for (int i = 0; i <= steps; i++)
        {
            float a = (startDeg + sweepDeg * i / steps) * MathF.PI / 180f;
            float px = cx + radius * MathF.Cos(a);
            float py = cy + radius * MathF.Sin(a);
            if (i == 0) path.MoveTo(px, py);
            else path.LineTo(px, py);
        }
        canvas.DrawPath(path);
    }

    public static Color Lerp(Color a, Color b, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return new Color(a.Red + (b.Red - a.Red) * t, a.Green + (b.Green - a.Green) * t, a.Blue + (b.Blue - a.Blue) * t, 1f);
    }
}

/// <summary>Pozitif / negatif / nötr dağılımı için halka grafik.</summary>
public sealed class DonutChartDrawable : IDrawable
{
    private readonly (string Label, double Value, Color Color)[] _slices;
    private readonly string _centerTitle;
    private readonly string _centerSubtitle;

    public DonutChartDrawable(AnalysisResult r)
    {
        _slices =
        [
            ("Pozitif", r.Positive, ChartPalette.Positive),
            ("Negatif", r.Negative, ChartPalette.Negative),
            ("Nötr", r.Neutral, ChartPalette.Neutral),
        ];
        var top = _slices.MaxBy(s => s.Value);
        _centerTitle = $"%{top.Value:0}";
        _centerSubtitle = top.Label;
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        float legendWidth = rect.Width >= 300 ? 130 : 0;
        float size = Math.Min(rect.Height - 8, rect.Width - legendWidth - 8);
        if (size <= 20) return;

        float stroke = Math.Max(12, size * 0.13f);
        float x = rect.X + (rect.Width - legendWidth - size) / 2;
        float y = rect.Y + (rect.Height - size) / 2;
        var arcRect = new RectF(x + stroke / 2, y + stroke / 2, size - stroke, size - stroke);

        float cx = arcRect.Center.X, cy = arcRect.Center.Y, radius = arcRect.Width / 2;

        canvas.StrokeSize = stroke;
        canvas.StrokeLineCap = LineCap.Butt;
        canvas.StrokeColor = ChartPalette.Track;
        canvas.DrawEllipse(arcRect);

        float start = -90f; // saat 12 yönünden başla
        double total = _slices.Sum(s => s.Value);
        foreach (var slice in _slices)
        {
            if (slice.Value <= 0 || total <= 0) continue;
            float sweep = (float)(slice.Value / total * 360.0);
            canvas.StrokeColor = slice.Color;
            // Dilimler arasında ince boşluk bırakılır
            float gap = sweep >= 359.5f ? 0 : Math.Min(1.5f, sweep / 4);
            ChartPalette.StrokeArc(canvas, cx, cy, radius, start + gap, sweep - 2 * gap);
            start += sweep;
        }

        canvas.FontColor = ChartPalette.Text;
        canvas.Font = Font.DefaultBold;
        canvas.FontSize = size * 0.17f;
        canvas.DrawString(_centerTitle, x, y + size * 0.30f, size, size * 0.24f, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.Font = Font.Default;
        canvas.FontColor = ChartPalette.TextMuted;
        canvas.FontSize = Math.Max(11, size * 0.08f);
        canvas.DrawString(_centerSubtitle, x, y + size * 0.54f, size, size * 0.14f, HorizontalAlignment.Center, VerticalAlignment.Center);

        if (legendWidth <= 0) return;

        float lx = rect.Right - legendWidth + 10;
        float ly = rect.Center.Y - 36;
        canvas.FontSize = 13;
        foreach (var slice in _slices)
        {
            canvas.FillColor = slice.Color;
            canvas.FillRoundedRectangle(lx, ly + 4, 10, 10, 3);
            canvas.FontColor = ChartPalette.Text;
            canvas.DrawString($"{slice.Label}  %{slice.Value:0}", lx + 18, ly, legendWidth - 24, 18, HorizontalAlignment.Left, VerticalAlignment.Center);
            ly += 26;
        }
    }
}

/// <summary>Duygu dağılımı için yatay çubuk grafik.</summary>
public sealed class EmotionBarsDrawable : IDrawable
{
    private readonly IReadOnlyList<EmotionScore> _items;

    public EmotionBarsDrawable(IReadOnlyList<EmotionScore> emotions)
    {
        _items = emotions;
    }

    public static double DesiredHeight(int count) => Math.Max(1, count) * 30 + 8;

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (_items.Count == 0) return;

        const float rowH = 30f, barH = 10f;
        float labelW = Math.Min(130, rect.Width * 0.36f);
        float valueW = 44;
        float barX = rect.X + labelW;
        float barW = Math.Max(20, rect.Width - labelW - valueW);

        canvas.FontSize = 13;
        for (int i = 0; i < _items.Count; i++)
        {
            var item = _items[i];
            float rowY = rect.Y + i * rowH;
            float barY = rowY + (rowH - barH) / 2;
            float t = (float)(item.Score / 100.0);

            canvas.Font = i == 0 ? Font.DefaultBold : Font.Default;
            canvas.FontColor = i == 0 ? ChartPalette.Text : ChartPalette.TextMuted;
            canvas.DrawString(item.Name, rect.X, rowY, labelW - 8, rowH, HorizontalAlignment.Left, VerticalAlignment.Center);

            canvas.FillColor = ChartPalette.Track;
            canvas.FillRoundedRectangle(barX, barY, barW, barH, barH / 2);

            if (t > 0)
            {
                canvas.FillColor = ChartPalette.Lerp(ChartPalette.Purple, ChartPalette.Pink, t);
                canvas.FillRoundedRectangle(barX, barY, Math.Max(barH, barW * t), barH, barH / 2);
            }

            canvas.FontColor = ChartPalette.Text;
            canvas.DrawString($"%{item.Score:0}", barX + barW + 6, rowY, valueW - 6, rowH, HorizontalAlignment.Right, VerticalAlignment.Center);
        }
    }
}

/// <summary>Duygu yoğunluğu için yarım daire gösterge.</summary>
public sealed class IntensityGaugeDrawable : IDrawable
{
    private readonly double _score;
    private readonly string _label;

    public IntensityGaugeDrawable(double score, string label)
    {
        _score = Math.Clamp(score, 0, 100);
        _label = label;
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        float size = Math.Min(rect.Width - 8, (rect.Height - 8) * 2f);
        if (size <= 20) return;

        float stroke = Math.Max(10, size * 0.09f);
        float x = rect.X + (rect.Width - size) / 2;
        float y = rect.Y + 4;
        var arc = new RectF(x + stroke / 2, y + stroke / 2, size - stroke, size - stroke);

        float cx = arc.Center.X, cy = arc.Center.Y, radius = arc.Width / 2;

        canvas.StrokeSize = stroke;
        canvas.StrokeLineCap = LineCap.Round;
        canvas.StrokeColor = ChartPalette.Track;
        ChartPalette.StrokeArc(canvas, cx, cy, radius, 180, 180); // üst yarım daire

        float t = (float)(_score / 100.0);
        if (t > 0.005f)
        {
            canvas.StrokeColor = ChartPalette.Lerp(ChartPalette.Purple, ChartPalette.Pink, t);
            ChartPalette.StrokeArc(canvas, cx, cy, radius, 180, 180 * t);
        }

        canvas.FontColor = ChartPalette.Text;
        canvas.Font = Font.DefaultBold;
        canvas.FontSize = size * 0.14f;
        canvas.DrawString(_label, x, y + size * 0.22f, size, size * 0.18f, HorizontalAlignment.Center, VerticalAlignment.Center);
        canvas.Font = Font.Default;
        canvas.FontColor = ChartPalette.TextMuted;
        canvas.FontSize = Math.Max(11, size * 0.065f);
        canvas.DrawString($"Yoğunluk %{_score:0}", x, y + size * 0.38f, size, size * 0.1f, HorizontalAlignment.Center, VerticalAlignment.Center);
    }
}

/// <summary>
/// Anahtar kelimelerden kelime bulutu. Kelime büyüklüğü, sağlayıcının verdiği önem ağırlığıdır.
/// Yerleşim, ağırlık sırasına göre spiral üzerinde çakışmasız yapılır (deterministik).
/// </summary>
public sealed class WordCloudDrawable : IDrawable
{
    private readonly IReadOnlyList<KeywordWeight> _words;

    public WordCloudDrawable(IReadOnlyList<KeywordWeight> words)
    {
        _words = words.OrderByDescending(w => w.Weight).Take(20).ToList();
    }

    public void Draw(ICanvas canvas, RectF rect)
    {
        if (_words.Count == 0) return;

        var placed = new List<RectF>();
        float cx = rect.Center.X, cy = rect.Center.Y;
        float maxFont = Math.Clamp(rect.Width / 11f, 18f, 34f);
        const float minFont = 12f;

        for (int i = 0; i < _words.Count; i++)
        {
            var w = _words[i];
            float t = (float)w.Weight;
            float fontSize = minFont + (maxFont - minFont) * t;
            var font = t >= 0.6f ? Font.DefaultBold : Font.Default;
            var size = canvas.GetStringSize(w.Word, font, fontSize);
            float bw = size.Width + 10, bh = size.Height + 4;

            RectF? spot = null;
            for (float a = 0; a < 60 * MathF.PI; a += 0.35f)
            {
                float r = 2.2f * a;
                float px = cx + r * MathF.Cos(a) * 1.6f - bw / 2;
                float py = cy + r * MathF.Sin(a) - bh / 2;
                var candidate = new RectF(px, py, bw, bh);
                if (candidate.Left < rect.Left || candidate.Right > rect.Right || candidate.Top < rect.Top || candidate.Bottom > rect.Bottom)
                    continue;
                if (placed.Any(p => p.IntersectsWith(candidate))) continue;
                spot = candidate;
                break;
            }

            if (spot is not { } s) continue; // sığmayan kelime atlanır
            placed.Add(s);

            canvas.Font = font;
            canvas.FontSize = fontSize;
            canvas.FontColor = i % 3 == 0
                ? ChartPalette.Lerp(ChartPalette.Purple, ChartPalette.Pink, t)
                : ChartPalette.Lerp(ChartPalette.TextMuted, ChartPalette.Text, t);
            canvas.DrawString(w.Word, s.X, s.Y, s.Width, s.Height, HorizontalAlignment.Center, VerticalAlignment.Center);
        }
    }
}
