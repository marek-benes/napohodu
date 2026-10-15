using System.Xml.Linq;
using NaPohodu.Core.Pohoda;

namespace NaPohodu.Tests.Fixtures;

internal static class FixtureFiles
{
    public static byte[] ReadBytes(string relativePath) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", relativePath));

    /// <summary>Načte odpověď stejně jako runner (dekódování podle deklarace, fallback windows-1250).</summary>
    public static XDocument LoadResponse(string relativePath) =>
        PohodaXmlRunner.ParseResponse(ReadBytes(relativePath));

    /// <summary>Kořen repozitáře (kvůli XSD v docs/pohoda/schemas).</summary>
    public static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "NaPohodu.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new InvalidOperationException("Nenalezen kořen repozitáře (NaPohodu.slnx).");
    }
}
