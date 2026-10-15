using NaPohodu.Core.Pohoda;
using NaPohodu.Tests.Fixtures;

namespace NaPohodu.Tests.Pohoda;

public class AccountingUnitParserTests
{
    [Fact]
    public void Parse_ReadsUnitsWithCzechTexts()
    {
        var units = AccountingUnitParser.Parse(FixtureFiles.LoadResponse("Synthetic/listAccountingUnit.response.xml"));

        Assert.Equal(2, units.Count);

        var company = units[0];
        Assert.Equal("StwPh_12345678_2026", company.DataFile);
        Assert.Equal(2026, company.Year);
        Assert.Equal(new DateOnly(2026, 1, 1), company.DateFrom);
        Assert.Equal(new DateOnly(2026, 12, 31), company.DateTo);
        Assert.Equal("doubleEntry", company.UnitType);
        Assert.Equal("legalPerson", company.StateType);
        Assert.Equal("Žluťoučký kůň s.r.o.", company.Company);
        Assert.Equal("Žluťoučký kůň s.r.o.", company.DisplayName);
        Assert.Equal("12345678", company.Ico);
        Assert.Equal("CZ12345678", company.Dic);
        Assert.Equal("Náměstí 12", company.Street);
        Assert.Equal("Česká Lípa", company.City);
        Assert.Equal("a1b2c3", company.Key);
    }

    [Fact]
    public void Parse_NaturalPersonWithoutCompany_UsesPersonName()
    {
        var units = AccountingUnitParser.Parse(FixtureFiles.LoadResponse("Synthetic/listAccountingUnit.response.xml"));

        var person = units[1];
        Assert.Null(person.Company);
        Assert.Equal("Jana Dvořáková", person.PersonName);
        Assert.Equal("Jana Dvořáková", person.DisplayName);
        Assert.Null(person.Key);
    }
}
