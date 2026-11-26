using SentimentAI.Core.Configuration;
using SentimentAI.Core.Errors;
using SentimentAI.Core.Privacy;
using SentimentAI.Core.Text;

namespace SentimentAI.Core.Tests;

public class TextValidatorTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t ")]
    public void Bos_metin_reddedilir(string? input)
    {
        var ex = Assert.Throws<AnalysisException>(() => TextValidator.Validate(input));
        Assert.Equal(ErrorKind.UnsupportedText, ex.Kind);
    }

    [Theory]
    [InlineData("12345 678")]
    [InlineData("!!! ??? ...")]
    [InlineData("ab")]
    public void Harf_icermeyen_veya_cok_kisa_metin_reddedilir(string input)
    {
        var ex = Assert.Throws<AnalysisException>(() => TextValidator.Validate(input));
        Assert.Equal(ErrorKind.UnsupportedText, ex.Kind);
    }

    [Fact]
    public void Sinirdaki_metin_kabul_edilir_fazlasi_reddedilir()
    {
        var ok = new string('a', AppLimits.MaxTextChars);
        Assert.Equal(AppLimits.MaxTextChars, TextValidator.Validate(ok).Length);

        var tooLong = new string('a', AppLimits.MaxTextChars + 1);
        var ex = Assert.Throws<AnalysisException>(() => TextValidator.Validate(tooLong));
        Assert.Equal(ErrorKind.TextTooLong, ex.Kind);
    }

    [Fact]
    public void Normalize_satir_sonlarini_ve_fazla_bos_satirlari_temizler()
    {
        var result = TextValidator.Normalize("  Merhaba\r\n\r\n\r\n\r\nDünya\u0007  ");
        Assert.Equal("Merhaba\n\nDünya", result);
    }

    [Fact]
    public void Karakter_sayaci_normallestirilmis_uzunlugu_verir()
    {
        Assert.Equal(5, TextValidator.CountChars("  şğüöç  "));
    }
}

public class AnonymizerTests
{
    [Fact]
    public void Eposta_maskelenir()
    {
        var r = Anonymizer.Anonymize("Bana furkan.test+1@ornek.com.tr adresinden yaz.");
        Assert.Equal("Bana [E-POSTA] adresinden yaz.", r.Text);
        Assert.Equal(1, r.Counts["e-posta"]);
    }

    [Theory]
    [InlineData("Numaram 0532 123 45 67 ara.")]
    [InlineData("Numaram +90 532 123 45 67 ara.")]
    [InlineData("Numaram 05321234567 ara.")]
    [InlineData("Numaram (0212) 555-12-34 ara.")]
    [InlineData("Numaram 532-123-4567 ara.")]
    public void Telefon_numaralari_maskelenir(string input)
    {
        var r = Anonymizer.Anonymize(input);
        Assert.Equal("Numaram [TELEFON] ara.", r.Text);
    }

    [Fact]
    public void Gecerli_TC_kimlik_no_maskelenir_gecersiz_dokunulmaz()
    {
        Assert.True(Anonymizer.IsValidTurkishNationalId("10000000146"));
        Assert.False(Anonymizer.IsValidTurkishNationalId("12345678901"));
        Assert.False(Anonymizer.IsValidTurkishNationalId("01234567890"));

        var r = Anonymizer.Anonymize("TC: 10000000146, sipariş no: 12345678901");
        Assert.Contains("[TC KİMLİK NO]", r.Text);
        Assert.Contains("12345678901", r.Text);
        Assert.Equal(1, r.Counts["TC kimlik no"]);
    }

    [Fact]
    public void IBAN_ve_kart_numarasi_maskelenir()
    {
        var r = Anonymizer.Anonymize("IBAN TR33 0006 1005 1978 6457 8413 26 ve kart 4111 1111 1111 1111");
        Assert.Contains("[IBAN]", r.Text);
        Assert.Contains("[KART NO]", r.Text);
        Assert.DoesNotContain("4111", r.Text);
    }

    [Fact]
    public void Luhn_kontrolunden_gecmeyen_uzun_sayi_kart_sayilmaz()
    {
        var r = Anonymizer.Anonymize("Takip kodu 1234 5678 9012 3456");
        Assert.DoesNotContain("[KART NO]", r.Text);
    }

    [Theory]
    [InlineData("Adresim Atatürk Mahallesi Gül Sokak No: 12/3 Kadıköy")]
    [InlineData("Adresim Cumhuriyet Mah. 1203. Sk. No:5 Daire 4")]
    [InlineData("Adresim Bağdat Caddesi No: 214 Kadıköy")]
    public void Acik_adres_kaliplari_maskelenir(string input)
    {
        var r = Anonymizer.Anonymize(input);
        Assert.Contains("[ADRES]", r.Text);
        Assert.True(r.Counts["adres"] >= 1);
    }

    [Fact]
    public void Kisisel_veri_olmayan_metin_degismez()
    {
        const string text = "Bugün hava çok güzel, 3 kilometre yürüdüm ve 2024 yılını düşündüm.";
        var r = Anonymizer.Anonymize(text);
        Assert.Equal(text, r.Text);
        Assert.False(r.Any);
    }

    [Fact]
    public void Ozet_metni_sayilari_icerir()
    {
        var r = Anonymizer.Anonymize("a@b.co ve c@d.co, tel 0532 111 22 33");
        Assert.Equal(3, r.Total);
        Assert.Contains("2 e-posta", r.Describe());
        Assert.Contains("1 telefon", r.Describe());
    }
}
