using System.Xml.Linq;
using NaPohodu.Core.Models;
using static NaPohodu.Core.Pohoda.PohodaNamespaces;

namespace NaPohodu.Core.Pohoda;

/// <summary>Parser odpovědi na <c>acu:listAccountingUnitRequest</c>.</summary>
public static class AccountingUnitParser
{
    public static readonly XName ListName = Acu + "listAccountingUnit";

    public static IReadOnlyList<AccountingUnit> Parse(XDocument response) =>
        [.. ResponsePack.GetList(response, ListName).Elements(Acu + "itemAccountingUnit").Select(ParseItem)];

    private static AccountingUnit ParseItem(XElement item)
    {
        // accountingUnitIdentity = typ:myAddress → typ:address (typ:addressInternetType)
        var address = item.Element(Acu + "accountingUnitIdentity")?.Element(Typ + "address");

        return new AccountingUnit
        {
            DataFile = item.Required(Acu + "dataFile"),
            Year = item.Int(Acu + "year"),
            YearType = item.Text(Acu + "yearType"),
            DateFrom = item.Date(Acu + "dateFrom"),
            DateTo = item.Date(Acu + "dateTo"),
            UnitType = item.Text(Acu + "unitType"),
            StateType = item.Text(Acu + "stateType"),
            Key = item.Text(Acu + "key"),
            Company = address.Text(Typ + "company"),
            PersonName = JoinName(address.Text(Typ + "name"), address.Text(Typ + "surname")),
            Ico = address.Text(Typ + "ico"),
            Dic = address.Text(Typ + "dic"),
            Street = JoinName(address.Text(Typ + "street"), address.Text(Typ + "number")),
            City = address.Text(Typ + "city"),
            Zip = address.Text(Typ + "zip"),
        };
    }

    private static string? JoinName(string? first, string? second) =>
        (first, second) switch
        {
            (null, null) => null,
            (null, _) => second,
            (_, null) => first,
            _ => $"{first} {second}",
        };
}
