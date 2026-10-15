// Anonymizace reálných XML odpovědí Pohody na fixtures pro testy.
//
//   dotnet run tools/anonymize-fixture.cs -- <výstupní složka> <vstup>=<výstup> [<vstup>=<výstup> ...]
//
// Zásady:
// - Výchozí je anonymizovat: ponechají se jen hodnoty z explicitního seznamu neosobních polí
//   (částky, data, interní ID, výčty, podsložka dokladu). Neznámé pole = chyba, ne tichý průchod.
// - Náhrady jsou deterministické a společné pro všechny vstupy jednoho běhu (stejné IČ → stejná náhrada),
//   takže vazby mezi soubory (seznam jednotek ↔ export faktur) zůstanou zachované.
// - Exporty faktur se zmenší na reprezentativní vzorek (všechny s dokumenty + typické případy).
// - Na konci se ověří, že se v žádném výstupu nevyskytuje žádná původní citlivá hodnota.
// - Výstup je ve windows-1250 se zachovanými mezerami, jako originál.

using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
var windows1250 = Encoding.GetEncoding(1250);

if (args.Length < 2)
{
    Console.Error.WriteLine("Použití: dotnet run tools/anonymize-fixture.cs -- <výstupní složka> <vstup>=<výstup> [...]");
    return 1;
}

var outputDirectory = args[0];
Directory.CreateDirectory(outputDirectory);
var anonymizer = new Anonymizer();
var outputs = new List<(string Path, string Text)>();

foreach (var pair in args[1..])
{
    var parts = pair.Split('=', 2);
    if (parts.Length != 2)
    {
        Console.Error.WriteLine($"Chybný argument „{pair}“, očekávám <vstup>=<výstup>.");
        return 1;
    }

    var document = XDocument.Parse(windows1250.GetString(File.ReadAllBytes(parts[0])), LoadOptions.PreserveWhitespace);
    var sampled = InvoiceSample.Apply(document);
    anonymizer.Apply(document);
    document.Root!.AddBeforeSelf(new XComment(
        $" Anonymizovaná reálná odpověď Pohody (tools/anonymize-fixture.cs){(sampled is { } s ? $"; vzorek {s.Kept} z {s.Total} faktur" : string.Empty)}. "));

    var path = Path.Combine(outputDirectory, parts[1]);
    var settings = new XmlWriterSettings { Encoding = windows1250, Indent = false, NewLineHandling = NewLineHandling.None };
    using (var writer = XmlWriter.Create(path, settings))
    {
        document.Save(writer);
    }

    outputs.Add((path, windows1250.GetString(File.ReadAllBytes(path))));
    Console.WriteLine($"{parts[0]} → {path}{(sampled is { } t ? $" ({t.Kept}/{t.Total} faktur)" : string.Empty)}");
}

var leaks = anonymizer.FindLeaks(outputs);
foreach (var leak in leaks)
{
    Console.Error.WriteLine($"ÚNIK: {leak}");
}

if (leaks.Count > 0)
{
    foreach (var (path, _) in outputs)
    {
        File.Delete(path);
    }

    Console.Error.WriteLine("Výstupy smazány.");
    return 2;
}

Console.WriteLine($"Hotovo, anonymizováno {anonymizer.Count} hodnot, bez úniků.");
return 0;

/// <summary>Zmenší export faktur na reprezentativní vzorek.</summary>
internal static class InvoiceSample
{
    private const int EvenlySpaced = 12;
    private static readonly XNamespace Lst = "http://www.stormware.cz/schema/version_2/list.xsd";
    private static readonly XNamespace Inv = "http://www.stormware.cz/schema/version_2/invoice.xsd";
    private static readonly XNamespace Typ = "http://www.stormware.cz/schema/version_2/type.xsd";

    public static (int Kept, int Total)? Apply(XDocument document)
    {
        var invoices = document.Descendants(Lst + "invoice").ToList();
        if (invoices.Count == 0)
        {
            return null;
        }

        var keep = new HashSet<XElement>(invoices.Where(i => i.Element(Inv + "attachments") is not null));
        var others = invoices.Where(i => !keep.Contains(i)).ToList();

        Func<XElement, bool>[] typical =
        [
            i => Header(i)?.Element(Inv + "number")?.Element(Typ + "ids") is null,
            i => Summary(i)?.Element(Inv + "foreignCurrency") is not null,
            i => Header(i)?.Element(Inv + "liquidation") is null,
            i => Summary(i)?.Element(Inv + "homeCurrency")?.Elements().Any(e => e.Value.StartsWith('-')) == true,
            i => Header(i)?.Element(Inv + "partnerIdentity")?.Descendants(Typ + "ico").Any() != true,
            i => i.Element(Inv + "invoiceDetail") is not null,
            i => i.Element(Inv + "linkedDocuments") is not null,
            i => Header(i)?.Element(Inv + "intNote") is not null,
        ];
        foreach (var predicate in typical)
        {
            if (others.FirstOrDefault(predicate) is { } match)
            {
                keep.Add(match);
            }
        }

        var step = Math.Max(1, others.Count / EvenlySpaced);
        for (var i = 0; i < others.Count; i += step)
        {
            keep.Add(others[i]);
        }

        foreach (var invoice in invoices.Where(i => !keep.Contains(i)))
        {
            if (invoice.PreviousNode is XText whitespace && string.IsNullOrWhiteSpace(whitespace.Value))
            {
                whitespace.Remove();
            }

            invoice.Remove();
        }

        return (keep.Count, invoices.Count);
    }

