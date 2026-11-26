using System.IO.Compression;
using System.Text;
using System.Xml;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;
using UglyToad.PdfPig;
using UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor;
using UglyToad.PdfPig.Exceptions;

namespace SentimentAI.Core.Files;

/// <summary>
/// TXT, PDF, DOCX, CSV ve XLSX dosyalarından cihaz üzerinde metin çıkarır.
/// Dosya bellekte işlenir; diske geçici kopya yazılmaz. Sonuç, tek tek seçilebilen kısa metin parçalarıdır.
/// </summary>
public static class FileTextExtractor
{
    public static IReadOnlyList<string> SupportedExtensions { get; } = [".txt", ".pdf", ".docx", ".csv", ".xlsx"];

    public static string SupportedListText => "TXT, PDF, DOCX, CSV, XLSX";

    private static readonly byte[] OleSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    static FileTextExtractor()
    {
        // Windows-1254 (Türkçe) gibi eski kod sayfalarıyla kaydedilmiş TXT/CSV dosyaları için
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public static FileKind? DetectKind(string fileName) => Path.GetExtension(fileName).ToLowerInvariant() switch
    {
        ".txt" => FileKind.Text,
        ".pdf" => FileKind.Pdf,
        ".docx" => FileKind.Docx,
        ".csv" => FileKind.Csv,
        ".xlsx" => FileKind.Xlsx,
        _ => null,
    };

    public static async Task<ExtractedFile> ExtractAsync(Stream stream, string fileName, CancellationToken ct = default)
    {
        var kind = DetectKind(fileName)
                   ?? throw Fail($"Desteklenmeyen dosya türü ({Path.GetExtension(fileName)}). Desteklenenler: {SupportedListText}.");

        var bytes = await ReadLimitedAsync(stream, ct).ConfigureAwait(false);
        if (bytes.Length == 0) throw Fail("Dosya boş.");

        // Ayrıştırma CPU yoğun olabilir; arayüz iş parçacığını bloklamamak için arka planda çalışır.
        return await Task.Run(() => Extract(bytes, fileName, kind, ct), ct).ConfigureAwait(false);
    }

    public static ExtractedFile Extract(byte[] bytes, string fileName, FileKind kind, CancellationToken ct = default)
    {
        if (bytes.LongLength > AppLimits.MaxFileBytes) throw TooLarge();

        var builder = new SegmentBuilder();
        try
        {
            switch (kind)
            {
                case FileKind.Text: ReadText(bytes, builder); break;
                case FileKind.Csv: ReadCsv(bytes, builder, ct); break;
                case FileKind.Pdf: ReadPdf(bytes, builder, ct); break;
                case FileKind.Docx: ReadDocx(bytes, builder, ct); break;
                case FileKind.Xlsx: ReadXlsx(bytes, builder, ct); break;
            }
        }
        catch (AnalysisException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (PdfDocumentEncryptedException) { throw Fail("PDF parola ile korunuyor (şifreli). Şifresiz bir kopya seç."); }
        catch (InvalidDataException) { throw Fail("Dosya bozuk görünüyor veya beklenen biçimde değil."); }
        catch (XmlException) { throw Fail("Dosyanın iç yapısı bozuk (XML okunamadı)."); }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            throw new AnalysisException(ErrorKind.FileReadFailed, "Dosya okunamadı: bozuk veya desteklenmeyen içerik.", inner: ex);
        }

        if (builder.Segments.Count == 0)
            throw Fail(kind == FileKind.Pdf
                ? "PDF'te seçilebilir metin bulunamadı. Taranmış (görüntü) bir PDF olabilir."
                : "Dosyada analiz edilebilecek metin bulunamadı.");

        return new ExtractedFile(Path.GetFileName(fileName), kind, builder.Segments, builder.Truncated);
    }

    // ------------------------------------------------------------------ TXT

    private static void ReadText(byte[] bytes, SegmentBuilder builder) =>
        builder.AddDocumentText("Metin", DecodeText(bytes));

    /// <summary>BOM, UTF-8 ve Türkçe Windows-1254 kodlamalarını tanır.</summary>
    public static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

        try
        {
            return new UTF8Encoding(false, throwOnInvalidBytes: true).GetString(bytes);
        }
        catch (DecoderFallbackException)
        {
            try { return Encoding.GetEncoding(1254).GetString(bytes); }
            catch (Exception) { return Encoding.Latin1.GetString(bytes); }
        }
    }

