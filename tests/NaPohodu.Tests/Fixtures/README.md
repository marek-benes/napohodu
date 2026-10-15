# Fixtures

`Real/` = **anonymizované reálné odpovědi Pohody** (verze 14400.171, 2026-10-15). Konstanty a popis jsou v `RealFixtures.cs`.

| Soubor | Obsah |
|---|---|
| `listAccountingUnit.response.xml` | seznam 83 účetních jednotek |
| `listInvoice.received.2025.response.xml` | vzorek 53 z 484 přijatých faktur, 36 se záložkou Dokumenty (prefixovaná i plochá podsložka, soubory nepojmenované číslem) |
| `listInvoice.received.2026-without-documents.response.xml` | vzorek 16 z 248 faktur jednotky, která zatím nemá žádné dokumenty |
| `error.not-for-this-unit.response.xml` | odmítnutý balíček bez `ico` („Tento balíček není určen pro tuto jednotku.“) |

## Nová fixture

1. Uložit odpověď: `napohodu … --save-response tmp/<název>.xml` (`tmp/` je mimo git, obsahuje osobní údaje).
2. Anonymizovat **všechny související soubory jedním během** (stejné IČ → stejná náhrada napříč soubory):

   ```
   dotnet run tools/anonymize-fixture.cs -- tests/NaPohodu.Tests/Fixtures/Real tmp/acu.xml=listAccountingUnit.response.xml tmp/inv.xml=…
   ```

   Nástroj ponechá jen explicitně povolená neosobní pole, na neznámé pole skončí chybou, exporty faktur zmenší na vzorek
   a na konci ověří, že ve výstupu není žádná původní citlivá hodnota (jinak výstupy smaže).
3. Výstup projít očima a doplnit testy. Přegenerování mění pořadová čísla náhrad, testy pak upravit.

XML zůstávají ve windows-1250 a `.gitattributes` je drží jako `-text`, aby git neměnil bajty ani konce řádků.
