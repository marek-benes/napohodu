# Podklady: inbox a Šanony

Návrh odsouhlasený uživatelem 2026-10-18. Řeší, aby účetní při zpracování vstupních souborů nevybírala složky: vše se odvodí ze zvoleného zákazníka.

## Výchozí stav (2026-10-18)

- Sdílený disk `G:\Sdílené disky\Podklady\<Firma>\` (Google Drive), u každého zákazníka `Ke zpracování` a `Šanony\<RRRR>\…`.
- Název složky zákazníka = **přesně** název složky firmy v Dokumentech Pohody (ověřeno na všech 7 zákaznících v Podkladech).
- `Ke zpracování` je současně inbox i odkladiště: účetní zadá fakturu do Pohody a soubor přejmenuje na přidělené číslo, aby ho zatřídění spárovalo. Nevyhovující.
- Šanony jsou členěné u každého zákazníka jinak (`PF 092026`, `PF1.Q`, `1.Q`, `01`, `Přijaté faktury/01`, `MZDY`/`MZDY 2026`/`MZDY+podklady`), místy volné soubory v roce a zipy. `Tureon` a `Milan Burda` nemají `Ke zpracování`.
- Zdroj pro zatřídění se v UI zadává ručně (`Filing.razor`).

## Zásady

- **Cesty skládá aplikace.** Konfigurace obsahuje jen kořen Podkladů (`Podklady:RootPath`). Inbox zákazníka = `<RootPath>\<složka firmy>\Ke zpracování`, kde složka firmy je název složky firmy v Dokumentech (`companyDocumentsFolder` / `CompanyFolderResolver`).
- **Strukturu zakládá aplikace** (inbox, roky, období, měsíce mezd). Nikdo nezakládá vlastní složky.
- **Originály jsou v Dokumentech Pohody**, Šanony jsou jejich exportovaný obraz pro zákazníky.

## Struktura

```
Podklady\<Firma>\                 = název složky firmy v Pohoda\Dokumenty
  Ke zpracování\                  píše jen zákazník (cokoliv, netřídí)
    Vráceno\                      soubory, které nejde zpracovat + <soubor>.txt s důvodem
  Šanony\
    <RRRR>\
      <období>\                   01…12 nebo Q1…Q4 podle periodicity DPH zákazníka
        Přijaté faktury\          období podle DUZP
        Vydané faktury\
        Banka\                    podle data výpisu
        Pokladna\
        DPH\                      přiznání, KH, SH (XML z ELvykazy + PDF) + doručenky z ISDS
      Mzdy\<MM>\                  vždy měsíčně: výplatní pásky, rekapitulace, podklady, ZP/ČSSZ + doručenky
      Mzdy\Mzdové listy\
      Závěrka\                    přiznání k dani z příjmů, výkazy, inventury + doručenky
      Dokumenty\                  smlouvy, korespondence s úřady
