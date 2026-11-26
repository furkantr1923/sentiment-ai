using System.IO.Compression;
using System.Text;
using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Files;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;

namespace SentimentAI.Core.Tests;

public class FileExtractionTests
{
    private static Task<ExtractedFile> Extract(byte[] bytes, string name) =>
        FileTextExtractor.ExtractAsync(new MemoryStream(bytes), name);

    [Fact]
    public async Task TXT_UTF8_okunur_ve_paragraflara_bolunur()
    {
        var text = "Birinci paragraf: Bugün harika bir gün geçirdim ve çok mutluyum, her şey yolunda gitti.\n\n" +
                   "İkinci paragraf: Akşam yemeği biraz geç kaldı ama yine de güzeldi, ailemle zaman geçirdim.";
        var file = await Extract(Encoding.UTF8.GetBytes(text), "not.txt");

        Assert.Equal(FileKind.Text, file.Kind);
        Assert.Equal(2, file.Segments.Count);
        Assert.StartsWith("Birinci", file.Segments[0].Text);
        Assert.All(file.Segments, s => Assert.True(s.FitsLimit));
    }

    [Fact]
    public async Task TXT_Windows1254_Turkce_karakterleri_dogru_cozer()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        var bytes = Encoding.GetEncoding(1254).GetBytes("Şişli'de güzel bir öğleden sonra ıhlamur içtik.");
        var file = await Extract(bytes, "eski.txt");
        Assert.Contains("Şişli", file.Segments[0].Text);
        Assert.Contains("ıhlamur", file.Segments[0].Text);
    }

    [Fact]
    public async Task Uzun_metin_sinira_sigan_parcalara_bolunur()
    {
        var sentence = "Bu cümle uzun bir belgenin parçasıdır ve analiz sınırını aşmak için tekrarlanır. ";
        var big = string.Concat(Enumerable.Repeat(sentence, 200)); // ~16.000 karakter, tek paragraf
        var file = await Extract(Encoding.UTF8.GetBytes(big), "uzun.txt");

        Assert.True(file.Segments.Count > 5);
        Assert.All(file.Segments, s => Assert.True(s.Length <= AppLimits.MaxTextChars));
    }

    [Fact]
    public async Task CSV_satir_ve_sutunlari_ayri_secilebilir_alanlar_olur()
    {
        var csv = "id;yorum;puan\n1;\"Ürün harika, çok beğendim\";5\n2;Kargo geç geldi;2\n3;;4\n";
        var file = await Extract(Encoding.UTF8.GetBytes(csv), "yorumlar.csv");

        Assert.Equal(FileKind.Csv, file.Kind);
        Assert.Equal(2, file.Segments.Count);
        Assert.Equal("Satır 2 · yorum", file.Segments[0].Label);
        Assert.Equal("Ürün harika, çok beğendim", file.Segments[0].Text);
        Assert.Equal("Kargo geç geldi", file.Segments[1].Text);
    }

    [Fact]
    public void CSV_ayrac_ve_tirnak_kurallari()
    {
        Assert.Equal(';', CsvParser.DetectDelimiter("a;b;c\n1;2;3"));
        Assert.Equal(',', CsvParser.DetectDelimiter("a,b,c\n1,2,3"));
        Assert.Equal('\t', CsvParser.DetectDelimiter("a\tb\n1\t2"));

        var rows = CsvParser.Parse("a,\"b, \"\"tırnak\"\"\",c\r\n1,\"çok\nsatırlı\",3");
        Assert.Equal(2, rows.Count);
        Assert.Equal("b, \"tırnak\"", rows[0][1]);
        Assert.Equal("çok\nsatırlı", rows[1][1]);
    }

    [Fact]
    public async Task DOCX_paragraflari_cikarilir()
    {
        var bytes = BuildDocx(
            "Toplantı notları",
            "Proje zamanında teslim edildi ve müşteri çok memnun kaldı. Ekip harika çalıştı.",
            "Bir sonraki adımda test sürecini hızlandırmamız gerekiyor; bu konuda endişeliyim.");

        var file = await Extract(bytes, "rapor.docx");

        Assert.Equal(FileKind.Docx, file.Kind);
        var all = string.Join("\n", file.Segments.Select(s => s.Text));
        Assert.Contains("Toplantı notları", all);
        Assert.Contains("müşteri çok memnun", all);
        Assert.Contains("endişeliyim", all);
    }

    [Fact]
    public async Task XLSX_paylasilan_ve_satir_ici_metinler_okunur()
    {
        var bytes = BuildXlsx();
        var file = await Extract(bytes, "anket.xlsx");

        Assert.Equal(FileKind.Xlsx, file.Kind);
        var labels = file.Segments.Select(s => s.Label).ToList();
        Assert.Contains("Satır 2 · Yorum", labels);
        Assert.Contains(file.Segments, s => s.Text == "Hizmet çok iyiydi");
        Assert.Contains(file.Segments, s => s.Text == "Satır içi metin hücresi");
        Assert.DoesNotContain(file.Segments, s => s.Text == "42"); // sayılar metin sayılmaz
    }

    [Fact]
    public async Task PDF_metni_sayfa_bazinda_cikarilir()
    {
        var builder = new PdfDocumentBuilder();
        var font = builder.AddStandard14Font(Standard14Font.Helvetica);
        var page = builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4);
        page.AddText("Customer feedback: the delivery was fast and the product quality is great.", 11, new UglyToad.PdfPig.Core.PdfPoint(40, 760), font);
        var pdf = builder.Build();

        var file = await Extract(pdf, "geri-bildirim.pdf");

        Assert.Equal(FileKind.Pdf, file.Kind);
        Assert.Contains("delivery was fast", file.Segments[0].Text);
        Assert.StartsWith("Sayfa 1", file.Segments[0].Label);
    }

    [Theory]
    [InlineData("resim.png")]
    [InlineData("eski.doc")]
    [InlineData("dosya")]
    public async Task Desteklenmeyen_uzanti_anlasilir_hata_verir(string name)
    {
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Extract([1, 2, 3], name));
        Assert.Equal(ErrorKind.FileReadFailed, ex.Kind);
        Assert.Contains("Desteklenmeyen", ex.Detail);
    }

    [Fact]
    public async Task Bozuk_DOCX_ve_PDF_anlasilir_hata_verir()
    {
        var docx = await Assert.ThrowsAsync<AnalysisException>(() => Extract(Encoding.UTF8.GetBytes("zip değil"), "bozuk.docx"));
        Assert.Equal(ErrorKind.FileReadFailed, docx.Kind);

        var pdf = await Assert.ThrowsAsync<AnalysisException>(() => Extract(Encoding.UTF8.GetBytes("%PDF-1.4 bozuk içerik"), "bozuk.pdf"));
        Assert.Equal(ErrorKind.FileReadFailed, pdf.Kind);
    }

    [Fact]
    public async Task Sifreli_Office_belgesi_tanınır()
    {
        byte[] ole = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0, 0, 0, 0];
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Extract(ole, "gizli.docx"));
        Assert.Contains("şifreli", ex.Detail);
    }

    [Fact]
    public async Task Boyut_siniri_asilinca_dosya_reddedilir()
    {
        var big = new byte[AppLimits.MaxFileBytes + 1];
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Extract(big, "buyuk.txt"));
        Assert.Contains("çok büyük", ex.Detail);
    }

    [Fact]
    public async Task Bos_dosya_reddedilir()
    {
        var ex = await Assert.ThrowsAsync<AnalysisException>(() => Extract([], "bos.txt"));
        Assert.Equal(ErrorKind.FileReadFailed, ex.Kind);
    }

    [Fact]
    public void Sutun_harfleri_ve_indeksleri()
    {
        Assert.Equal("A", FileTextExtractor.ColumnLetter(0));
        Assert.Equal("Z", FileTextExtractor.ColumnLetter(25));
        Assert.Equal("AA", FileTextExtractor.ColumnLetter(26));
        Assert.Equal(0, FileTextExtractor.ColumnIndex("A1"));
        Assert.Equal(27, FileTextExtractor.ColumnIndex("AB12"));
    }

    // ------------------------------------------------------------ test dosyası üreticileri

    private static byte[] BuildDocx(params string[] paragraphs)
    {
        var body = string.Concat(paragraphs.Select(p =>
            $"<w:p><w:pPr><w:pStyle w:val=\"Normal\"/></w:pPr><w:r><w:t xml:space=\"preserve\">{System.Security.SecurityElement.Escape(p)}</w:t></w:r></w:p>"));
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                  "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
                  body + "<w:sectPr/></w:body></w:document>";

        return Zip(("[Content_Types].xml", "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"/>"),
                   ("word/document.xml", xml));
    }

    private static byte[] BuildXlsx()
    {
        const string ns = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
        const string rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

        var workbook = $"<workbook xmlns=\"{ns}\" xmlns:r=\"{rel}\"><sheets><sheet name=\"Anket\" sheetId=\"1\" r:id=\"rId1\"/></sheets></workbook>";
        var rels = "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
                   "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet1.xml\"/></Relationships>";
        var shared = $"<sst xmlns=\"{ns}\"><si><t>Yorum</t></si><si><t>Puan</t></si><si><r><t>Hizmet </t></r><r><t>çok iyiydi</t></r></si></sst>";
        var sheet = $"<worksheet xmlns=\"{ns}\"><sheetData>" +
                    "<row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row>" +
                    "<row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\"><v>42</v></c></row>" +
                    "<row r=\"3\"><c r=\"A3\" t=\"inlineStr\"><is><t>Satır içi metin hücresi</t></is></c></row>" +
                    "</sheetData></worksheet>";

        return Zip(("xl/workbook.xml", workbook), ("xl/_rels/workbook.xml.rels", rels),
                   ("xl/sharedStrings.xml", shared), ("xl/worksheets/sheet1.xml", sheet));
    }

    private static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var e = zip.CreateEntry(name);
                using var w = new StreamWriter(e.Open(), new UTF8Encoding(false));
                w.Write(content);
            }
        }
        return ms.ToArray();
    }
}

public class TempDataCleanerTests
{
    [Fact]
    public void Gecici_klasor_icerigi_silinir_klasor_kalir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "sentimentai-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "alt"));
        File.WriteAllText(Path.Combine(dir, "secilen.pdf"), "x");
        File.WriteAllText(Path.Combine(dir, "alt", "kopya.txt"), "y");

        var removed = TempDataCleaner.CleanDirectory(dir);

        Assert.Equal(2, removed);
        Assert.True(Directory.Exists(dir));
        Assert.Empty(Directory.EnumerateFileSystemEntries(dir));
        Directory.Delete(dir);
    }

    [Fact]
    public void Olmayan_klasor_hata_vermez() =>
        Assert.Equal(0, TempDataCleaner.CleanDirectory(Path.Combine(Path.GetTempPath(), "yok-" + Guid.NewGuid())));
}
