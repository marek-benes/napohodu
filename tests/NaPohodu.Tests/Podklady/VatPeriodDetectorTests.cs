using NaPohodu.Core.Podklady;
using NaPohodu.Tests.Filing;

namespace NaPohodu.Tests.Podklady;

public sealed class VatPeriodDetectorTests : IDisposable
{
    private readonly TempDirectory _company = new();

    public void Dispose() => _company.Dispose();

    // Tvary názvů z Dokumentů Pohody (2026-10-18), IČO vymyšlená.
    [Theory]
    [InlineData("DPHDP-12345678-2026-zari_radne_09.10.2026_Veta.xml", VatPeriod.Monthly)]
    [InlineData("DPHDP-12345678-2025-cervenec_radne_06.08.2025_Veta.xml", VatPeriod.Monthly)]
    [InlineData("DPHDP-12345678-2026-3q_radne_09.10.2026_Veta.xml", VatPeriod.Quarterly)]
    [InlineData("DPHDP-12345678-2021-1Q_radne_11.04.2021_Veta.xml", VatPeriod.Quarterly)]
    public void Parse(string fileName, VatPeriod expected)
    {
        Assert.Equal(expected, VatPeriodDetector.Parse(fileName)?.Period);
    }

    [Theory]
    [InlineData("DPHKH-12345678-2026-zari_radne_09.10.2026_Veta.xml")]
    [InlineData("DPHDP-12345678-2026-5q_radne_09.10.2026_Veta.xml")]
    [InlineData("DPHDP-12345678-2026-nekdy_radne.xml")]
    public void Parse_Ignores(string fileName)
    {
        Assert.Null(VatPeriodDetector.Parse(fileName));
    }

    [Fact]
    public void FromFilings_UsesLatestFromElvykazyAndIsds()
    {
        _company.File("x", "ELvykazy", "DPH", "DPHDP-1-2025-prosinec_radne_20.01.2026_Veta.xml");
        _company.File("x", "ISDS", "abc1234", "1768425779", "DPHDP-1-2026-1q_radne_20.04.2026_Veta.xml");
        _company.File("x", "ISDS", "abc1234", "1768425780", "DPHKH-1-2026-zari_radne_09.10.2026_Veta.xml");

        var result = VatPeriodDetector.FromFilings(_company.Path, 2026);

        Assert.Equal(VatPeriod.Quarterly, result?.Period);
        Assert.Contains("DPHDP-1-2026-1q", result?.Source, StringComparison.Ordinal);
    }

    [Fact]
    public void FromFilings_PrefersYearsUpToTarget()
    {
        _company.File("x", "ELvykazy", "DPH", "DPHDP-1-2025-4q_radne_20.01.2026_Veta.xml");
        _company.File("x", "ELvykazy", "DPH", "DPHDP-1-2026-leden_radne_20.02.2026_Veta.xml");

        Assert.Equal(VatPeriod.Quarterly, VatPeriodDetector.FromFilings(_company.Path, 2025)?.Period);
        Assert.Equal(VatPeriod.Monthly, VatPeriodDetector.FromFilings(_company.Path, 2026)?.Period);
        Assert.Equal(VatPeriod.Monthly, VatPeriodDetector.FromFilings(_company.Path, 2024)?.Period);
    }

    [Fact]
    public void FromFilings_NoneForNonPayer()
    {
        _company.Dir("ELvykazy", "VSD");

        Assert.Null(VatPeriodDetector.FromFilings(_company.Path, 2026));
    }
}
