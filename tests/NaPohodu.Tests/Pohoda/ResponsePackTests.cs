using System.Xml.Linq;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.Tests.Pohoda;

public class ResponsePackTests
{
    private const string Namespaces =
        """xmlns:rsp="http://www.stormware.cz/schema/version_2/response.xsd" xmlns:rdc="http://www.stormware.cz/schema/version_2/documentresponse.xsd" xmlns:lst="http://www.stormware.cz/schema/version_2/list.xsd" """;

    [Fact]
    public void GetList_ReturnsListOnSuccess()
    {
        var response = Response("""<rsp:responsePackItem version="2.0" id="1" state="ok"><lst:listInvoice version="2.0" state="ok"/></rsp:responsePackItem>""");

        var list = ResponsePack.GetList(response, InvoiceParser.ListName);

        Assert.Equal(InvoiceParser.ListName, list.Name);
    }

    [Fact]
    public void GetList_PackError_ThrowsWithNote()
    {
        var response = Response(string.Empty, packState: "error", packNote: "Chyba přihlášení");

        var ex = Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));

        Assert.Contains("Chyba přihlášení", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetList_ItemError_ThrowsWithNote()
    {
        var response = Response("""<rsp:responsePackItem version="2.0" id="1" state="error" note="Účetní jednotka neexistuje"/>""");

        var ex = Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));

        Assert.Contains("Účetní jednotka neexistuje", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetList_ListError_ThrowsWithImportDetails()
    {
        var response = Response(
            """
            <rsp:responsePackItem version="2.0" id="1" state="ok">
              <lst:listInvoice version="2.0" state="error">
                <rdc:importDetails>
                  <rdc:detail><rdc:state>warning</rdc:state><rdc:note>jen varování</rdc:note></rdc:detail>
                  <rdc:detail><rdc:state>error</rdc:state><rdc:errno>105</rdc:errno><rdc:note>Chybný filtr</rdc:note></rdc:detail>
                </rdc:importDetails>
              </lst:listInvoice>
            </rsp:responsePackItem>
            """);

        var ex = Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));

        Assert.Contains("Chybný filtr (chyba 105)", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("jen varování", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetList_MissingList_ThrowsInsteadOfEmpty()
    {
        var response = Response("""<rsp:responsePackItem version="2.0" id="1" state="ok"/>""");

        Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));
    }

    [Fact]
    public void GetList_MissingItem_Throws()
    {
        var response = Response(string.Empty);

        Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));
    }

    [Fact]
    public void GetList_SplitExport_Throws()
    {
        var response = Response(
            """<rsp:responsePackItem version="2.0" id="1" state="ok"><lst:listInvoice version="2.0" state="ok"><rdc:parts><rdc:part>a_1.xml</rdc:part></rdc:parts></lst:listInvoice></rsp:responsePackItem>""");

        var ex = Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));

        Assert.Contains("rdc:parts", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void GetList_UnexpectedRoot_Throws()
    {
        var response = XDocument.Parse("<html/>");

        Assert.Throws<PohodaException>(() => ResponsePack.GetList(response, InvoiceParser.ListName));
    }

    private static XDocument Response(string items, string packState = "ok", string packNote = "") =>
        XDocument.Parse($"""<rsp:responsePack version="2.0" id="1" state="{packState}" note="{packNote}" {Namespaces}>{items}</rsp:responsePack>""");
}
