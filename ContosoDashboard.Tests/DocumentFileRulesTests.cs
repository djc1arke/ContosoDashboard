using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;

namespace ContosoDashboard.Tests;

public class DocumentFileRulesTests
{
    private static FileValidationResult Validate(string name, byte[] bytes)
    {
        using var ms = new MemoryStream(bytes);
        return DocumentFileRules.Validate(name, bytes.Length, ms);
    }

    [Theory]
    [InlineData("a.pdf")] [InlineData("a.PDF")] [InlineData("a.jpg")] [InlineData("a.jpeg")] [InlineData("a.png")] [InlineData("a.txt")]
    public void Accepts_valid_simple_types(string name)
    {
        var ext = Path.GetExtension(name).ToLowerInvariant();
        var bytes = ext switch
        {
            ".pdf" => TestData.Pdf(),
            ".png" => TestData.Png(),
            ".txt" => TestData.Text(),
            _ => TestData.Jpeg()
        };
        var r = Validate(name, bytes);
        Assert.True(r.IsValid, r.ErrorMessage);
        Assert.Equal(ext, r.Extension);
        Assert.NotNull(r.ContentType);
    }

    [Theory]
    [InlineData("a.doc")] [InlineData("a.xls")] [InlineData("a.ppt")]
    public void Accepts_legacy_office_with_ole2_signature(string name)
        => Assert.True(Validate(name, TestData.Ole2()).IsValid);

    [Theory]
    [InlineData("a.docx", "word")] [InlineData("a.xlsx", "xl")] [InlineData("a.pptx", "ppt")]
    public void Accepts_ooxml_with_matching_root_part(string name, string root)
        => Assert.True(Validate(name, TestData.Ooxml(root)).IsValid);

    [Fact]
    public void Rejects_ooxml_with_wrong_root_part()
        => Assert.False(Validate("a.docx", TestData.Ooxml("xl")).IsValid);

    [Theory]
    [InlineData("a.exe")] [InlineData("a.zip")] [InlineData("noextension")] [InlineData("a.pdf.exe")]
    public void Rejects_unsupported_types(string name)
    {
        var r = Validate(name, TestData.Pdf());
        Assert.False(r.IsValid);
        Assert.Contains("PDF", r.ErrorMessage);
    }

    [Fact]
    public void Size_boundary_is_25MB_inclusive()
    {
        var ok = new byte[26_214_400];
        "%PDF-"u8.CopyTo(ok);
        Assert.True(Validate("a.pdf", ok).IsValid);

        var tooBig = new byte[26_214_401];
        "%PDF-"u8.CopyTo(tooBig);
        var r = Validate("a.pdf", tooBig);
        Assert.False(r.IsValid);
        Assert.Contains("25 MB", r.ErrorMessage);
    }

    [Fact]
    public void Rejects_zero_byte()
        => Assert.False(Validate("a.pdf", Array.Empty<byte>()).IsValid);

    [Fact]
    public void Rejects_executable_renamed_to_pdf()
    {
        var exe = new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 };
        var r = Validate("evil.pdf", exe);
        Assert.False(r.IsValid);
        Assert.Contains("does not match", r.ErrorMessage);
    }

    [Fact]
    public void Rejects_png_content_named_jpg()
        => Assert.False(Validate("a.jpg", TestData.Png()).IsValid);

    [Fact]
    public void Text_with_nul_bytes_rejected()
        => Assert.False(Validate("a.txt", new byte[] { 0x68, 0x00, 0x69 }).IsValid);

    [Fact]
    public void Text_with_invalid_utf8_rejected()
        => Assert.False(Validate("a.txt", new byte[] { 0xC3, 0x28, 0x41 }).IsValid);

    [Fact]
    public void Text_utf16_bom_accepted()
        => Assert.True(Validate("a.txt", new byte[] { 0xFF, 0xFE, 0x68, 0x00, 0x69, 0x00 }).IsValid);

    [Theory]
    [InlineData("../../etc/passwd", "passwd")]
    [InlineData(@"C:\temp\evil.pdf", "evil.pdf")]
    [InlineData("a\"b<c>.pdf", "abc.pdf")]
    [InlineData("", "file")]
    public void Sanitizes_file_names(string input, string expected)
        => Assert.Equal(expected, DocumentFileRules.SanitizeFileName(input));

    [Fact]
    public void Sanitizer_truncates_long_names_keeping_extension()
    {
        var name = new string('a', 400) + ".pdf";
        var s = DocumentFileRules.SanitizeFileName(name);
        Assert.True(s.Length <= 255);
        Assert.EndsWith(".pdf", s);
    }

    [Theory]
    [InlineData("pdf", true)] [InlineData("png", true)] [InlineData("jpg", true)] [InlineData("jpeg", true)]
    [InlineData("docx", false)] [InlineData("txt", false)]
    public void Previewable_types(string type, bool expected)
        => Assert.Equal(expected, DocumentFileRules.IsPreviewable(type));
}
