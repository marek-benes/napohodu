using System.Xml.Linq;

namespace NaPohodu.Core.Pohoda;

/// <summary>Jedno volání Pohoda XML API: požadavek (dataPack) → odpověď (responsePack).</summary>
public interface IPohodaXmlRunner
{
    /// <param name="request">Vstupní <c>dat:dataPack</c>.</param>
    /// <param name="database">Název datového souboru jednotky; <c>null</c> jen pro seznam účetních jednotek.</param>
    Task<PohodaXmlResponse> RunAsync(XDocument request, string? database, CancellationToken cancellationToken = default);
}

/// <summary>Odpověď Pohody: naparsovaný dokument a původní bajty (pro uložení jako fixture).</summary>
public sealed record PohodaXmlResponse(XDocument Document, byte[] RawBytes);
