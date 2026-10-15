using NaPohodu.Core.Filing;

namespace NaPohodu.Tests.Filing;

public class DocumentFileNamesTests
{
    private static readonly HashSet<string> Numbers = ["251100001", "251100002", "FV_2025_7"];

    [Theory]
    [InlineData("251100001.pdf", "251100001")]
    [InlineData("251100001.PDF", "251100001")]
    [InlineData("251100001", "251100001")]
    [InlineData("251100002_dodaci_list.pdf", "251100002")]
    [InlineData("FV_2025_7.pdf", "FV_2025_7")]             // číslo s podtržítkem
    [InlineData("FV_2025_7_priloha.pdf", "FV_2025_7")]
    [InlineData("k251100002.pdf", null)]
    [InlineData("2511000011.pdf", null)]
    [InlineData("_251100001.pdf", null)]
    [InlineData("faktura.pdf", null)]
    public void MatchInvoiceNumber(string fileName, string? expected)
    {
        Assert.Equal(expected, DocumentFileNames.MatchInvoiceNumber(fileName, Numbers));
    }

    [Theory]
    [InlineData("desktop.ini", true)]
    [InlineData("Thumbs.db", true)]
    [InlineData(".DS_Store", true)]
    [InlineData(".~lock.251100001.pdf#", true)]
    [InlineData("251100001.pdf", false)]
    public void IsHiddenOrSystem(string fileName, bool expected)
    {
        Assert.Equal(expected, DocumentFileNames.IsHiddenOrSystem(fileName));
    }
}
