using NaPohodu.Core.Models;
using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Pohoda;

public class AccountingUnitParserTests
{
    private static IReadOnlyList<AccountingUnit> Units() =>
        AccountingUnitParser.Parse(FixtureFiles.LoadResponse(RealFixtures.AccountingUnits));

    [Fact]
    public void Parse_ReadsAllUnits()
    {
        var units = Units();

        Assert.Equal(83, units.Count);
        Assert.All(units, u => Assert.EndsWith(".mdb", u.DataFile, StringComparison.Ordinal));
        Assert.Equal(83, units.Select(u => u.DataFile).Distinct().Count());
    }

    [Fact]
    public void Parse_ReadsUnitFields()
    {
        var unit = Units().Single(u => u.DataFile == RealFixtures.UnitDataFile2025);

        Assert.Equal(2025, unit.Year);
        Assert.Equal("calendar", unit.YearType);
        Assert.Equal(new DateOnly(2025, 1, 1), unit.DateFrom);
        Assert.Equal(new DateOnly(2025, 12, 31), unit.DateTo);
        Assert.Equal("doubleEntry", unit.UnitType);
        Assert.Equal("Firma Šťastná 011", unit.Company);
        Assert.Equal(RealFixtures.UnitIco, unit.Ico);
        Assert.Equal("CZ90000010", unit.Dic);
        Assert.Equal("Ulice 10 9", unit.Street);
        Assert.Equal("Město 12", unit.City);
        Assert.Equal("00000000-0000-4000-8000-000000000010", unit.Key);
    }

    [Fact]
    public void Parse_SameCompanyHasOneUnitPerYear()
    {
        var years = Units().Where(u => u.Ico == RealFixtures.UnitIco).Select(u => u.Year).Order().ToList();

        Assert.Equal([2019, 2020, 2021, 2022, 2023, 2024, 2025, 2026], years);
    }

    [Fact]
    public void Parse_EmptyElementsAreMissingValues()
    {
        // Pohoda posílá prázdné <typ:name/>, <typ:surname/>, <typ:title/> …; nesmí z nich být prázdné řetězce.
        var unit = Units().Single(u => u.DataFile == RealFixtures.UnitDataFile2025);

        Assert.Null(unit.PersonName);
    }

    [Fact]
    public void Parse_PersonNameFromNameAndSurname()
    {
        var unit = Units().Single(u => u.DataFile == "90000003_2025.mdb");

        Assert.Equal("Jméno1 Příjmení2", unit.PersonName);
        Assert.Equal("Firma Šťastná 003", unit.DisplayName);
    }

    [Fact]
    public void DisplayName_FallsBackToPersonThenDataFile()
    {
        Assert.Equal("Jana Nováková", new AccountingUnit { DataFile = "x.mdb", PersonName = "Jana Nováková" }.DisplayName);
        Assert.Equal("x.mdb", new AccountingUnit { DataFile = "x.mdb" }.DisplayName);
    }
}
