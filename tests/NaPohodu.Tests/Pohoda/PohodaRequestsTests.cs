using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using NaPohodu.Core.Pohoda;
using NaPohodu.Core.Text;
using NaPohodu.Tests.Fixtures;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Tests.Pohoda;

public class PohodaRequestsTests
{
    private static readonly Lazy<XmlSchemaSet> _schemas = new(LoadSchemas);
    private static readonly UnitIdentity _ico = new("12345678", key: null);

    [Fact]
    public void ListAccountingUnits_HasEnvelopeWithoutIco()
    {
        var request = PohodaRequests.ListAccountingUnits();

        var pack = request.Root!;
        Assert.Equal(Dat + "dataPack", pack.Name);
        Assert.Null(pack.Attribute("ico"));
        Assert.Equal(DataPack.Application, pack.Attribute("application")?.Value);
        Assert.NotNull(pack.Attribute("note"));
        Assert.Equal(Acu + "listAccountingUnitRequest", Content(request).Name);
    }

    [Fact]
    public void ListReceivedInvoices_RequestsAttachmentsAndReceivedType()
    {
        var request = PohodaRequests.ListReceivedInvoices(_ico);

        var list = Content(request);
        Assert.Equal(Lst + "listInvoiceRequest", list.Name);
        Assert.Equal("receivedInvoice", list.Attribute("invoiceType")?.Value);
        Assert.Equal("true", list.Element(Lst + "restrictionData")?.Element(Lst + "attachments")?.Value);
        Assert.Null(list.Element(Lst + "limit"));
        Assert.NotNull(list.Element(Lst + "requestInvoice")?.Element(Ftr + "filter"));
    }

    [Fact]
    public void ListReceivedInvoices_WritesDateFilterAndIco()
    {
        var request = PohodaRequests.ListReceivedInvoices(
            _ico,
            new InvoiceFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31)));

        var filter = Content(request).Element(Lst + "requestInvoice")!.Element(Ftr + "filter")!;
        Assert.Equal("2026-01-01", filter.Element(Ftr + "dateFrom")?.Value);
        Assert.Equal("2026-03-31", filter.Element(Ftr + "dateTill")?.Value);
        Assert.Equal("12345678", request.Root!.Attribute("ico")?.Value);
        Assert.Null(request.Root!.Attribute("key"));
    }

    [Fact]
    public void ListReceivedInvoices_UnitWithoutIco_UsesKey()
    {
        var request = PohodaRequests.ListReceivedInvoices(new UnitIdentity(ico: null, key: "0dae2f2b"));

        Assert.Null(request.Root!.Attribute("ico"));
        Assert.Equal("0dae2f2b", request.Root!.Attribute("key")?.Value);
    }

    [Fact]
    public void UnitIdentity_WithoutIcoAndKey_Throws()
    {
        Assert.Throws<ArgumentException>(() => new UnitIdentity(" ", null));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Requests_AreValidAgainstOfficialSchemas(bool withFilterAndKey)
    {
        XDocument[] requests =
        [
            PohodaRequests.ListAccountingUnits(),
            PohodaRequests.ListReceivedInvoices(
                new UnitIdentity("12345678", withFilterAndKey ? "0dae2f2b-ee10-43ac-babe-18971ec01786" : null),
                withFilterAndKey ? new InvoiceFilter(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31)) : null),
        ];

        foreach (var request in requests)
        {
            // Obálka; obsah dataPackItem je xsd:any processContents="skip", proto ho validujeme zvlášť.
            Validate(request);
            Validate(new XDocument(new XElement(Content(request))));
        }
    }

    private static XElement Content(XDocument request) =>
        request.Root!.Element(Dat + "dataPackItem")!.Elements().Single();

    private static void Validate(XDocument document)
    {
        var errors = new List<string>();
        document.Validate(_schemas.Value, (_, e) => errors.Add(e.Message));
        Assert.Empty(errors);
    }

    private static XmlSchemaSet LoadSchemas()
    {
        // XSD jsou ve windows-1250; bez registrace code pages je XmlReader nepřečte.
        _ = PohodaEncoding.Windows1250;

        var dir = Path.Combine(FixtureFiles.RepositoryRoot(), "docs", "pohoda", "schemas");
        var set = new XmlSchemaSet { XmlResolver = new XmlUrlResolver() };
        foreach (var name in new[] { "data-package.xsd", "list.xsd", "accountingunit.xsd" })
        {
            set.Add(null, Path.Combine(dir, name));
        }

        set.Compile();
        return set;
    }
}