    // ------------------------------------------------------------------ CSV

    private static void ReadCsv(byte[] bytes, SegmentBuilder builder, CancellationToken ct)
    {
        var text = DecodeText(bytes);
        var rows = CsvParser.Parse(text);
        AddTable(builder, rows, sheetName: null, ct);
    }

    /// <summary>Tablo verisini "Satır N · Sütun" etiketli tek tek seçilebilir alanlara çevirir.</summary>
    internal static void AddTable(SegmentBuilder builder, IReadOnlyList<IReadOnlyList<string>> rows, string? sheetName, CancellationToken ct)
    {
        if (rows.Count == 0) return;

        // İlk satır tamamen metin ise başlık satırı kabul edilir.
        var first = rows[0];
        bool hasHeader = rows.Count > 1 && first.Count > 0 && first.All(c => c.Length > 0 && !double.TryParse(c, out _));
        var header = hasHeader ? first : null;
        var prefix = sheetName is null ? "" : sheetName + " · ";

        for (int r = hasHeader ? 1 : 0; r < rows.Count; r++)
        {
            ct.ThrowIfCancellationRequested();
            if (builder.IsFull) return;

            var row = rows[r];
            for (int c = 0; c < row.Count; c++)
            {
                var cell = row[c];
                if (cell.Length < 2 || double.TryParse(cell, out _)) continue;
                var colName = header is not null && c < header.Count && header[c].Length > 0
                    ? header[c]
                    : ColumnLetter(c);
                builder.AddField($"{prefix}Satır {r + 1} · {colName}", cell);
            }
        }
    }

    internal static string ColumnLetter(int index)
    {
        var sb = new StringBuilder();
        index++;
        while (index > 0)
        {
            int rem = (index - 1) % 26;
            sb.Insert(0, (char)('A' + rem));
            index = (index - 1) / 26;
        }
        return sb.ToString();
    }

    // ------------------------------------------------------------------ PDF

    private static void ReadPdf(byte[] bytes, SegmentBuilder builder, CancellationToken ct)
    {
        using var document = PdfDocument.Open(bytes);
        foreach (var page in document.GetPages())
        {
            ct.ThrowIfCancellationRequested();
            if (builder.IsFull) return;
            var text = ContentOrderTextExtractor.GetText(page);
            builder.AddDocumentText($"Sayfa {page.Number}", text);
        }
    }

    // ------------------------------------------------------------------ DOCX

    private const string WordNs = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private const string SheetNs = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    private const string RelNs = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    private static void ReadDocx(byte[] bytes, SegmentBuilder builder, CancellationToken ct)
    {
        using var zip = OpenOoxml(bytes);
        var entry = zip.GetEntry("word/document.xml") ?? throw Fail("Geçerli bir Word (DOCX) belgesi değil.");

        var paragraphs = new List<string>();
        var current = new StringBuilder();

        using (var reader = CreateXmlReader(entry))
        {
            reader.MoveToContent();
            while (!reader.EOF)
            {
                ct.ThrowIfCancellationRequested();
                if (reader.NamespaceURI == WordNs)
                {
                    if (reader.NodeType == XmlNodeType.Element)
                    {
                        if (reader.LocalName == "t")
                        {
                            // ReadElementContentAsString imleci zaten bir sonraki düğüme taşır
                            current.Append(reader.ReadElementContentAsString());
                            continue;
                        }
                        if (reader.LocalName == "tab") current.Append('\t');
                        else if (reader.LocalName is "br" or "cr") current.Append('\n');
                    }
                    else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "p")
                    {
                        paragraphs.Add(current.ToString());
                        current.Clear();
                    }
                }
                reader.Read();
            }
        }