    private static XElement? Header(XElement invoice) => invoice.Element(Inv + "invoiceHeader");

    private static XElement? Summary(XElement invoice) => invoice.Element(Inv + "invoiceSummary");
}

/// <summary>Deterministická anonymizace listových hodnot a atributů.</summary>
internal sealed partial class Anonymizer
{
    // Neosobní pole, která se ponechají beze změny (lokální název elementu).
    private static readonly HashSet<string> KeptElements =
    [
        "id", "ids", "version", "classificationVATType", "sourceAgenda", "numberRequested", "number",
        "invoiceType", "paymentType", "symConst", "subFolder", "type", "VATPayerType",
        "date", "dateAccounting", "dateDue", "dateKHDPH", "dateTax", "dateFrom", "dateTo", "dateDelivery", "dateApplicationVAT",
        "year", "yearType", "stateType", "unitType",
        "lock1", "lock2", "markRecord", "payVAT", "rateVAT", "percentVAT", "coefficient", "discountPercentage", "quantity", "unit",
        "calculateVAT", "roundingDocument", "roundingVAT", "typeCalculateVATForeignCurrency", "typeCalculateVATInclusivePrice",
        "amount", "rate", "price", "priceSum", "priceVAT", "unitPrice", "priceRound", "amountHome", "amountForeign",
        "priceNone", "priceLow", "priceLowVAT", "priceLowSum", "priceHigh", "priceHighVAT", "priceHighSum",
        "price3", "price3VAT", "price3Sum",
        "state", "errno",
    ];

    private static readonly HashSet<string> KeptAttributes =
    [
        "version", "id", "state", "note", "programVersion", "dateTimeStamp", "dateValidFrom", "rate", "value", "application",
    ];

    private readonly Dictionary<(string Category, string Value), string> _map = [];
    private readonly Dictionary<string, int> _counters = [];

    public int Count => _map.Count;

    public void Apply(XDocument document)
    {
        foreach (var element in document.Descendants().Where(e => !e.HasElements).ToList())
        {
            if (element.Value.Length > 0)
            {
                element.Value = AnonymizeElement(element);
            }
        }

        foreach (var attribute in document.Descendants().Attributes().Where(a => !a.IsNamespaceDeclaration).ToList())
        {
            attribute.Value = AnonymizeAttribute(attribute);
        }
    }

    /// <summary>
    /// Původní citlivé hodnoty (aspoň 4 znaky), které se objevily ve výstupu. Dlouhé hodnoty se hledají
    /// kdekoli (i uvnitř jiné hodnoty), krátké jen jako samostatný token, protože např. variabilní symbol
    /// „0066“ je legitimně součástí ponechaného čísla dokladu „261100066“.
    /// </summary>
    public List<string> FindLeaks(IEnumerable<(string Path, string Text)> outputs) =>
        [
            .. from output in outputs
               from entry in _map
               let original = entry.Key.Value
               where original.Length >= 4
               let index = IndexOf(output.Text, original)
               where index >= 0
               select $"{Path.GetFileName(output.Path)}: {entry.Key.Category} „{original}“ v …{Context(output.Text, index)}…",
        ];

    private const int SubstringLeakMinLength = 6;

    private static int IndexOf(string text, string original) =>
        original.Length >= SubstringLeakMinLength
            ? text.IndexOf(original, StringComparison.Ordinal)
            : Regex.Match(text, $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(original)}(?![\p{{L}}\p{{N}}])", RegexOptions.CultureInvariant) is { Success: true } match
                ? match.Index
                : -1;

    private static string Context(string text, int index)
    {
        var start = Math.Max(0, index - 60);
        return Regex.Replace(text[start..Math.Min(text.Length, index + 40)], @"\s+", " ");
    }

    private string AnonymizeElement(XElement element)
    {
        var name = element.Name.LocalName;
        var parent = element.Parent?.Name.LocalName;
        var value = element.Value;

        if (parent is "address" or "establishment")
        {
            return AnonymizeAddress(name, value, parent);
        }

        return (parent, name) switch
        {
            (_, "dataFile") => DataFile(value),
            (_, "key") => Key(value),
            (_, "companyDocumentsFolder") => CompanyFolder(value),
            ("file", "name") => FileName(value),
            ("invoiceHeader", "text") => Map("headerText", value, n => $"Popis dokladu {n}"),
            ("invoiceItem", "text") => Map("itemText", value, n => $"Položka č. {n}"),
            (_, "note") => Map("note", value, n => $"Poznámka {n}"),
            (_, "intNote") => Map("intNote", value, n => $"Interní poznámka {n}"),
            // Bez oddělovače: „DOD-0066“ by kontrola úniků zaměnila s původním číslem „0066“.
            (_, "originalDocument") => Map("originalDocument", value, n => $"DOD{n:00000}"),
            (_, "symVar" or "symPar") => Map("symVar", value, n => SameLengthNumber(n, value.Length)),
            ("paymentAccount" or "account", "accountNo") => Map("accountNo", value, n => $"000000-{n:0000000000}"),
            ("paymentAccount" or "account", "bankCode") => "0000",
            _ when KeptElements.Contains(name) => value,
            _ => throw new InvalidOperationException(
                $"Neznámé pole {parent}/{name} = „{value}“. Doplň ho do anonymizace, nebo do seznamu ponechaných polí."),
        };
    }

