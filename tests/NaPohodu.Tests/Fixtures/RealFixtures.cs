namespace NaPohodu.Tests.Fixtures;

/// <summary>
/// Anonymizované reálné odpovědi Pohody 14400.171 (tools/anonymize-fixture.cs). Hodnoty v testech
/// pocházejí z nich; jednotka 90000010 je firma, jejíž faktury jsou v exportech.
/// </summary>
internal static class RealFixtures
{
    public const string AccountingUnits = "Real/listAccountingUnit.response.xml";

    /// <summary>Vzorek 53 z 484 přijatých faktur, 36 se záložkou Dokumenty.</summary>
    public const string ReceivedInvoices2025 = "Real/listInvoice.received.2025.response.xml";

    /// <summary>Vzorek 16 z 248 přijatých faktur; žádná nemá dokumenty.</summary>
    public const string ReceivedInvoices2026WithoutDocuments = "Real/listInvoice.received.2026-without-documents.response.xml";

    /// <summary>Odmítnutý balíček bez <c>ico</c> v dataPack.</summary>
    public const string ErrorNotForThisUnit = "Real/error.not-for-this-unit.response.xml";

    public const string UnitIco = "90000010";

    public const string UnitDataFile2025 = "90000010_2025.mdb";

    public const string CompanyDocumentsFolder = @"C:\ProgramData\STORMWARE\POHODA\Dokumenty\Složka firmy 01\";
}
