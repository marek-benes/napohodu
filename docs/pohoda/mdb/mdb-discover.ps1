# Discovery Pohoda MDB — jen pro čtení. Spouštět ve 32bit PowerShellu:
#   C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe
#
# Nejdřív si udělej KOPII datového souboru (živý se nedotýkáme):
#   Copy-Item "C:\ProgramData\STORMWARE\POHODA\Data\46202153_2026.mdb" "C:\napohodu-test\46202153_2026_kopie.mdb"
#
# Spuštění:
#   & "C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass -File "C:\napohodu-test\mdb-discover.ps1"

$mdb = "C:\napohodu-test\46202153_2026_kopie.mdb"

$conn = New-Object -ComObject ADODB.Connection
$conn.Open("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$mdb;Mode=Read")

# --- 1) všechny uživatelské tabulky ---
$rs = $conn.OpenSchema(20)   # adSchemaTables
$tables = @()
while (-not $rs.EOF) {
  if ($rs.Fields.Item("TABLE_TYPE").Value -eq "TABLE") {
    $tables += [string]$rs.Fields.Item("TABLE_NAME").Value
  }
  $rs.MoveNext()
}
$rs.Close()

"=== VSECHNY TABULKY ($($tables.Count)) ==="
$tables | Sort-Object | ForEach-Object { $_ }

# --- 2) sloupce tabulek, jejichz nazev pripomina dokumenty ---
"`n=== TABULKY ~ dokument (nazev obsahuje 'dok') + sloupce ==="
foreach ($t in ($tables | Where-Object { $_ -match 'dok' } | Sort-Object)) {
  "--- $t ---"
  $cs = $conn.OpenSchema(4, @($null, $null, $t, $null))  # adSchemaColumns
  while (-not $cs.EOF) {
    "   {0}" -f $cs.Fields.Item("COLUMN_NAME").Value
    $cs.MoveNext()
  }
  $cs.Close()
}

$conn.Close()
