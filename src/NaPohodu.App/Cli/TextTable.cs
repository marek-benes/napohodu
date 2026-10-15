using System.Text;

namespace NaPohodu.App.Cli;

/// <summary>Jednoduchá textová tabulka se zarovnanými sloupci pro výstup CLI.</summary>
internal sealed class TextTable
{
    private const string Separator = "  ";
    private readonly Column[] _columns;
    private readonly List<string[]> _rows = [];

    public TextTable(params Column[] columns)
    {
        _columns = columns;
    }

    public int RowCount => _rows.Count;

    public void AddRow(params string?[] cells)
    {
        if (cells.Length != _columns.Length)
        {
            throw new ArgumentException($"Řádek má {cells.Length} buněk, tabulka {_columns.Length} sloupců.", nameof(cells));
        }

        _rows.Add([.. cells.Select((cell, i) => Truncate(cell ?? string.Empty, _columns[i].MaxWidth))]);
    }

    public override string ToString()
    {
        var widths = _columns
            .Select((column, i) => _rows.Select(row => row[i].Length).Append(column.Header.Length).Max())
            .ToArray();

        var output = new StringBuilder();
        AppendLine(output, [.. _columns.Select(c => c.Header)], widths);
        AppendLine(output, [.. widths.Select(w => new string('-', w))], widths);
        foreach (var row in _rows)
        {
            AppendLine(output, row, widths);
        }

        return output.ToString();
    }

    private void AppendLine(StringBuilder output, string[] cells, int[] widths)
    {
        var line = string.Join(
            Separator,
            cells.Select((cell, i) => _columns[i].AlignRight ? cell.PadLeft(widths[i]) : cell.PadRight(widths[i])));
        output.AppendLine(line.TrimEnd());
    }

    private static string Truncate(string value, int? maxWidth) =>
        maxWidth is { } max && value.Length > max ? string.Concat(value.AsSpan(0, max - 1), "…") : value;

    internal sealed record Column(string Header, bool AlignRight = false, int? MaxWidth = null);
}
