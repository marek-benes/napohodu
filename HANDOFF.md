# HANDOFF — stav k 2026-10-18

Předání kontextu mezi sessions. Při pokračování začni tímto souborem (hlavně **Plán další session** na konci) a `CLAUDE.md`.

## Kde jsme

- Repozitář `napohodu-electron` = první generace (Electron). Zatím nearchivovat, dokud 2. generace neprojde ostrým provozem (první ostrý zápis DocDirs už proběhl úspěšně). Pak na GitHubu archivovat (ne mazat), odebrat případného zástupce staré verze z plochy; pozor, v historii jsou commitnuté přihlašovací údaje (chyba č. 7) – repozitář nechat soukromý.
- Tento repozitář = druhá generace v .NET 10, **funkčně nahrazuje 1. generaci** (bez „Export pro zákazníka“, který byl i tam jen placeholder): seznam jednotek a faktur z Pohody, zatřídění souborů, poznačení DocDirs, export pro kontrolu; CLI + webové UI + okenní spouštěč s ikonou u hodin. 163 testů nad anonymizovanými reálnými daty.
- **Produkce nainstalovaná** na tomto PC (`%LOCALAPPDATA%\Programs\NaPohodu`, zástupce „NaPohodu“ na ploše, aktualizace `C:\Tools\NAPOHODU-UPDATE.bat`). Vývoj běží odděleně (instance `NaPohodu-vyvoj`, port 5081).
- **Pozor:** vývoj i produkce sahají na **stejnou Pohodu, stejná data a Dokumenty**. Zápisové operace (přesun, DocDirs, budoucí import) z vývoje míří do ostrých dat.

## Rozhodnutí (schválená uživatelem)

