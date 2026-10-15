# Fixtures

Reálné XML odpovědi Pohody (a později kopie MDB) pro testy parserů.

- Před commitem **anonymizovat**: názvy firem, IČ/DIČ, adresy, jména, čísla účtů.
- Zachovat původní kódování (windows-1250) a XML deklaraci, aby testy pokrývaly i dekódování.
- `Synthetic/` = ručně sestavené odpovědi podle XSD (ne reálný výstup Pohody); nahradit reálnými, jakmile budou.
- `.gitattributes` drží XML jako `-text`, aby git neměnil bajty ani konce řádků.
- Pojmenovat podle požadavku, např. `listAccountingUnit.response.xml`, `listInvoice-received-attachments.response.xml`.