        if (current.Length > 0) paragraphs.Add(current.ToString());
        builder.AddParagraphs("Belge", paragraphs);
    }

    // ------------------------------------------------------------------ XLSX

    private static void ReadXlsx(byte[] bytes, SegmentBuilder builder, CancellationToken ct)
    {
        using var zip = OpenOoxml(bytes);
        var shared = ReadSharedStrings(zip);
        var sheets = ReadSheetList(zip);
        if (sheets.Count == 0) throw Fail("Geçerli bir Excel (XLSX) çalışma kitabı değil.");

        foreach (var (name, path) in sheets)
        {
            ct.ThrowIfCancellationRequested();
            if (builder.IsFull) return;
            var entry = zip.GetEntry(path);
            if (entry is null) continue;
            var rows = ReadSheetRows(entry, shared, ct);
            AddTable(builder, rows, sheets.Count > 1 ? name : null, ct);
        }
    }

    private static List<string> ReadSharedStrings(ZipArchive zip)
    {
        var list = new List<string>();
        var entry = zip.GetEntry("xl/sharedStrings.xml");
        if (entry is null) return list;

        using var reader = CreateXmlReader(entry);
        StringBuilder? current = null;
        reader.MoveToContent();
        while (!reader.EOF)
        {
            if (reader.NamespaceURI == SheetNs)
            {
                if (reader.NodeType == XmlNodeType.Element)
                {
                    switch (reader.LocalName)
                    {
                        case "si":
                            if (reader.IsEmptyElement) list.Add("");
                            else current = new StringBuilder();
                            break;
                        case "t" when current is not null:
                            current.Append(reader.ReadElementContentAsString());
                            continue;
                        case "rPh":
                            reader.Skip(); // fonetik okunuşlar (Japonca) metne dahil edilmez
                            continue;
                    }
                }
                else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "si" && current is not null)
                {
                    list.Add(current.ToString());
                    current = null;
                }
            }
            reader.Read();
        }
        return list;
    }

    private static List<(string Name, string Path)> ReadSheetList(ZipArchive zip)
    {
        var workbook = zip.GetEntry("xl/workbook.xml");
        var rels = zip.GetEntry("xl/_rels/workbook.xml.rels");
        var result = new List<(string, string)>();
        if (workbook is null) return result;

        var targets = new Dictionary<string, string>(StringComparer.Ordinal);
        if (rels is not null)
        {
            using var rr = CreateXmlReader(rels);
            while (rr.Read())
            {
                if (rr.NodeType == XmlNodeType.Element && rr.LocalName == "Relationship")
                {
                    var id = rr.GetAttribute("Id");
                    var target = rr.GetAttribute("Target");
                    if (id is null || target is null) continue;
                    target = target.StartsWith('/') ? target.TrimStart('/') : "xl/" + target;
                    targets[id] = target.Replace("\\", "/");
                }
            }
        }

        using var wr = CreateXmlReader(workbook);
        int index = 0;
        while (wr.Read())
        {
            if (wr.NodeType == XmlNodeType.Element && wr.LocalName == "sheet" && wr.NamespaceURI == SheetNs)
            {
                index++;
                var name = wr.GetAttribute("name") ?? $"Sayfa{index}";
                var rid = wr.GetAttribute("id", RelNs);
                var path = rid is not null && targets.TryGetValue(rid, out var t) ? t : $"xl/worksheets/sheet{index}.xml";
                result.Add((name, path));
            }
        }
        return result;
    }

    private static List<IReadOnlyList<string>> ReadSheetRows(ZipArchiveEntry entry, List<string> shared, CancellationToken ct)
    {
        var rows = new List<IReadOnlyList<string>>();
        using var reader = CreateXmlReader(entry);

        List<string>? row = null;
        int cellCount = 0;
        while (reader.Read())
        {
            if (reader.NamespaceURI != SheetNs) continue;

            if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "row")
            {
                ct.ThrowIfCancellationRequested();
                row = [];
                // Boş satırlar (r atlaması) korunarak satır numaraları gerçek Excel satırlarıyla eşleşir
                if (int.TryParse(reader.GetAttribute("r"), out var rNum))
                    while (rows.Count < rNum - 1) rows.Add([]);
                if (reader.IsEmptyElement) { rows.Add(row); row = null; }
            }
            else if (reader.NodeType == XmlNodeType.EndElement && reader.LocalName == "row" && row is not null)
            {
                rows.Add(row);
                row = null;
                if (rows.Count > 100_000) break;
            }
            else if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "c" && row is not null)
            {
                var type = reader.GetAttribute("t");
                var reference = reader.GetAttribute("r");
                int col = reference is null ? row.Count : ColumnIndex(reference);
                while (row.Count < col) row.Add("");

                string value = ReadCellValue(reader, type, shared);
                row.Add(value);
                if (++cellCount > 1_000_000) break;
            }
        }

        return rows;
    }

    private static string ReadCellValue(XmlReader reader, string? type, List<string> shared)
    {
        var sb = new StringBuilder();
        string? v = null;

        // Alt ağaç okuyucu: dispose edildiğinde ana okuyucu hücrenin kapanış etiketinde kalır.
        using (var sub = reader.ReadSubtree())
        {
            sub.MoveToContent();
            while (!sub.EOF)
            {
                if (sub.NodeType == XmlNodeType.Element && sub.NamespaceURI == SheetNs)
                {
                    if (sub.LocalName == "v") { v = sub.ReadElementContentAsString(); continue; }
                    if (sub.LocalName == "t") { sb.Append(sub.ReadElementContentAsString()); continue; }
                    if (sub.LocalName == "rPh") { sub.Skip(); continue; }
                }
                sub.Read();
            }
        }

        return type switch
        {
            "s" => int.TryParse(v, out var idx) && idx >= 0 && idx < shared.Count ? shared[idx] : "",
            "inlineStr" => sb.ToString(),
            "str" => v ?? "",
            _ => "", // sayılar, tarihler ve mantıksal değerler metin analizi için atlanır
        };
    }

    internal static int ColumnIndex(string cellReference)
    {
        int col = 0;
        foreach (var ch in cellReference)
        {
            if (ch is >= 'A' and <= 'Z') col = col * 26 + (ch - 'A' + 1);
            else if (ch is >= 'a' and <= 'z') col = col * 26 + (ch - 'a' + 1);
            else break;
        }
        return Math.Max(col - 1, 0);
    }

    // ------------------------------------------------------------------ ortak

    private static ZipArchive OpenOoxml(byte[] bytes)
    {
        if (bytes.Length >= 8 && bytes.AsSpan(0, 8).SequenceEqual(OleSignature))
            throw Fail("Dosya parola ile korunuyor (şifreli) veya eski Office biçiminde (.doc/.xls). Şifresiz bir DOCX/XLSX seç.");

        try
        {
            return new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);
        }
        catch (InvalidDataException)
        {
            throw Fail("Dosya bozuk görünüyor (geçerli bir Office belgesi değil).");
        }
    }

    private static XmlReader CreateXmlReader(ZipArchiveEntry entry)
    {
        if (entry.Length > AppLimits.MaxArchiveEntryBytes)
            throw Fail("Dosyanın iç içeriği çok büyük.");

        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit, // XXE saldırılarına karşı
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreProcessingInstructions = true,
            IgnoreWhitespace = false,
            CloseInput = true,
        };
        return XmlReader.Create(entry.Open(), settings);
    }

    private static async Task<byte[]> ReadLimitedAsync(Stream stream, CancellationToken ct)
    {
        if (stream.CanSeek && stream.Length > AppLimits.MaxFileBytes) throw TooLarge();

        using var ms = new MemoryStream(stream.CanSeek ? (int)Math.Min(stream.Length, AppLimits.MaxFileBytes) : 64 * 1024);
        var buffer = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
        {
            if (ms.Length + read > AppLimits.MaxFileBytes) throw TooLarge();
            ms.Write(buffer, 0, read);
        }
        return ms.ToArray();
    }

    private static AnalysisException TooLarge() =>
        Fail($"Dosya çok büyük. En fazla {AppLimits.MaxFileBytes / (1024 * 1024)} MB boyutunda dosya yüklenebilir.");

    private static AnalysisException Fail(string detail) => new(ErrorKind.FileReadFailed, detail);
}
