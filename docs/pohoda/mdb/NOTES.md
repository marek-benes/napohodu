# Pohoda MDB — poznatky z discovery

Zjištěno na kopii datového souboru přes `mdb-diff.ps1` (před/po ručním poznačení
adresáře dokumentů u faktury v Pohodě).

## Tabulka `DocDirs` — poznačení adresáře dokumentů

Poznačení **není sloupec v tabulce agendy** (FA apod.), ale samostatná vazební
tabulka `DocDirs` — jedna řádka na záznam agendy:

| Sloupec   | Význam |
|---|---|
| `ID`      | primární klíč (nové řádky vkládáme jako MAX+1) |
| `RelAgID` | ID agendy — **vydané faktury `2`, přijaté faktury `3`** (pozorováno) |
| `RelSubID`| ve všech pozorovaných řádcích `NULL` (u faktur se nepoužívá); při INSERTu vynecháváme |
| `RelID`   | ID záznamu ve zdrojové tabulce agendy (např. `FA.ID`) |
| `Dir`     | cesta relativně od složky firmy pod Dokumenty, backslashe, bez koncového `\`; může být i `NULL` |

Pozorované chování (diff 2026-07):

- poznačení nové faktury = `INSERT` nové řádky do `DocDirs`
- existovala i řádka s `Dir = NULL`, kterou Pohoda při poznačení jen doplnila
  (`UPDATE`) — řádky s prázdným `Dir` tedy existují a je třeba s nimi počítat

Pozorované tvary `Dir` — struktura se liší podle agendy:

- přijaté: `Fakturace\Přijaté faktury\2611\26110009` (s prefixovou úrovní)
- vydané: `Fakturace\Vydané faktury\10090262` (plochá, bez prefixu)

`mdb-mark-docs.ps1` proto skenuje obě varianty a jako očekávaný `Dir` bere
skutečné umístění složky na disku.

## Ověřené / neověřené předpoklady

- [x] `DocDirs` schéma a formát `Dir` (viz výše)
- [x] `RelAgID` pro vydané faktury = `2` (diff 2026-07)
- [x] vydané i přijaté faktury sdílejí tabulku `FA`, rozlišené sloupcem
      `RelTpFak`; FA sama žádný odkaz na adresář nemá — vazba je jen
      `DocDirs (RelAgID, RelID) → FA.ID`
- [x] `RelTpFak`: vydané faktury = `1`, přijaté faktury = `11`
      (ověřeno dotazem 2026-07 na FA.ID 1825 / 4995)
- [x] `DocDirs.ID` i `FA.ID` jsou **AutoNumber** (schéma reálné `_2025.mdb`, 2026-10-15).
      2. generace proto vkládá bez ID; AutoNumber přidělil MAX+1 (ověřeno na kopii).
      **Ostrý zápis 2. generace úspěšný** (uživatel 2026-10-18): Pohoda vložený řádek akceptuje.
- [x] `inv:id` z XML exportu = `FA.ID` (např. 4451 ↔ `Cislo` 251100002); `FA.Cislo` je TEXT(32).
- [x] Ve `DocDirs` jsou i jiné agendy (RelAgID 17, 19, 25, 28 – neidentifikováno); pracujeme jen s `RelAgID = 3`.
- [x] Na disku se může stejné číslo vyskytnout ploše i pod prefixem (`…\251100001` i `…\2511\251100001`);
      rekonciliace takové číslo přeskočí a nahlásí, Pohoda měla poznačenou plochou variantu.

## Praktické poznámky

- **Historické ročníky**: Pohoda je otevírá jako „účetnictví minulých let" a
  používání dokumentů v globálním nastavení už tam nejde zapnout. Řádky vložené
  do `DocDirs` jsou pak v UI neaktivní, ale neškodí — poznačujeme je i tak, ať
  DB odpovídá disku; u ročníků se zapnutými Dokumenty se projeví normálně.
  Ověřeno zápisem do kopie ročníku 2009 (2× INSERT, commit OK).
- **Ověřeno end-to-end (2026-07)**: INSERT i UPDATE větev na kopii `_2026.mdb`
  (přijaté faktury, RelAgID 3) — Pohoda vložené/doplněné řádky akceptuje,
  záložka Dokumenty zobrazuje obsah adresáře.
- **MDB je per rok, Dokumenty jsou společné pro všechny roky.** Adresáře
  faktur z jiných ročníků skončí v reportu jako „není v DB" (ověřeno dry-runem
  2026-07: složky `2009xxxx` nejsou v `_2026.mdb`). Rekonciliaci je nutné
  pustit zvlášť proti datovému souboru každého roku, jehož faktury mají
  na disku dokumenty.

- Jet 4.0 OLEDB provider je jen 32bit → skripty spouštět ve 32bit PowerShellu
  (`C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe`)
- otevřený datový soubor poznáš podle lock souboru `.ldb` vedle `.mdb`
- zápis vždy: zavřená jednotka v Pohodě → záloha → exkluzivní režim → transakce
- po zápisu ověřit v Pohodě záložku Dokumenty na dotčené faktuře

## 2. generace (C#, `src/NaPohodu.Core/Mdb`)

- `System.Data.OleDb` v x86 procesu; potřebuje vestavěné COM, takže **žádné AOT ani trimming**
  (jinak „requires MDAC“ – ověřeno).
- Čtení sdíleně (`Mode=Read`, smí běžet i při otevřené Pohodě); zápis jen bez `.ldb` → záloha do
  `Pohoda:BackupPath` (výchozí `%LOCALAPPDATA%\NaPohodu\Zalohy`, nikdy datová složka, úklid na `BackupsToKeep`)
  → `Mode=Share Exclusive` → jedna transakce s opětovnou kontrolou stavu (INSERT jen bez řádku,
  UPDATE jen prázdného `Dir`; jiný `Dir` = konflikt, nepřepisuje se).
- Pozor: Jet po zavření sdíleného spojení smaže „svůj“ `.ldb`; zámek otevřené Pohody zůstává.
- CLI: `napohodu docdirs -c <dataFile> [--apply]` (rekonciliace disk ↔ DB jako `mdb-mark-docs.ps1`),
  `napohodu filing … --apply --mark-docs` (poznačí jen právě zatříděné).

## Skripty

- `mdb-discover.ps1` — výpis tabulek a sloupců (read-only)
- `mdb-diff.ps1` — diff dvou kopií MDB; zdroj poznatků výše
- `mdb-mark-docs.ps1` — rekonciliace `DocDirs` podle adresářové struktury
  vydaných faktur (dry-run default, zápis s `-Write`)