| Téma | Rozhodnutí |
|---|---|
| Stack | .NET (C#) pro vše: Core + CLI + Blazor web. Obrazovky ne ve WinForms; WinForms jen izolovaně v `NaPohodu.Desktop` pro ikonu u hodin a dialog výběru složky. Ne Electron, ne Node. |
| Architektura | `src/NaPohodu.Core` (logika), `src/NaPohodu.App` (`napohodu.exe`: CLI + Blazor), `src/NaPohodu.Desktop` (`NaPohodu.Desktop.exe`: okenní spouštěč), `tests/NaPohodu.Tests`. |
| Build | `win-x86` kvůli Jet OLEDB (dáno Pohodou). |
| Edice Pohody | POHODA Komplet (MDB). Jet most je správná cesta k zápisu `DocDirs`. |
| Kód | Nový repozitář (tento), ne větev. Electron verze archivovat po migraci. |
| Hostovaná část / inbox | Zatím mimo rozsah, možná samostatná aplikace. |
| AI analýza faktur | Později. |
| Provoz | PC účetní trvale zapnuté. Plánované úlohy smí propadnout, vše musí jít spustit ručně z CLI. |
| Instalace | Jeden počítač (tento), jeden účet Windows, jedna kopie. Spouštění zástupcem na ploše. Produkce oddělená od vývoje (`tools/install.ps1`, viz CLAUDE.md „Provoz“). Rozhodnuto 2026-10-18. |
| Ovládání pro účetní | Spuštění i ukončení jako běžná aplikace: zástupce → okno (Edge `--app`), křížek → po 15 min nečinnosti se server vypne; ikona u hodin pro kontrolu a „Ukončit“. Výběr složek systémovým dialogem. Ikona u hodin ověřena uživatelem 2026-10-18. Zavírání okna skriptem při „Ukončit“ v Edge `--app` nefunguje (zkoušeno a vráceno); po ukončení serveru okno ukáže českou hlášku o ztrátě spojení s odkazem na zástupce. Bez přepisu UI (zůstává Blazor, ověřitelný v prohlížeči). Zvažováno a zamítnuto: přímé MDB místo XML (nedokumentované schéma, importy jen přes XML), WPF (přepis UI), Blazor Hybrid (nový hostitel, nejde ověřovat v prohlížeči), Windows služba (Pohoda.exe v session 0). Rozhodnuto 2026-10-18. |
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

- ~~Vrací export `companyDocumentsFolder` u každé faktury?~~ **Jen u faktur s dokumenty.** Vyřešeno: export → složka podle názvu firmy (ověřeno na 20 firmách) → ruční zadání / „Vybrat…“.
- ~~Jaký tvar podsložky vytvářet?~~ **Rozhodnuto: s prefixem řady** `<ids>\<číslo>`; existující tvar z Pohody se převezme.
- ~~Hledat faktury ve všech ročnících?~~ **Rozhodnuto: jen vybraný ročník.**
- ~~Má být Pohoda při zatřiďování zavřená?~~ Přesun funguje vždy; poznačení DocDirs jen při zavřené jednotce, jinak UI/CLI poradí dokončit později.
- ~~Kolik stanic, distribuce?~~ **Jeden PC, jeden účet**, instalace skriptem.
- Mají se zatřiďovat i přijaté dobropisy a zálohové faktury (jiný `RelTpFak`, jiný `invoiceType`)? **Otevřené.**
- Co má obsahovat „Export pro zákazníka“? **Otevřené.**
- Vrací `Pohoda.exe` chybové kódy 1xx/5xx jako návratový kód procesu (špatné heslo, poškozená jednotka)? **Neověřeno** (ověřeno jen odmítnutí balíčku s kódem 0).

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

5. **Provoz a ovládání hotovo 2026-10-18**: produkční instalace (`tools/install.ps1`, `C:\Tools\NAPOHODU-UPDATE.bat`), oddělení vývoje, okenní spouštěč `NaPohodu.Desktop` (okno Edge `--app`, ikona u hodin – ověřeno uživatelem, vypnutí po 15 min nečinnosti, obnova dat starších než 4 h), výběr složek dialogem, ikona aplikace z 1. generace, výchozí filtr roku, fokus na vyhledávání, česká hláška o ztrátě spojení.

## Plán další session

### A. Ověřit v ostrém provozu (bez nového kódu, s uživatelem)

1. ~~**První ostrý zápis DocDirs**~~ **Proběhl úspěšně** (uživatel 2026-10-18), zapsáno i v `docs/pohoda/mdb/NOTES.md`.
2. **Dialog „Vybrat…“**: otevírá se nad oknem aplikace a doplní cestu?
3. **Vypnutí po nečinnosti**: zavřít okno → do 15 min zmizí ikona u hodin.
4. **Export pro kontrolu** ostře (kopírování do složky, zatím jen náhled).
5. Chování při otevřené jednotce v Pohodě (poznačení odmítne, poradí dokončit) a při jiné firmě než SafetyLine (hlavně firmy bez složky v Dokumentech).
6. Úklid (ručně uživatel): duplicitní složka `SafetyLine\Fakturace\Přijaté faktury\2511\251100001` (totožná kopie, Pohoda odkazuje na plochou) a záloha 1. generace `Data\08215839_2025.backup-20260727163103..mdb`.

### B. Technický dluh (malé, doporučeno před rozšířeními)

1. **Testovací účetní jednotka v Pohodě pro vývoj** (založí uživatel) – aby vývoj nezapisoval do ostrých dat; vývojová instance pak může mít povolený zápis jen do ní. Podmínka pro plán rozšíření 2/3 (import faktur).
2. **Log do souboru** pro `NaPohodu.Desktop` (bez konzole teď chyby nejsou nikde vidět): `%LOCALAPPDATA%\<instance>\Logy`, rotace, bez obsahu odpovědí Pohody a bez osobních údajů (chyba č. 3 z 1. generace).
3. Sjednotit x86 runtime: `Microsoft.WindowsDesktop.App` x86 je 10.0.10, ostatní 10.0.13 (`winget install Microsoft.DotNet.DesktopRuntime.10 --architecture x86`).
4. Ověřit chybové stavy Pohody (špatné heslo, modální dialog → timeout 2 min) a srozumitelnost hlášek v UI.

### C. Plán rozšíření 1: plánované úlohy (Task Scheduler)

Nejdřív s uživatelem rozhodnout **co a kdy** automatizovat (CLI je připravené). Kandidáti: noční `napohodu docdirs -c … --apply` pro aktuální ročníky (poznačení souborů zatříděných ručně), pravidelný `audit-export` na konci měsíce, kontrola konfigurace/dostupnosti Pohody. Pak: skript pro registraci úloh (`tools/schedule.ps1`), běh při zapnutém PC bez nutnosti přihlášení řešit opatrně (Pohoda.exe potřebuje uživatelskou relaci), výsledky do logu.

### D. Plán rozšíření 2/3: sběr a import faktur

Až po B.1 (testovací jednotka). Ověřit XML import přijaté faktury se záložkou Dokumenty (`docs/pohoda/import_faktury_vcetne_zalozky_dokumenty.xml`) na testovací jednotce, pak sběr z adresáře (přejmenované soubory), e-mail (MailKit) a AI analýza (později). Otevřená otázka dobropisů/zálohových faktur se tím stane aktuální.

**Úhrada přijaté faktury pokladnou (dotaz uživatele 2026-10-18, zatím jen rozbor XSD, neověřeno v Pohodě):**

- Podle `voucher.xsd` jde: výdajový pokladní doklad (`vch:voucherHeader/vch:voucherType = expense`, pokladna `vch:cashAccount`) s položkou `vch:voucherDetail/vch:voucherLiquidationItem` („Pouze pro import“). Ta obsahuje `vch:settingsLiquidation` (`sourceAgenda = receivedInvoice`, `sourceDocument/typ:number` nebo `typ:id`, `liquidationPrice` pro částečnou úhradu) a `vch:liquidationItem` (sazba DPH, částka, text, párovací symbol).
- Nejde: importovat fakturu rovnou jako uhrazenou (`inv:liquidation` je „Pouze pro export, při importu je ignorováno“). `liquidationWithoutLink.xsd` = likvidace bez dokladu, `automaticLiquidation.xsd` = jen bankovní doklady.
- Import faktury a úhrada najednou: jeden `dat:dataPack` se dvěma `dat:dataPackItem` (faktura, pak výdajový doklad odkazující na její číslo). **Ověřit na testovací jednotce**: zpracuje Pohoda položky postupně, aby doklad fakturu našel? Jinak dvě volání (import faktury → ID z odpovědi → pokladní doklad). Dál ověřit jednoznačnost čísla (`numberRequested` / z odpovědi), chování při selhání úhrady (faktura založená, úhrada ne), DPH na položce likvidace a zaúčtování.
- Postup: připravit ukázkový XML balíček (faktura + výdajový pokladní doklad) a vyzkoušet jen na testovací jednotce (B.1), ne na ostrých datech.
