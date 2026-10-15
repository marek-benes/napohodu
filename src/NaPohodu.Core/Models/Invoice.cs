namespace NaPohodu.Core.Models;

/// <summary>
/// Faktura z exportu <c>lst:listInvoice</c> (<c>inv:invoiceType</c>), zjednodušená na to,
/// co potřebuje zatřídění a přehledy. Mapování na XSD je u jednotlivých vlastností.
/// </summary>
public sealed record Invoice
{
    /// <summary><c>inv:id</c> = <c>FA.ID</c> v MDB (vazba <c>DocDirs.RelID</c>).</summary>
    public required int Id { get; init; }

    /// <summary><c>inv:invoiceType</c>, např. <c>receivedInvoice</c>.</summary>
    public required string InvoiceType { get; init; }

    /// <summary>Číslo dokladu (<c>inv:number/typ:numberRequested</c>).</summary>
    public required string Number { get; init; }

    /// <summary>Prefix číselné řady (<c>inv:number/typ:ids</c>), pokud ho Pohoda vrátí.</summary>
    public string? NumberSeries { get; init; }

    /// <summary>Číslo dokladu dodavatele (<c>inv:originalDocument</c>).</summary>
    public string? OriginalDocument { get; init; }

    public string? SymVar { get; init; }

    public DateOnly? Date { get; init; }

    public DateOnly? DateTax { get; init; }

    public DateOnly? DateDue { get; init; }

    public string? Text { get; init; }

    public string? PartnerCompany { get; init; }

    public string? PartnerName { get; init; }

    public string? PartnerIco { get; init; }

    /// <summary>
    /// Celkem v domácí měně: <c>priceNone + priceLowSum + priceHighSum + price3Sum + round/priceRound</c>.
    /// </summary>
    public decimal TotalHome { get; init; }

    /// <summary>Celkem v cizí měně (<c>foreignCurrency/priceSum</c>), pokud je faktura v cizí měně.</summary>
    public ForeignAmount? TotalForeign { get; init; }

    /// <summary>Datum likvidace (<c>inv:liquidation/typ:date</c>); <c>null</c> = neuhrazeno.</summary>
    public DateOnly? LiquidationDate { get; init; }

    /// <summary>Záložka Dokumenty (<c>inv:attachments/typ:files</c>), jen při <c>attachments=true</c>.</summary>
    public InvoiceDocuments? Documents { get; init; }

    /// <summary>Jméno partnera pro zobrazení.</summary>
    public string? PartnerDisplayName => PartnerCompany ?? PartnerName;
}

/// <summary>Částka v cizí měně; <see cref="Currency"/> je kód z <c>currency/ids</c>.</summary>
public sealed record ForeignAmount(string Currency, decimal Total);

/// <summary>
/// Záložka Dokumenty faktury. Pohoda ji posílá jen u faktur, které nějaký dokument mají.
/// <see cref="SubFolder"/> je adresář dokladu relativně ke složce firmy
/// (např. <c>Fakturace\Přijaté faktury\2511\251100002</c>, výjimečně plochý <c>…\Přijaté faktury\251100001</c>).
/// </summary>
public sealed record InvoiceDocuments
{
    /// <summary>
    /// Absolutní cesta ke složce firmy v Dokumentech včetně koncového <c>\</c>
    /// (např. <c>C:\ProgramData\STORMWARE\POHODA\Dokumenty\SafetyLine\</c>).
    /// </summary>
    public string? CompanyDocumentsFolder { get; init; }

    public string? SubFolder { get; init; }

    public IReadOnlyList<string> Files { get; init; } = [];
}