    private string AnonymizeAddress(string name, string value, string parent) =>
        name switch
        {
            "company" => Map("company", value, n => $"Firma Šťastná {n:000}"),
            "name" => Map("name", value, n => $"Jméno{n}"),
            "surname" => Map("surname", value, n => $"Příjmení{n}"),
            "title" => string.Empty,
            "city" => Map("city", value, n => $"Město {n}"),
            "street" => Map("street", value, n => $"Ulice {n}"),
            "number" => Map("houseNumber", value, n => n.ToString(CultureInfo.InvariantCulture)),
            "zip" => Map("zip", value, n => $"{100 + n:000} 00"),
            "ico" => Ico(value),
            "dic" => Dic(value),
            "email" => Map("email", value, n => $"kontakt{n}@example.cz"),
            "phone" or "mobilPhone" or "fax" => Map("phone", value, n => $"600 000 {n:000}"),
            "www" => Map("www", value, n => $"www.example{n}.cz"),
            "VATPayerType" => value,
            _ => throw new InvalidOperationException($"Neznámé pole adresy {parent}/{name} = „{value}“."),
        };

    private string AnonymizeAttribute(XAttribute attribute) =>
        (attribute.Parent?.Name.LocalName, attribute.Name.LocalName) switch
        {
            (_, "ico") => Ico(attribute.Value),
            (_, "key") => Key(attribute.Value),
            (_, var name) when KeptAttributes.Contains(name) => attribute.Value,
            var (element, name) => throw new InvalidOperationException($"Neznámý atribut {element}/@{name} = „{attribute.Value}“."),
        };

    private string Ico(string value) => Map("ico", value, n => $"9{n:0000000}");

    private string Dic(string value)
    {
        var match = VatNumber().Match(value);
        if (!match.Success)
        {
            return Map("dic", value, n => $"XX{n:00000000}");
        }

        var (prefix, digits) = (match.Groups["prefix"].Value, match.Groups["digits"].Value);
        return prefix == "CZ" && digits.Length == 8
            ? prefix + Ico(digits)
            : prefix + Map("dicDigits", digits, n => SameLengthNumber(n, digits.Length));
    }

    private string DataFile(string value)
    {
        var match = DataFileName().Match(value);
        return match.Success
            ? $"{Ico(match.Groups["ico"].Value)}_{match.Groups["year"].Value}.mdb"
            : Map("dataFile", value, n => $"jednotka{n}.mdb");
    }

    private string Key(string value) => Map("key", value, n => $"00000000-0000-4000-8000-{n:000000000000}");

    private string CompanyFolder(string value)
    {
        var trimmed = value.TrimEnd('\\');
        var separator = trimmed.LastIndexOf('\\');
        var folder = trimmed[(separator + 1)..];
        var mapped = Map("folder", folder, n => $"Složka firmy {n:00}");
        return string.Concat(trimmed.AsSpan(0, separator + 1), mapped, value.EndsWith('\\') ? "\\" : string.Empty);
    }

    private string FileName(string value) =>
        DocumentNumberFile().IsMatch(value)
            ? value
            : Map("file", value, n => $"Sken {n}{Path.GetExtension(value)}");

    private string Map(string category, string value, Func<int, string> create)
    {
        if (_map.TryGetValue((category, value), out var mapped))
        {
            return mapped;
        }

        var next = _counters[category] = _counters.GetValueOrDefault(category) + 1;
        mapped = create(next);
        _map[(category, value)] = mapped;
        return mapped;
    }

    private static string SameLengthNumber(int n, int length) =>
        ("9" + n.ToString(CultureInfo.InvariantCulture).PadLeft(Math.Max(length - 1, 1), '0'))[..Math.Max(length, 2)];

    [GeneratedRegex(@"^(?<prefix>[A-Z]{2})(?<digits>\d+)$")]
    private static partial Regex VatNumber();

    [GeneratedRegex(@"^(?<ico>\d{8})_(?<year>\d{4})\.mdb$", RegexOptions.IgnoreCase)]
    private static partial Regex DataFileName();

    // Soubor pojmenovaný jen číslem dokladu (případně s krátkým písmenným prefixem) nic osobního nenese.
    [GeneratedRegex(@"^[A-Za-z]{0,2}\d{6,}\.\w+$")]
    private static partial Regex DocumentNumberFile();
}