```

- Nejdřív **období**, pak agenda (odpovídá předávání podkladů po obdobích). Mzdy, Závěrka a Dokumenty jsou na úrovni roku.
- Názvy agend jako v Pohodě. Období čistá čísla kvůli řazení.
- Periodicita DPH (měsíc/čtvrtletí) se nikam neukládá: XML Pohody ji nevrací (`accountingunit.xsd` nic takového nemá, ověřeno 2026-10-18). Zjistí se z posledního podaného přiznání `DPHDP-<IČO>-<rok>-<období>_…` v `ELvykazy` nebo `ISDS` firmy (období `zari` = měsíčně, `3q` = čtvrtletně; kontrolní hlášení se nepočítá, právnické osoby ho podávají měsíčně vždy), u založených roků z názvů období. U neplátců (bez přiznání) se zadá ručně.

## Šanony

Šanony plní:

1. **Export aplikace** (opakovatelný, idempotentní; **spouští účetní ručně** u zákazníka, v UI nebo z CLI, rozhodnuto 2026-10-18):

   | Zdroj | Kde v Dokumentech Pohody | Období |
   |---|---|---|
   | originály dokladů | `Fakturace\…`, `Účetnictví\…` (vazba `DocDirs`) | DUZP / datum dokladu |
   | elektronická podání | `ELvykazy\<typ>\<TYP>-<IČO>-<rok>-<období>_<druh>_<datum>_Veta.xml` | z názvu souboru |
   | datová schránka | `ISDS\<schránka>\<id zprávy>\` (`dorucenka_<id>.zfo` + přílohy) | podle přílohy |

   Soubory dokladu se kopírují se jmény z Dokumentů (`<číslo>.pdf`, `<číslo>_<popis>.pdf`), jako `audit-export`.

2. **Ruční tisky z Pohody** do předem založených složek: výplatní pásky a rekapitulace (`Mzdy\<MM>`), mzdové listy, PDF přiznání DPH a KH (`<období>\DPH`). Tisk přes XML (`print.xsd`) to neumí: `ftr:printAgendaType` nemá mzdy ani přiznání (ověřeno v XSD).

Pravidla:

- Aplikace **nikdy nemaže ani nepřepisuje soubory, které sama nevytvořila** (ruční tisky).
- Kontrola úplnosti: upozornit na chybějící výplatní pásky / PDF přiznání, když je podání v `ELvykazy`.
- Zákazníci chtějí podání jako **PDF**.

Neověřeno: jak se jmenují přijaté zprávy ISDS s potvrzením o přijetí podání a zda obsahují odkaz na odeslané podání (párování doručenky k období).

## Zpracování inboxu

Stav souboru se **nevede podsložkami**, ale v evidenci aplikace (`%LOCALAPPDATA%\<instance>`). Důvody: podsložky neumí vyjádřit skupinu souborů jednoho dokladu ani soubor procházející víc kroky (ISDOC v PDF, LLM návrh čekající na kontrolu), zákazník je na sdíleném disku vidí a Google Drive při přesunech dělá konflikty.

- **Identita souboru = otisk obsahu (SHA-256)**, přejmenování ani přesun stav nerozbije; slouží i k odhalení duplicit proti už zpracovaným souborům.
- Kroky:
  1. **Příjem**: nové soubory v inboxu se zaevidují, určí se druh (ISDOC, PDF s vloženým ISDOC, PDF s textem, sken, obrázek), označí se duplicity.
  2. **Výběr zpracování**: u ISDOC automaticky, jinak účetní (později návrh LLM). Seskupení souborů k jednomu dokladu, jeden hlavní.
  3. **Zpracování** modulem: náhled → potvrzení → provedení (jako dnešní zatřídění).
  4. **Výsledek**: soubor z inboxu vždy odejde, nikdy se nesmaže.

| Výsledek | Kam soubor jde | Viditelné zákazníkovi |
|---|---|---|
| zpracováno | Dokumenty u dokladu; v evidenci číslo dokladu, kdo, kdy | ano, později v Šanonech |
| vráceno | `Ke zpracování\Vráceno\` + `<soubor>.txt` s důvodem | ano |
| ručně | interní archiv `Dokumenty\<firma>\NaPohodu\Přijato\<RRRR>\<MM>\` | ne |
| odloženo | zůstává v inboxu, evidence pamatuje rozpracování | ne |

Moduly zpracování (každý nový = nový modul, struktura se nemění):

| Modul | Stav |
|---|---|
| přiřazení k faktuře v Pohodě (dnešní zatřídění bez přejmenování) | první |
| ručně mimo aplikaci (banka, mzdy, ostatní, co účetní dělá ručně) | první |
| vrácení zákazníkovi | první |
| import ISDOC | později |
| rozpoznání PDF pomocí LLM a import | později |
| import pokladních dokladů | později |

### Přiřazení k faktuře

- K jednomu dokladu lze přiřadit **víc souborů najednou** (faktura, soupiska, certifikát, rozpis); jeden je hlavní.
- Lze přiřadit i k faktuře, **která už soubor má** (příloha dorazí později). Výběr nabízí nejdřív faktury bez dokumentů, ostatní jde dohledat.
- Pojmenování v Dokumentech podle dosavadní konvence:

  ```
  Fakturace\Přijaté faktury\<prefix>\<číslo>\
    <číslo>.pdf                 hlavní soubor
    <číslo>_<původní název>     přílohy
  ```

- Přechod: soubor, který se už jmenuje číslem dokladu (nebo `<číslo>_<popis>`), se spáruje automaticky jako dnes.

## Přístup zákazníků a přechod

Zákazníci nejsou členy sdíleného disku. Většinou mají nasdílené **čtení celé složky** `Podklady\<Firma>` a **zápis do `Ke zpracování`** (uživatel 2026-10-18). Z toho plyne:

- **Nový disk ne.** Sdílení drží složky (ID); přejmenování a přesun v rámci disku je zachová, nový disk by znamenal nasdílet vše znovu a rozbít zákazníkům odkazy a zástupce. Název disku zákazníci nevidí; jeho přejmenování mění jen `Podklady:RootPath`.
- **Převod na místě, po jednotlivých zákaznících:**

  | Složka | Akce |
  |---|---|
  | `Ke zpracování` | beze změny (ID i sdílení pro zápis zůstávají) |
  | `Šanony` (stávající) | přejmenovat na `Archiv`; aplikace do něj nikdy nepíše |
  | `Šanony` (nová) | založí aplikace; dědí čtení ze složky zákazníka |
  | chybějící `Ke zpracování` | založí aplikace; **zápis pro zákazníka musí nasdílet účetní ručně** (aplikace práva na Google Drive nevidí ani nemění) |

- Zákazník je převedený, když existuje `Archiv` nebo nová kostra Šanonů; dokud ne, aplikace do jeho Šanonů nepíše.
- Provádí `napohodu podklady init -c <ročník> [--period mesicne|ctvrtletne]` (náhled, pak `--apply`): Šanony s čímkoliv mimo strukturu → `Archiv` (jen když Archiv ještě není, jinak se cizí položky jen hlásí), kostra roku jednotky, chybějící `Ke zpracování` a `Vráceno`. Periodicita: zadaná volba > založený rok > poslední přiznání DPH > jiné založené roky; volba je nutná jen u neplátců a k přepsání.
- `Archiv` zůstává jako „starší podklady“; přesun do nové struktury je volitelný a vždy s náhledem.
- Sdělení zákazníkovi: „Nahrávejte dál do Ke zpracování. Šanony mají nové uspořádání, starší podklady najdete ve složce Archiv.“

Důsledky pro inbox:

- `Vráceno` uvnitř `Ke zpracování` dědí zápis, zákazník si z něj soubory může vzít nebo je smazat (žádoucí). Může do něj ale omylem i nahrávat: příjem `Vráceno` vždy přeskočí a soubory, které v něm nejsou evidované jako vrácené, jen nahlásí.
- Podsložky, které si zákazník v inboxu založí sám, se procházejí rekurzivně (kromě `Vráceno`).

### Mimo rozsah

- Originály, které nejsou přijatou fakturou (výpisy, pokladna, mzdové podklady, smlouvy), ukládá účetní do Dokumentů ručně; aplikace je jen přesune do interního archivu (výsledek „ručně“). Automatizujeme nejdřív přijaté faktury.
- Webový přístup zákazníků k Šanonům.
