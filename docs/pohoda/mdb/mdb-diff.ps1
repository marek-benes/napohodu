# mdb-diff.ps1 — porovná dvě kopie Pohoda MDB a vypíše rozdíly po tabulkách/řádcích/sloupcích.
#
# Účel: zjistit, do kterého sloupce (a v jakém formátu) Pohoda ukládá poznačení
# adresáře dokumentů u faktury. Postup:
#   1. Zavři účetní jednotku v Pohodě a udělej kopii MDB → "before".
#   2. V Pohodě otevři jednu testovací vydanou fakturu a poznač jí adresář
#      dokumentů (záložka Dokumenty). Zavři jednotku, udělej druhou kopii → "after".
#   3. Spusť diff (32bit PowerShell!):
#      & "C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass `
#        -File .\mdb-diff.ps1 -Before .\before.mdb -After .\after.mdb
#
# Tip: celá databáze má stovky tabulek a diff může trvat dlouho. Pokud víš,
# že jde o faktury, omez ho: -Table FA  (víc tabulek: -Table FA,FApol)
#
# Skript je jen pro čtení, živého datového souboru se nedotýká.

param(
  [Parameter(Mandatory = $true)][string]$Before,
  [Parameter(Mandatory = $true)][string]$After,
  [string[]]$Table = @()
)

if ([Environment]::Is64BitProcess) {
  Write-Host "Jet 4.0 provider je jen 32bit. Spust skript ve 32bit PowerShellu:" -ForegroundColor Red
  Write-Host "  C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ForegroundColor Red
  exit 1
}

function Open-Mdb([string]$path) {
  if (-not (Test-Path $path)) { throw "Soubor nenalezen: $path" }
  $c = New-Object -ComObject ADODB.Connection
  $c.Open("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$path;Mode=Read")
  return $c
}

function Get-UserTables($conn) {
  $rs = $conn.OpenSchema(20)   # adSchemaTables
  $tables = @()
  while (-not $rs.EOF) {
    if ($rs.Fields.Item("TABLE_TYPE").Value -eq "TABLE") {
      $tables += [string]$rs.Fields.Item("TABLE_NAME").Value
    }
    $rs.MoveNext()
  }
  $rs.Close()
  return $tables
}

function Format-Value($v) {
  if ($null -eq $v -or $v -is [System.DBNull]) { return '<NULL>' }
  if ($v -is [byte[]]) { return "<binary $($v.Length) B>" }
  return [string]$v
}

# Načte celou tabulku do hashtable klíčované sloupcem ID (Pohoda ho má všude);
# bez ID se klíčuje pořadím řádku (porovnání je pak jen orientační).
function Read-Table($conn, $table) {
  $rs = $conn.Execute("SELECT * FROM [$table]")
  $cols = @()
  for ($i = 0; $i -lt $rs.Fields.Count; $i++) { $cols += $rs.Fields.Item($i).Name }
  $hasId = $cols -contains 'ID'
  $rows = @{}
  $n = 0
  while (-not $rs.EOF) {
    $row = @{}
    foreach ($c in $cols) { $row[$c] = Format-Value $rs.Fields.Item($c).Value }
    $key = if ($hasId) { $row['ID'] } else { "#$n" }
    $rows[$key] = $row
    $n++
    $rs.MoveNext()
  }
  $rs.Close()
  return @{ Columns = $cols; Rows = $rows; HasId = $hasId }
}

$connB = Open-Mdb $Before
$connA = Open-Mdb $After

$tablesB = Get-UserTables $connB
$tablesA = Get-UserTables $connA
$tables = if ($Table.Count -gt 0) { $Table } else { $tablesB | Where-Object { $tablesA -contains $_ } | Sort-Object }

$totalDiffs = 0

foreach ($t in $tables) {
  $b = Read-Table $connB $t
  $a = Read-Table $connA $t
  $lines = @()

  $keys = @($b.Rows.Keys) + @($a.Rows.Keys) | Select-Object -Unique
  foreach ($k in $keys) {
    $inB = $b.Rows.ContainsKey($k)
    $inA = $a.Rows.ContainsKey($k)
    if ($inB -and -not $inA) {
      $lines += "- radek ID=$k odstranen"
    }
    elseif ($inA -and -not $inB) {
      $vals = ($a.Columns | Where-Object { $a.Rows[$k][$_] -ne '<NULL>' } |
        ForEach-Object { "$_='$($a.Rows[$k][$_])'" }) -join ', '
      $lines += "+ radek ID=$k pridan: $vals"
    }
    else {
      foreach ($c in $a.Columns) {
        if ($b.Rows[$k][$c] -ne $a.Rows[$k][$c]) {
          $lines += "~ ID=$k  [$c]: '$($b.Rows[$k][$c])' -> '$($a.Rows[$k][$c])'"
        }
      }
    }
  }

  if ($lines.Count -gt 0) {
    Write-Host "`n=== $t ===" -ForegroundColor Cyan
    if (-not $b.HasId) { Write-Host "(tabulka nema sloupec ID - porovnano po poradi radku)" -ForegroundColor Yellow }
    $lines | ForEach-Object { Write-Host $_ }
    $totalDiffs += $lines.Count
  }
}

if ($totalDiffs -eq 0) {
  Write-Host "`nZadne rozdily." -ForegroundColor Green
} else {
  Write-Host "`nCelkem rozdilu: $totalDiffs" -ForegroundColor Cyan
}

$connB.Close()
$connA.Close()
