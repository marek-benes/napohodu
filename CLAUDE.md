# CLAUDE.md

Pokyny pro Claude Code při práci v tomto repozitáři.

---

## Co je NaPohodu

**NaPohodu** je doplňková aplikace k českému účetnímu systému [Stormware POHODA](https://www.stormware.cz/pohoda/) pro účetní, která spravuje **více účetních jednotek**. Automatizuje věci, které Pohoda sama neumí pohodlně: zatřídění souborů faktur do adresářové struktury Dokumentů, poznačení těchto adresářů v databázi Pohody, exporty souborů pro kontrolu a (postupně) sběr faktur z e-mailu, import do Pohody a reporty.

Toto je **druhá generace** aplikace. První generace (Electron + TypeScript) je v repozitáři `napohodu-electron` a zůstává v provozu, dokud ji tato verze nenahradí. Stav migrace a rozhodnutí: `HANDOFF.md`.

### Pojmy

- **Zákazník** (`zákazník`) = **účetní jednotka** Pohody (`účetní jednotka`) v konkrétním roce: jedna firma, jeden datový soubor MDB. Seznam se čte přes `listAccountingUnitRequest`; identifikátor je název datového souboru (`dataFile`). Není to `odběratel`/obchodní partner na dokladu.
- **Dokumenty** = složka `<documentsPath>\<složka firmy>\…`, společná pro všechny roky jedné firmy. Pohoda do ní ukládá soubory k dokladům a v MDB drží vazbu doklad → adresář (tabulka `DocDirs`).
- **Podklady** = sdílený disk `<Podklady:RootPath>\<složka firmy>\` (název stejný jako složka firmy v Dokumentech). **Ke zpracování** = inbox, kam zákazník nahrává cokoliv; stav zpracování vede aplikace ve své evidenci, ne podsložkami. **Šanony** = exportovaný obraz účetnictví pro zákazníky (`<RRRR>\<období>\<agenda>`), složky zakládá aplikace a nikdy nemaže soubory, které nevytvořila. Pravidla: **`docs/podklady.md`**.

### Prostředí

| | |
|---|---|
| Produkce | Windows 64bit, vedle 32bit Pohody, edice **POHODA Komplet** (základní řada, datové soubory Access/MDB, ne SQL/E1) |
| Vývoj | Windows (lze i macOS pro část bez Pohody, ale nic reálného tam nejde ověřit) |
| Běh | PC účetní je trvale zapnuté. Naplánované úlohy mohou propadnout, každý příkaz musí jít kdykoliv spustit ručně. |

---

## Technologický stack (cílový)

- **.NET 10 (LTS), C#**, jeden jazyk pro vše. Žádný Node, žádný PowerShell most.
- **Build `win-x86`**: Jet 4.0 OLEDB provider (přístup k MDB) existuje jen 32bit. Je to dané Pohodou.
- **Jeden spustitelný soubor s podpříkazy** (`napohodu serve`, `napohodu filing …`, `napohodu collect …`). CLI slouží Task Scheduleru, `serve` spouští webové UI.
- **Web UI: ASP.NET Core + Blazor** (Razor komponenty, C#). Obrazovky nikdy ve WinForms. Vlastní CSS bez CDN (offline).
- **WinForms jen v `src/NaPohodu.Desktop`** a jen pro integraci se systémem: ikona v oznamovací oblasti (stav, „Otevřít“, „Ukončit“) a systémový dialog výběru složky (`IFolderPicker`). Nikdy pro obrazovky; App ani Core na WinForms nezávisí.
- **Struktura řešení**

```
NaPohodu.slnx
  src/NaPohodu.Core     doménové modely, Pohoda XML (export/import), MDB (DocDirs), zatřídění souborů,
                        později e-mail (MailKit), AI analýza, reporty
  src/NaPohodu.App      napohodu.exe: CLI (System.CommandLine) + webové UI (ASP.NET Core/Blazor, `serve`)
  src/NaPohodu.Desktop  NaPohodu.Desktop.exe: okenní spouštěč pro účetní (server + ikona u hodin + okno)
  tests/NaPohodu.Tests  xUnit; fixtures = reálné XML odpovědi Pohody a kopie MDB
```

- **Hostovaná část** (inbox mezi klienty a účetní) se zatím neřeší; možná bude samostatná aplikace.
- **AI analýza faktur** přijde později; neřešit předem.

### Zásady

- UI texty **česky**. Kód, názvy a commit messages podle běžných .NET konvencí.
- Komponenty UI bez business logiky; logika v Core, testovatelná bez Pohody.
- Vše, co sahá do MDB nebo spouští Pohodu, musí být bezpečné při opakovaném spuštění a musí jít spustit ručně z CLI.
- Když je chování Pohody nejasné (XML odchylky, reálné tvary dat, rozložení souborů), **zeptat se**, neodhadovat. Co jde, ověřit na Windows proti testovací jednotce.

---

## Pohoda XML API

Oficiální XSD (version_2) jsou v **`docs/pohoda/schemas/`** a jsou zdrojem pravdy pro XML, které posíláme i parsujeme. Jsou v kódování **windows-1250** s CRLF; `grep` je bere jako binární, použij `grep -a`. Dokumentace: https://www.stormware.cz/pohoda/xml/ (zpracování, podrobnosti, podporované doklady, mServer).

### Volání

- `Pohoda.exe /XML <user> <password> <cesta.ini>`; INI má sekci `[XML]` s klíči `input_xml`, `response_xml`, `database` (název datového souboru; vynechat jen pro seznam účetních jednotek), `format_output`, volitelně `check_duplicity`, `XSLT_input/output`. Heslo je v argumentech procesu: nikdy ho nelogovat (chybová hláška `Process` ho obsahuje).
- Každé volání = start Pohody, přihlášení, otevření jednotky. Trvá sekundy. **Vždy s timeoutem** a možností proces ukončit (Pohoda může zobrazit modální dialog a viset).
- Odpověď je soubor, zpravidla **windows-1250**; dekódovat podle XML deklarace, fallback windows-1250.
- Chyby přípravy 551–557 (konfigurace, konverze jednotky, poškozený soubor, přihlášení, práva, aktivace), chyby zpracování 101–113, varování 601–609. Odpověď `rsp:responsePack` i `rsp:responsePackItem` nese atribut `state` (`ok`/`error`) a `note`; chybu nikdy nepolykat jako prázdný seznam.
- **mServer** (HTTP server vestavěný v Pohodě) pro tento případ nepoužíváme: drží jednu instanci Pohody na jednotku, blokuje zálohy, Stormware ho nedoporučuje nechávat běžet.

### Ověřené konvence

- **Obálka** (`data-package.xsd`): `dat:dataPack` vyžaduje `version`, `id`, `application`, `note`; `ico` je nepovinné (vynechat u seznamu jednotek). `dat:dataPackItem` vyžaduje `version` + `id`.
- **Jmenné prostory**: lokálně deklarovaný element patří do namespace *svého* schématu, ne schématu svého typu (`lst:requestInvoice` → child `ftr:filter`).
- **Export faktur**: `lst:listInvoiceRequest version="2.0" invoiceType="receivedInvoice" invoiceVersion="2.0"`. Filtr (`filter.xsd`, `groupFilter_1`) umí `dateFrom`/`dateTill`, `selectedNumbers`, `selectedIco`, `lastChanges`, `id`. `restrictionData/attachments=true` přidá záložku Dokumenty: `inv:attachments/typ:files/typ:companyDocumentsFolder` (**absolutní** cesta ke složce firmy s koncovým `\`, jen export), `typ:subFolder` (adresář dokladu relativně k ní, např. `Fakturace\Přijaté faktury\2511\251100002`, u některých i plochý `…\Přijaté faktury\251100001`) a `typ:file/typ:name`. **Pohoda je posílá jen u faktur, které nějaký dokument mají** (ověřeno 2026-10-15). **Použít místo heuristik** (viz níže).
- **Identita jednotky**: požadavek nad jednotkou musí mít v `dat:dataPack` `ico` (nebo `key`), jinak Pohoda vrátí `state="error"` „Tento balíček není určen pro tuto jednotku.“ (s návratovým kódem 0). `acu:dataFile` obsahuje příponu `.mdb`.
- **Číslo dokladu**: `inv:number/typ:numberRequested`; `typ:ids` = prefix číselné řady.
- **Peníze** (`typ:typeCurrencyHome`): tuzemský součet = `priceNone + priceLowSum + priceHighSum + price3Sum + round/priceRound`. Cizí měna `foreignCurrency/priceSum`, kód v `currency/ids`.
- **Rozdělený export**: seznamy rozšiřují `rdc:listVersionType` a při zadaném `limit` mohou vrátit `rdc:parts` místo dat. Pokud `limit` pošleme, musíme to ošetřit; jinak kontrolovat, že `parts` chybí.
- **Update dokladů XML neumí.** `invoice.xsd` nemá `actionType`; update/delete podporují jen adresář, zásoby, objednávky a číselníky. Proto se poznačení Dokumentů u existujících faktur dělá přímo v MDB. Při **importu nové faktury** lze `inv:attachments/typ:files/typ:subFolder` zadat (viz `docs/pohoda/import_faktury_vcetne_zalozky_dokumenty.xml`).

---

## Pohoda MDB (přímý přístup)

Podrobnosti a historie zjištění: **`docs/pohoda/mdb/NOTES.md`** (čti před jakoukoliv prací s MDB). Skripty `mdb-discover.ps1`, `mdb-diff.ps1`, `mdb-mark-docs.ps1` jsou původní PowerShell nástroje; v nové generaci je nahradí C# (`System.Data.OleDb`, provider `Microsoft.Jet.OLEDB.4.0`, proces x86).

Ověřená fakta:

- Datový soubor: `<dataPath>\<dataFile>.mdb`, **jeden na jednotku a rok**. Dokumenty jsou společné pro všechny roky, proto rekonciliaci dělat zvlášť nad každým ročníkem.
- Vazba doklad → adresář: tabulka **`DocDirs`** (`ID`, `RelAgID`, `RelSubID` = NULL, `RelID` = `FA.ID`, `Dir` = cesta relativně od složky firmy, backslashe, bez koncového `\`, může být NULL/prázdné).
- `RelAgID`: vydané faktury **2**, přijaté faktury **3**. Obě sdílejí tabulku `FA`; `RelTpFak` vydané **1**, přijaté **11** (dobropisy a zálohové faktury mají jiné hodnoty, zatím neřešeno).
- Pozorované tvary `Dir`: přijaté `Fakturace\Přijaté faktury\2611\26110009` (s úrovní prefixu), vydané `Fakturace\Vydané faktury\10090262` (ploché). Proč se liší, není ověřeno.
- **Zápis vždy**: jednotka zavřená v Pohodě (žádný `.ldb` vedle `.mdb`, pozor i na jiné stanice v síti) → záloha MDB (mimo datovou složku Pohody, s úklidem starých záloh) → exkluzivní otevření → jedna transakce → INSERT bez ID (`DocDirs.ID` je AutoNumber, ověřeno), UPDATE jen řádků s prázdným `Dir`; **nikdy nepřepisovat** existující odlišný `Dir`, jen reportovat. Implementace: `src/NaPohodu.Core/Mdb` (`DocDirsSnapshot`, `DocDirsPlanner`, `DocDirsWriter`).
- OleDb potřebuje vestavěné COM: **nepublikovat s AOT ani trimmingem** (vypnuto v `Directory.Build.props`).
- Historické ročníky: řádky v `DocDirs` neškodí, v UI Pohody jsou neaktivní.

---

## Zatřídění souborů (doménová pravidla)

- Soubor se páruje s fakturou podle názvu bez přípony: přesná shoda s číslem, nebo příloha `<číslo>_<popis>`. Číslo samo může obsahovat podtržítko, zkoušet každý prefix před podtržítkem.
- Cíl: `<documentsPath>\<složka firmy>\Fakturace\Přijaté faktury\<prefix>\<číslo>\<soubor>`. Prefix byl v 1. generaci „první 4 znaky čísla“; správně je to **prefix číselné řady** z exportu (`number/ids`) nebo rovnou `subFolder` z `attachments`.
- Složka firmy byla v 1. generaci odvozována z názvu jednotky odseknutím právní formy. Heuristika **ořezávala i běžná jména** („Adidas“ → „Adid“). V nové generaci číst `companyDocumentsFolder` z exportu (je jen u faktur s dokumenty, takže firma bez zatříděného souboru ho nemá); heuristiku použít jen jako fallback s povinným oddělovačem před právní formou a vždy ověřit existenci složky.
- Skryté a systémové soubory (`desktop.ini`, `Thumbs.db`, `.DS_Store`, názvy s tečkou na začátku) ignorovat.
- Přesun přes hranice svazků: `rename` selže s EXDEV, fallback copy + delete.
- Kolize v cíli: rozhodnutí per soubor, nikdy tiše nepřepisovat.

---

## Dokumentace a soubory

```
docs/pohoda/schemas/      oficiální XSD version_2 (windows-1250)
docs/pohoda/mdb/NOTES.md  zjištění o MDB (DocDirs, RelAgID, RelTpFak, postup zápisu)
docs/pohoda/mdb/*.ps1     původní PowerShell nástroje (discovery, diff, rekonciliace)
docs/pohoda/import_faktury_vcetne_zalozky_dokumenty.xml  ukázka importu faktury se záložkou Dokumenty
docs/podklady.md          Podklady: inbox Ke zpracování, Šanony, moduly zpracování (rozhodnuto 2026-10-18)
HANDOFF.md                stav migrace, rozhodnutí, plán, otevřené otázky
```

---

## Vývoj

Předpoklady: .NET 10 SDK (x64) a **x86** runtime .NET + ASP.NET Core 10 (`C:\Program Files (x86)\dotnet`). Aplikace i testy běží jako x86 proces kvůli Jet OLEDB.

```
dotnet build NaPohodu.slnx
dotnet test --solution NaPohodu.slnx
dotnet run --project src/NaPohodu.App -- --help
dotnet publish src/NaPohodu.App -c Release -o publish
```

- `global.json` drží SDK 10.0.x a zapíná pro `dotnet test` Microsoft.Testing.Platform (xunit v3 jinak na .NET 10 SDK neběží).
- `Directory.Build.props`: `net10.0-windows`, `win-x86`, nullable, warnings as errors, analyzéry `latest-recommended`. Verze balíčků centrálně v `Directory.Packages.props`.
- `nuget.config` v repozitáři nastavuje nuget.org (nespoléhat na globální konfiguraci stroje).
- CLI (`src/NaPohodu.App/Cli`): `napohodu customers [--year] [--search]`, `napohodu invoices -c <dataFile> [--from --to RRRR-MM-DD] [--ico]`, `napohodu config`, `napohodu filing -c <dataFile> --source <adresář> [--company-folder] [--apply] [--on-conflict Skip|KeepBoth]` (výchozí náhled; přesun až s `--apply`, nikdy nepřepisuje; `--mark-docs` po přesunu poznačí DocDirs), `napohodu docdirs -c <dataFile> [--company-folder] [--apply]` (rekonciliace DocDirs se složkami na disku, výchozí náhled), `napohodu audit-export -c <dataFile> --dest <složka> [--from --to] [--dry-run]` (kopie souborů faktur za období podle DUZP, ze `subFolder` evidovaného v Pohodě; soubory nepojmenované číslem dostanou prefix čísla; bezpečné při opakování). Společné `--verbose` (logy na stderr) a `--save-response <soubor>` (surová odpověď Pohody → podklad pro fixtures, před commitem anonymizovat). Tabulky jdou na stdout, hlášky a logy na stderr. Návratové kódy: 0 OK, 1 chyba Pohody / chybné volby, 2 konfigurace, 64 nesmyslné volby, 130 přerušeno.
- **Provoz**: jeden počítač, jeden účet Windows, jedna kopie. Produkce a vývoj se nemíchají:
  - produkce: `tools/install.ps1` (testy → publikace Desktop + App do jedné složky → `%LOCALAPPDATA%\Programs\NaPohodu`, zástupce „NaPohodu“ na ploše = `NaPohodu.Desktop.exe`), data/zálohy `%LOCALAPPDATA%\NaPohodu`, port 5080; produkční `appsettings.Local.json` se při aktualizaci nepřepisuje (při první instalaci se převezme z `src/NaPohodu.App`), `VERZE.txt` = commit;
  - vývoj: `dotnet run` přes `Properties/launchSettings.json` → instance `NaPohodu-vyvoj`, port 5081, data `%LOCALAPPDATA%\NaPohodu-vyvoj`, v UI štítek „VÝVOJ“;
  - spouštění pro účetní: `NaPohodu.Desktop.exe` (WinExe, bez konzole) spustí webový server (`Web/AppHost`), ikonu u hodin a okno aplikace (Edge `--app`, `Web/AppWindow`). Zavření okna → po `Instance:IdleShutdown` (výchozí 15 min, 0 = nikdy) bez okna a bez rozběhnuté operace se aplikace sama vypne (`Web/IdleShutdown`, `Web/AppActivity` počítá okna = Blazor obvody a operace = `UnitPageBase.RunAsync` + volání Pohody). „Ukončit“ z ikony počká na operace (max 2 min). Data z Pohody starší než `Instance:CacheMaxAge` (4 h) se načtou znovu.
  - `napohodu serve` (konzole) je pro vývoj a ladění, bez dialogu výběru složky (tlačítko „Vybrat…“ jen s `IFolderPicker`).
  - zámek jedné kopie (`Local\<instance>-serve`) sdílí Desktop i serve; druhé spuštění jen otevře okno. `napohodu config` ukazuje, která instance běží.
  - publikace nesmí obsahovat `*.staticwebassets.runtime.json` (odkazuje do repozitáře); instalace ho odstraní a hlídá.
  - `appsettings.Local.json` se nikdy nepublikuje (`CopyToPublishDirectory=Never`).
  - aktualizace na tomto PC: `C:\Tools\NAPOHODU-UPDATE.bat` (mimo repozitář): `git pull --ff-only` → dotaz při necommitnutých změnách → `tools/install.ps1`.
- Webové UI: `napohodu serve [--port] [--no-browser]` – Blazor Server jen na `127.0.0.1`, bez přihlašování. Zákazník je v adrese (`/z/<datový soubor>/faktury|zatrideni|dokumenty|export-kontrola`), takže přežije obnovení stránky. `Web/PohodaSession` drží cache jednotek a faktur (ruční „Obnovit z Pohody“) a řadí volání Pohody za sebe. Stránky dědí `Components/Common/UnitPageBase` (načtení jednotky, spinner, chyby jako text) a logiku volají z Core. Vlastní `wwwroot/app.css` bez CDN (offline). Razor: string parametry komponent předávat s `@` (`Busy="@Busy"`), jinak se předá doslovný text.
- Fixtures pro testy = anonymizované reálné odpovědi Pohody (`tests/NaPohodu.Tests/Fixtures/Real`). Nové vytvářet jen přes `tools/anonymize-fixture.cs` (postup v `Fixtures/README.md`); surové odpovědi z `--save-response` patří do `tmp/` (mimo git) a nikdy do commitu.
- Pro `dotnet run` patří lokální konfigurace do `src/NaPohodu.App/appsettings.Local.json` (Web SDK ji zkopíruje vedle exe; je v `.gitignore`).
- Konfigurace aplikace: `appsettings.json` (v gitu, bez tajných údajů) → `appsettings.Local.json` vedle exe (mimo git, vzor `appsettings.Local.example.json`) → proměnné prostředí `NAPOHODU_*` (např. `NAPOHODU_Pohoda__Password`). Sekce `Pohoda` = `PohodaOptions`.
