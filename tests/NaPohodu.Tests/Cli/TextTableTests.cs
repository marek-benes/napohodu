using NaPohodu.App.Cli;

namespace NaPohodu.Tests.Cli;

public class TextTableTests
{
    [Fact]
    public void ToString_AlignsColumnsAndTruncates()
    {
        var table = new TextTable(new("Název", MaxWidth: 6), new("Částka", AlignRight: true));
        table.AddRow("Šťastný a syn", "1,50");
        table.AddRow("ABC", null);

        var lines = table.ToString().Split(Environment.NewLine, StringSplitOptions.RemoveEmptyEntries);

        Assert.Equal(["Název   Částka", "------  ------", "Šťast…    1,50", "ABC"], lines);
    }

    [Fact]
    public void AddRow_WrongCellCount_Throws()
    {
        var table = new TextTable(new("A"), new("B"));

        Assert.Throws<ArgumentException>(() => table.AddRow("jen jedna"));
    }
}
