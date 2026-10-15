# HANDOFF — stav k 2026-10-15

Předání kontextu z analýzy první generace (Electron) do nového repozitáře. Při pokračování na jiném stroji začni tímto souborem a `CLAUDE.md`.

## Kde jsme

- Repozitář `napohodu-electron` = první generace (Electron 42, React, TypeScript, PowerShell most do MDB). Funguje v provozu, zůstává bez větších zásahů do dokončení migrace.
- Tento repozitář = druhá generace v .NET. Zatím obsahuje jen dokumentaci (`docs/pohoda/`), `CLAUDE.md` a tento soubor. Žádný kód.
- Vývoj se přesouvá na Windows s nainstalovanou Pohodou (lze testovat reálně).

## Rozhodnutí (schválená uživatelem)

| Téma | Rozhodnutí |
|---|---|
| Stack | .NET (C#) pro vše: Core + CLI + Blazor web v jednom exe s podpříkazy. Ne WinForms, ne Electron, ne Node. |
| Architektura | `src/NaPohodu.Core`, `src/NaPohodu.App`, `tests/NaPohodu.Tests`. Co nejméně částí, jeden jazyk. |
| Build | `win-x86` kvůli Jet OLEDB (dáno Pohodou). |
| Edice Pohody | POHODA Komplet (MDB). Jet most je správná cesta k zápisu `DocDirs`. |
| Kód | Nový repozitář (tento), ne větev. Electron verze archivovat po migraci. |
| Hostovaná část / inbox | Zatím mimo rozsah, možná samostatná aplikace. |
| AI analýza faktur | Později. |
| Provoz | PC účetní trvale zapnuté. Plánované úlohy smí propadnout, vše musí jít spustit ručně z CLI. |
| mServer | Nepoužívat (instance na jednotku, blokuje zálohy). |

## Plán rozšíření (od uživatele)

1. Automatizace spouštěné z CLI (Task Scheduler).
2. Automatický sběr faktur z e-mailu / adresáře, analýza AI, export do Pohody včetně souboru v Dokumentech firmy.
3. Automatický import přejmenovaných souborů s fakturami.
4. Reporty pro zákazníky.
5. Webový klient; později možný inbox pro výměnu souborů mezi klienty a účetní.

## Co přenést z první generace (funkčně)

Mapování modulů `napohodu-electron/src` → nová generace:

| Původní | Co dělá | Cíl |
|---|---|---|
| `main/pohoda/xmlApi.ts` | spuštění `Pohoda.exe /XML`, INI, dekódování odpovědi | `Core/Pohoda/PohodaXmlRunner` |
| `main/pohoda/xmlHelpers.ts` | obálka DataPack, namespaces, kontrola `state`, `rdc:parts` | `Core/Pohoda/DataPack*`, `ResponsePack*` |
| `main/pohoda/PohodaClient.ts` | seznam jednotek, export přijatých faktur, parsování na modely | `Core/Pohoda/PohodaClient` + parsery |
| `shared/models/*.ts` | `Customer`, `Invoice` a podtypy mapované na XSD | `Core/Models` (zachovat mapování na XSD, zjednodušit na to, co UI používá) |
| `main/filing/fileFiling.ts` | párování souborů, cílové cesty, přesun s kolizemi | `Core/Filing` |
| `main/filing/fileExport.ts` | export souborů pro kontrolu podle období | `Core/Filing/AuditExport` |
| `main/mdb/adoBridge.ts` + `markDocs.ts` | čtení FA/DocDirs, plán, zápis v transakci se zálohou | `Core/Mdb` přes `System.Data.OleDb` |
| `renderer/views/*` | obrazovky: výběr zákazníka, dashboard, zatřídění, export pro kontrolu, dialog poznačení | Blazor stránky, stejné toky a české texty |
| `main/pohoda/config.ts` | JSON konfigurace v userData (`documentsPath`, `dataPath`, `exePath`, `username`, `password`) | `appsettings` + lokální soubor mimo repo; heslo nikdy do gitu |

## Chyby první generace, které v nové nesmí být

1. Odvození složky firmy ořezávalo běžná jména (regexy bez oddělovače před právní formou). Řešení: `companyDocumentsFolder` z exportu, heuristika jen jako fallback.
2. Prefix adresáře = první 4 znaky čísla. Řešení: `number/ids` nebo `subFolder` z exportu.
3. Celá XML odpověď logovaná na INFO bez rotace (osobní údaje, MB na volání).
4. Žádný timeout při spouštění Pohody a PowerShellu.
5. Dočasné adresáře podle `Date.now()` (souběh). V .NET `Path.GetTempFileName`/`Directory.CreateTempSubdirectory`.
6. Zálohy MDB hromaděné v datové složce Pohody bez úklidu.
7. Přihlašovací údaje commitnuté v `deploy/napohodu-config.json`.
8. Export pro kontrolu kopíroval ploše, kolize názvů možná u souborů přidaných ručně v Pohodě.
9. Faktury exportované z Pohody při každém otevření obrazovky (pomalé). Cache per zákazník s ručním obnovením.
10. Žádné testy. Čisté funkce (párování, cesty, součty, parsery) pokrýt xUnit s fixture z reálných odpovědí.

## Otevřené otázky (ověřit na Windows nebo s uživatelem)

- ~~Vrací export s `attachments=true` `companyDocumentsFolder` u každé faktury, nebo jen u faktur s dokumenty?~~ **Ověřeno 2026-10-15: jen u faktur s dokumenty** (SafetyLine 2025: 36 z 484). Firma bez jediného zatříděného souboru složku z exportu nedostane → **otevřené: odkud ji vzít** (heuristika z názvu + ověření existence, ruční nastavení per zákazník, nebo jiný ročník téže firmy).
- Proč mají přijaté faktury prefixovou úroveň a vydané plochou? **Částečně:** u přijatých jsou v jedné jednotce obě podoby (`Fakturace\Přijaté faktury\2511\251100002` i plochá `Fakturace\Přijaté faktury\251100001`), takže tvar určuje to, kdo/jak soubor zatřídil, ne pevné pravidlo. Prostřední úroveň odpovídá `number/ids` (`2511`). Zbývá: jaký tvar má NaPohodu vytvářet pro nové zatřídění (návrh: `<ids>\<číslo>`, jak to dělá většina).
- Mají se zatřiďovat i přijaté dobropisy a zálohové faktury (jiný `RelTpFak`, jiný `invoiceType`)?
- Má zatřídění hledat faktury ve všech ročnících firmy, nebo jen ve vybraném?
- Má účetní při zatřiďování Pohodu zavřenou? Automatické poznačení po přesunu to vyžaduje.
- Co má obsahovat „Export pro zákazníka“ (v 1. generaci jen placeholder)?
- Kolik uživatelů a stanic, jak se distribuuje exe.

## Navržený první krok

1. ~~Založit řešení (`dotnet new sln`, Core/App/Tests), `Directory.Build.props` s `RuntimeIdentifier=win-x86`, `.editorconfig`, nullable + warnings as errors.~~ Hotovo 2026-10-15: .NET 10, `NaPohodu.slnx`, xunit v3 na Microsoft.Testing.Platform, `PohodaOptions` + vrstvená konfigurace, `napohodu --help/--version`.
2. ~~`Core/Pohoda`: runner (`Pohoda.exe /XML` s timeoutem, INI, dekódování windows-1250), DataPack obálka, kontrola `state`, parser `listAccountingUnit` a `listInvoice` (přijaté, s `attachments=true`).~~ Hotovo 2026-10-15: `PohodaXmlRunner`, `DataPack`, `PohodaRequests` (validované proti XSD v testech), `ResponsePack`, `AccountingUnitParser`, `InvoiceParser`, `PohodaClient`. Testy zatím nad **syntetickými** fixtures (`tests/…/Fixtures/Synthetic`); proti reálné Pohodě neověřeno. Ověřit v kroku 3:
   - ~~zda Pohoda čte INI ve windows-1250~~ **Ověřeno 2026-10-15:** ano, funguje i s diakritikou v temp cestě (`C:\Users\Marek Beneš\…`).
   - zda `Pohoda.exe` vrací chybové kódy 1xx/5xx jako návratový kód procesu, nebo jen v odpovědi. **Částečně:** odmítnutý balíček vrátí kód 0 a `responsePack state="error"` s `note`.
   - ~~zda export faktur projde bez `ico`~~ **Ověřeno 2026-10-15: neprojde** („Tento balíček není určen pro tuto jednotku.“, odpověď nese `ico` a `key` otevřené jednotky). Požadavky nad jednotkou proto vyžadují `UnitIdentity` (IČ, jinak `key`); CLI bez `--ico` dohledá jednotku přes `listAccountingUnit`.
   - `acu:dataFile` vrací název **včetně přípony** (`08215839_2026.mdb`) a ten se předává jako `database` v INI. `acu:id` Pohoda neposílá. Seznam 83 jednotek trval 4,7 s, odpověď 93 kB.
   - **Ověřeno 2026-10-15 (SafetyLine 2025, 484 faktur, 2 MB):** `attachments=true` funguje. `companyDocumentsFolder` je **absolutní cesta** včetně `documentsPath` a koncového `\` (`C:\ProgramData\STORMWARE\POHODA\Dokumenty\SafetyLine\`), ne jen název složky. `subFolder` je relativní k ní. Soubory v Dokumentech se nemusí jmenovat podle čísla (`k251100002.pdf`, `Sken ze dne … (přetaženo).pdf`), párování tedy nesmí předpokládat název = číslo u existujících záznamů.
   - `number/ids` (prefix řady, např. `2511`) Pohoda vrací u faktur z řady daného roku; u faktur z řady předchozího roku, které jsou v ročníku také (2024 v souboru 2025, 2025 v souboru 2026), `ids` **chybí**. Prefix pro cestu proto nelze brát jen z `ids`.
   - Ročník obsahuje i faktury s datem z předchozího roku (24 faktur z 12/2024 v souboru 2025).
3. ~~`App`: CLI `napohodu customers` a `napohodu invoices --customer <dataFile> [--from --to]` s tabulkovým výstupem.~~ Hotovo 2026-10-15 (+ `napohodu config`, `--save-response`). **Ověřeno proti reálné Pohodě** (seznam jednotek, faktury SafetyLine 2025/2026). Testy běží nad anonymizovanými reálnými odpověďmi (`tests/…/Fixtures/Real`, nástroj `tools/anonymize-fixture.cs`); syntetické fixtures odstraněny.
4. Teprve pak `Core/Filing`, `Core/Mdb` a Blazor `serve`.
   - **`Core/Filing` hotovo 2026-10-15** (+ CLI `napohodu filing`, výchozí náhled). Rozhodnutí uživatele: nová podsložka **s prefixem řady** (`Fakturace\Přijaté faktury\<ids>\<číslo>`), existující `subFolder` z exportu se vždy převezme (i plochý); prefix bez `ids` z jiné faktury téže řady nebo z jediné složky řady na disku, jinak „nelze určit“ (nikdy první 4 znaky). Složka firmy: `companyDocumentsFolder` z exportu → existující složka podle názvu (celá slova, právní forma jen jako celý zbytek; ověřeno na 20 reálných firmách: 15 správně, 5 bez složky bez falešného odhadu) → ručně `--company-folder`. Párování **jen proti vybranému ročníku**. Kolize: přeskočit (výchozí) nebo „ponechat oba“ (`soubor (2).pdf`), nikdy přepsat; totožný soubor v cíli = „už zatříděno“.
   - **`Core/Mdb` hotovo 2026-10-15**: čtení `FA`/`DocDirs` (sdíleně), plán (vložit / doplnit / už poznačeno / konflikt / faktura není v ročníku), zápis se zálohou mimo datovou složku, exkluzivně, v transakci s opětovnou kontrolou. CLI `napohodu docdirs` (rekonciliace) a `filing --apply --mark-docs`. Ověřeno na **kopii** reálné `08215839_2025.mdb` (35 vazeb sedí, 1 duplicitní složka na disku, INSERT přes AutoNumber, UPDATE prázdného Dir, konflikt nedotčen). **Zápis do ostrého datového souboru zatím neproběhl** – po prvním ostrém zápisu ověřit záložku Dokumenty v Pohodě.
   - V datové složce Pohody zůstala záloha z 1. generace `08215839_2025.backup-20260727163103..mdb` (chyba č. 6) – smazat ručně, až nebude potřeba.
   - **Export pro kontrolu hotovo 2026-10-15** (`Core/Filing/AuditExport`, CLI `napohodu audit-export`): období podle DUZP (jako 1. generace), zdroj = `subFolder` z Pohody, kopie do ploché složky; názvy bez čísla faktury dostanou prefix čísla (oprava chyby č. 8), cíl se nepřepisuje, opakování nekopíruje znovu; hlásí faktury bez dokumentů a soubory, které Pohoda eviduje, ale na disku chybí.
   - **Blazor `serve` hotovo 2026-10-15**: stránky Výběr zákazníka, rozcestník, Přijaté faktury, Zatřídění (plán, kolize po souborech, přesun + automatické poznačení DocDirs jako v 1. generaci), Poznačení v Pohodě, Export pro kontrolu (výchozí období = minulý měsíc). Ověřeno v prohlížeči proti reálné Pohodě jen v režimu čtení (SafetyLine 2025: faktury, plán zatřídění, porovnání DocDirs, náhled exportu); přesun ani zápis z UI zatím neproběhl. Ověřeno i z publikované verze (statické soubory z `wwwroot`).
   - „Export pro zákazníka“ je v UI jako „Připravujeme“ (otevřená otázka, co má obsahovat).

## Další možné kroky

- Plán rozšíření 1: plánované úlohy (Task Scheduler) nad CLI – co přesně automatizovat.
- Otevřené otázky výše (dobropisy a zálohové faktury, distribuce exe, více stanic).
- První ostrý zápis DocDirs ověřit v Pohodě; smazat duplicitní složku `SafetyLine\…\2511\251100001` (totožná kopie, Pohoda odkazuje na plochou) a zálohu 1. generace v datové složce.
