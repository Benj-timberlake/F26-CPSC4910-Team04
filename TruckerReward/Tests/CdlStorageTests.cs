using FrontEnd;
using Xunit;

public class CdlStorageTests
{
    [Theory]
    [InlineData("%PDF-1.7 test")]
    [InlineData("/9j/AA==")]
    [InlineData("iVBORw0KGgo=")]
    public async Task AcceptsSupportedSignatures(string input)
    {
        var bytes = input.StartsWith('%') ? System.Text.Encoding.ASCII.GetBytes(input) : Convert.FromBase64String(input);
        using var source = new MemoryStream(bytes);
        using var result = await CdlStorage.ReadAsync(source);
        Assert.Equal(bytes, result.ToArray());
        Assert.Equal(0, result.Position);
    }

    [Fact]
    public async Task RejectsUnsupportedContent()
    {
        using var source = new MemoryStream("<script>not a license</script>"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => CdlStorage.ReadAsync(source));
    }

    [Fact]
    public async Task RejectsOversizedFile()
    {
        using var source = new MemoryStream(new byte[CdlStorage.MaxBytes + 1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => CdlStorage.ReadAsync(source));
    }
}
