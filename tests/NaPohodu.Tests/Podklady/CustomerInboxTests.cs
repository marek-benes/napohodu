using NaPohodu.Core.Filing;
using NaPohodu.Core.Podklady;
using NaPohodu.Tests.Filing;

namespace NaPohodu.Tests.Podklady;

public sealed class CustomerInboxTests : IDisposable
{
    private readonly TempDirectory _temp = new();

    public void Dispose() => _temp.Dispose();

    [Fact]
    public void Resolve_UsesCompanyFolderName()
    {
        var root = _temp.Dir("Podklady");
        var inbox = _temp.Dir("Podklady", "Firma s.r.o", "Ke zpracování");

        var result = CustomerInbox.Resolve(root, Company(@"C:\ProgramData\STORMWARE\POHODA\Dokumenty\Firma s.r.o\"));

        Assert.Equal(inbox, result.Path);
    }

    [Fact]
    public void Resolve_RootNotConfigured()
    {
        var result = CustomerInbox.Resolve(" ", Company(@"C:\Dokumenty\Firma"));

        Assert.False(result.IsResolved);
        Assert.Contains("Podklady:RootPath", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_RootMissing()
    {
        var result = CustomerInbox.Resolve(Path.Combine(_temp.Path, "neexistuje"), Company(@"C:\Dokumenty\Firma"));

        Assert.False(result.IsResolved);
        Assert.Contains("neexistuje", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CompanyFolderUnknown()
    {
        var result = CustomerInbox.Resolve(_temp.Path, new CompanyFolderResolution(null, "nenalezeno", []));

        Assert.False(result.IsResolved);
        Assert.Contains("složka firmy", result.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_CustomerFolderMissing()
    {
        var result = CustomerInbox.Resolve(_temp.Path, Company(@"C:\Dokumenty\Firma"));

        Assert.False(result.IsResolved);
        Assert.Contains("chybí složka zákazníka „Firma“", result.Source, StringComparison.Ordinal);
    }

    // Tureon a Milan Burda (2026-10-18): složka zákazníka je, Ke zpracování ne.
    [Fact]
    public void Resolve_InboxMissing()
    {
        _temp.Dir("Firma", "Šanony");

        var result = CustomerInbox.Resolve(_temp.Path, Company(@"C:\Dokumenty\Firma"));

        Assert.False(result.IsResolved);
        Assert.Contains("nemá v Podkladech složku Ke zpracování", result.Source, StringComparison.Ordinal);
    }

    private static CompanyFolderResolution Company(string path) => new(path, "test", []);
}
