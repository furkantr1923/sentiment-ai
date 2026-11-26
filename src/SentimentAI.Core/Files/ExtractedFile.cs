using SentimentAI.Core.Configuration;

namespace SentimentAI.Core.Files;

public enum FileKind { Text, Pdf, Docx, Csv, Xlsx }

/// <summary>Dosyadan çıkarılan, tek başına analiz edilebilecek kısa metin parçası.</summary>
public sealed record TextSegment(int Index, string Label, string Text)
{
    public int Length => Text.Length;

    public bool FitsLimit => Text.Length <= AppLimits.MaxTextChars;

    /// <summary>Listede gösterilecek kısa önizleme.</summary>
    public string Preview
    {
        get
        {
            var oneLine = Text.Replace('\n', ' ').Replace('\t', ' ');
            return oneLine.Length <= 180 ? oneLine : oneLine[..180].TrimEnd() + "…";
        }
    }

    public string LengthText => $"{Length:N0} karakter";
}

/// <summary>Bir dosyanın okunma sonucu. Dosyanın kendisi değil, yalnızca metin parçaları bellekte tutulur.</summary>
public sealed record ExtractedFile(string FileName, FileKind Kind, IReadOnlyList<TextSegment> Segments, bool Truncated)
{
    public string KindLabel => Kind switch
    {
        FileKind.Pdf => "PDF",
        FileKind.Docx => "Word (DOCX)",
        FileKind.Csv => "CSV",
        FileKind.Xlsx => "Excel (XLSX)",
        _ => "Metin (TXT)",
    };
}
