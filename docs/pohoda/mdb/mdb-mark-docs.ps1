# mdb-mark-docs.ps1 — rekonciliace poznačení adresáře dokumentů u vydaných faktur.
#
# Zjištěno přes mdb-diff.ps1 (viz NOTES.md): Pohoda ukládá poznačení do tabulky
# DocDirs (ID, RelAgID, RelID, Dir) — jedna řádka na záznam agendy:
#   RelAgID = ID agendy: vydané faktury = 2, přijaté faktury = 3 (pozorováno)
#   RelID   = ID záznamu ve zdrojové tabulce (FA.ID)
#   Dir     = cesta adresáře relativně od složky firmy, backslashe,
#             např. 'Fakturace\Vydané faktury\10090262'
#
# Skript projde <DocumentsRoot>\<InvoicesSubPath> (plochou i prefixovou
# strukturu — viz komentář u skenu) a pro každou fakturu s neprázdným
# adresářem zajistí odpovídající řádku v DocDirs:
#   - řádka neexistuje              -> INSERT
#   - řádka existuje s Dir NULL/''  -> UPDATE (pozorováno, Pohoda takové řádky mívá)
#   - Dir odpovídá                  -> ok, nic
#   - Dir je jiný                   -> jen report, NIKDY nepřepisuje
#
# Bezpečnostní zásady:
#   - defaultně DRY-RUN: jen vypíše, co by se změnilo; zápis až s -Write
#   - kontrola lock souboru (.ldb) — Pohoda nesmí mít jednotku otevřenou
#   - před zápisem záloha MDB vedle originálu
#   - všechny zápisy v jedné transakci, MDB otevřeno exkluzivně
#   - nové ID = MAX(ID)+1 (explicitní vklad do AutoNumber Jet povoluje;
#     díky exkluzivnímu režimu bez souběhu)
#
# ⚠ NEŽ POUŽIJEŠ s -Write:
#   1. ověř filtr vydaných faktur (-Where, předpoklad "RelTpFak = 1")
#   2. spusť dry-run a zkontroluj report
#
# Spuštění (32bit PowerShell!):
#   & "C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ExecutionPolicy Bypass `
#     -File .\mdb-mark-docs.ps1 `
#     -Mdb "C:\napohodu-test\46202153_2026_kopie.mdb" `
#     -DocumentsRoot "C:\...\Dokumenty\BlueTrail" `
#     -RelAgID 2 `
#     -Where "RelTpFak = 1"
#   ...a teprve po kontrole reportu totéž s -Write.

param(
  [Parameter(Mandatory = $true)][string]$Mdb,
  [Parameter(Mandatory = $true)][string]$DocumentsRoot,
  [Parameter(Mandatory = $true)][int]$RelAgID,
  [string]$DocTable = 'DocDirs',
  [string]$InvoiceTable = 'FA',
  [string]$NumberColumn = 'Cislo',
  [string]$Where = '',
  [string]$InvoicesSubPath = 'Fakturace\Vydané faktury',
  [switch]$Write
)

$ErrorActionPreference = 'Stop'

if ([Environment]::Is64BitProcess) {
  Write-Host "Jet 4.0 provider je jen 32bit. Spust skript ve 32bit PowerShellu:" -ForegroundColor Red
  Write-Host "  C:\Windows\SysWOW64\WindowsPowerShell\v1.0\powershell.exe" -ForegroundColor Red
  exit 1
}

if (-not (Test-Path $Mdb)) { throw "MDB nenalezeno: $Mdb" }
$invoicesDir = Join-Path $DocumentsRoot $InvoicesSubPath
if (-not (Test-Path $invoicesDir)) { throw "Adresar vydanych faktur nenalezen: $invoicesDir" }

# --- kontrola, ze Pohoda nema datovy soubor otevreny (lock soubor) ---
$lockFiles = @(
  [IO.Path]::ChangeExtension($Mdb, '.ldb'),
  [IO.Path]::ChangeExtension($Mdb, '.laccdb')
) | Where-Object { Test-Path $_ }
if ($lockFiles.Count -gt 0) {
  throw "Datovy soubor je otevreny (existuje lock: $($lockFiles -join ', ')). Zavri ucetni jednotku v Pohode."
}

# --- 1) sken filesystemu: cislo faktury -> adresar s dokumenty ---
$systemFiles = @('desktop.ini', 'thumbs.db', 'ehthumbs.db', '.ds_store')
function Test-RealFile($file) {
  -not ($file.Name.StartsWith('.') -or $systemFiles -contains $file.Name.ToLower())
}

$fsFolders = @{}   # cislo faktury -> @{ RelPath; FileCount }

function Add-FsFolder([string]$number, [string]$relPath, [int]$fileCount) {
  if ($fsFolders.ContainsKey($number)) {
    Write-Host "POZOR: cislo $number nalezeno ve dvou adresarich ('$($fsFolders[$number].RelPath)' i '$relPath') - preskakuji obe." -ForegroundColor Yellow
    $fsFolders[$number] = @{ RelPath = $null; FileCount = 0 }
    return
  }
  $fsFolders[$number] = @{ RelPath = $relPath; FileCount = $fileCount }
}

function Get-RealFileCount([string]$dir) {
  @(Get-ChildItem -LiteralPath $dir -File -Recurse -Force |
    Where-Object { Test-RealFile $_ }).Count
}

# Pozorovane dve varianty struktury (viz NOTES.md):
#   plocha:      <root>\<cislo faktury>\...          (vydane faktury, diff 2026-07)
#   s prefixem:  <root>\<prefix>\<cislo faktury>\... (prijate faktury)
# Skenujeme obe: adresar prvni urovne s neprazdnym obsahem = kandidat na cislo
# faktury; jeho podadresare = kandidati v prefixove strukture. O tom, co je
# skutecne faktura, rozhodne az parovani proti DB (zbytek skonci v reportu).
foreach ($level1 in Get-ChildItem -LiteralPath $invoicesDir -Directory) {
  $directFiles = @(Get-ChildItem -LiteralPath $level1.FullName -File -Force |
    Where-Object { Test-RealFile $_ })
  if ($directFiles.Count -gt 0) {
    # plocha varianta: slozka pojmenovana cislem faktury primo pod rootem
    Add-FsFolder $level1.Name (Join-Path $InvoicesSubPath $level1.Name) (Get-RealFileCount $level1.FullName)
  }
  foreach ($level2 in Get-ChildItem -LiteralPath $level1.FullName -Directory) {
    # prefixova varianta: <prefix>\<cislo>
    $count = Get-RealFileCount $level2.FullName
    if ($count -eq 0) { continue }   # prazdny adresar se nepoznacuje
    Add-FsFolder $level2.Name (Join-Path $InvoicesSubPath (Join-Path $level1.Name $level2.Name)) $count
  }
}
Write-Host "Filesystem: $($fsFolders.Count) faktur s neprazdnym adresarem dokumentu." -ForegroundColor Cyan

# --- 2) zaloha (pred exkluzivnim otevrenim - jinak Copy-Item narazi na vlastni
#         zamek) a nacteni faktur a DocDirs z MDB ---
if ($Write) {
  $backup = [IO.Path]::ChangeExtension($Mdb, $null).TrimEnd('.') + ('.backup-{0:yyyyMMdd-HHmmss}.mdb' -f (Get-Date))
  Copy-Item -LiteralPath $Mdb -Destination $backup
  Write-Host "Zaloha: $backup" -ForegroundColor Cyan
}

$conn = New-Object -ComObject ADODB.Connection
$mode = if ($Write) { 'Share Exclusive' } else { 'Read' }
$conn.Open("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$Mdb;Mode=$mode")

# faktury: cislo -> FA.ID
$sql = "SELECT ID, [$NumberColumn] FROM [$InvoiceTable]"
if ($Where -ne '') { $sql += " WHERE ($Where)" }
$invoices = @{}
$rs = $conn.Execute($sql)
while (-not $rs.EOF) {
  $num = ([string]$rs.Fields.Item($NumberColumn).Value).Trim()
  if ($num -ne '') {
    if ($invoices.ContainsKey($num)) {
      Write-Host "POZOR: duplicitni cislo faktury v DB: $num - preskakuji." -ForegroundColor Yellow
      $invoices[$num] = $null
    } else {
      $invoices[$num] = [int]$rs.Fields.Item('ID').Value
    }
  }
  $rs.MoveNext()
}
$rs.Close()
Write-Host "Databaze: $($invoices.Count) faktur (tabulka $InvoiceTable$(if ($Where) { ", filtr: $Where" }))." -ForegroundColor Cyan

# DocDirs pro danou agendu: RelID -> @{ Id; Dir }
$docDirs = @{}
$rs = $conn.Execute("SELECT ID, RelID, Dir FROM [$DocTable] WHERE RelAgID = $RelAgID")
while (-not $rs.EOF) {
  $dir = $rs.Fields.Item('Dir').Value
  if ($null -eq $dir -or $dir -is [System.DBNull]) { $dir = '' }
  $docDirs[[int]$rs.Fields.Item('RelID').Value] = @{
    Id  = [int]$rs.Fields.Item('ID').Value
    Dir = ([string]$dir).Trim()
  }
  $rs.MoveNext()
}
$rs.Close()
Write-Host "DocDirs: $($docDirs.Count) zaznamu pro RelAgID=$RelAgID." -ForegroundColor Cyan

# --- 3) porovnani ---
$toInsert = @(); $toUpdate = @(); $alreadyOk = @(); $mismatch = @(); $fsOnly = @()

foreach ($number in ($fsFolders.Keys | Sort-Object)) {
  $info = $fsFolders[$number]
  if ($null -eq $info.RelPath) { continue }   # duplicitni adresar — reportovano vyse
  if (-not $invoices.ContainsKey($number)) { $fsOnly += $number; continue }
  $faId = $invoices[$number]
  if ($null -eq $faId) { continue }   # duplicitni cislo — reportovano vyse
  $expected = $info.RelPath

  if (-not $docDirs.ContainsKey($faId)) {
    $toInsert += @{ Number = $number; FaId = $faId; Dir = $expected; Files = $info.FileCount }
  } else {
    $row = $docDirs[$faId]
    if ($row.Dir -eq '') {
      $toUpdate += @{ Number = $number; RowId = $row.Id; Dir = $expected; Files = $info.FileCount }
    } elseif ($row.Dir -eq $expected) {
      $alreadyOk += $number
    } else {
      $mismatch += "  $number (DocDirs ID $($row.Id)): v DB '$($row.Dir)', ocekavano '$expected'"
    }
  }
}

Write-Host ""
Write-Host "INSERT (nova radka):     $($toInsert.Count)" -ForegroundColor Green
$toInsert | ForEach-Object { Write-Host "  $($_.Number) (FA.ID $($_.FaId), souboru: $($_.Files)) -> '$($_.Dir)'" }
Write-Host "UPDATE (Dir byl prazdny): $($toUpdate.Count)" -ForegroundColor Green
$toUpdate | ForEach-Object { Write-Host "  $($_.Number) (DocDirs ID $($_.RowId)) -> '$($_.Dir)'" }
Write-Host "Uz poznaceno (ok):        $($alreadyOk.Count)"
if ($mismatch.Count -gt 0) {
  Write-Host "NESHODY (poznaceno jinak - NEPREPISUJI, zkontroluj rucne): $($mismatch.Count)" -ForegroundColor Yellow
  $mismatch | ForEach-Object { Write-Host $_ -ForegroundColor Yellow }
}
if ($fsOnly.Count -gt 0) {
  Write-Host "Adresar existuje, ale faktura neni v DB (zkontroluj -Where): $($fsOnly.Count)" -ForegroundColor Yellow
  $fsOnly | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
}

# --- 4) zapis ---
$changes = $toInsert.Count + $toUpdate.Count
if (-not $Write) {
  Write-Host "`nDRY-RUN: nic nezapsano. Pro zapis spust znovu s -Write." -ForegroundColor Cyan
  $conn.Close()
  exit 0
}
if ($changes -eq 0) {
  Write-Host "`nNeni co zapisovat." -ForegroundColor Green
  $conn.Close()
  exit 0
}

function Invoke-Update($conn, [string]$sql, [object[]]$params) {
  $cmd = New-Object -ComObject ADODB.Command
  $cmd.ActiveConnection = $conn
  $cmd.CommandText = $sql
  foreach ($p in $params) {
    # 202 = adVarWChar, 3 = adInteger, 1 = adParamInput
    if ($p -is [string]) {
      $cmd.Parameters.Append($cmd.CreateParameter('p', 202, 1, [Math]::Max($p.Length, 1), $p)) | Out-Null
    } else {
      $cmd.Parameters.Append($cmd.CreateParameter('p', 3, 1, 0, [int]$p)) | Out-Null
    }
  }
  $affected = 0
  $cmd.Execute([ref]$affected) | Out-Null
  return $affected
}

$conn.BeginTrans() | Out-Null
try {
  # nove ID: MAX+1 (exkluzivni rezim, zadny soubeh)
  $rs = $conn.Execute("SELECT MAX(ID) AS MaxId FROM [$DocTable]")
  $nextId = 1
  $maxVal = $rs.Fields.Item('MaxId').Value
  if ($null -ne $maxVal -and -not ($maxVal -is [System.DBNull])) { $nextId = [int]$maxVal + 1 }
  $rs.Close()

  foreach ($m in $toInsert) {
    $n = Invoke-Update $conn "INSERT INTO [$DocTable] (ID, RelAgID, RelID, Dir) VALUES (?, ?, ?, ?)" `
      @([int]$nextId, [int]$RelAgID, [int]$m.FaId, [string]$m.Dir)
    if ($n -ne 1) { throw "INSERT faktury $($m.Number) zasahl $n radku - rollback." }
    $nextId++
  }
  foreach ($m in $toUpdate) {
    $n = Invoke-Update $conn "UPDATE [$DocTable] SET Dir = ? WHERE ID = ?" `
      @([string]$m.Dir, [int]$m.RowId)
    if ($n -ne 1) { throw "UPDATE faktury $($m.Number) (DocDirs ID $($m.RowId)) zasahl $n radku - rollback." }
  }

  $conn.CommitTrans() | Out-Null
  Write-Host "Zapsano: $($toInsert.Count) INSERT, $($toUpdate.Count) UPDATE." -ForegroundColor Green
}
catch {
  $conn.RollbackTrans() | Out-Null
  Write-Host "CHYBA, transakce vracena zpet: $_" -ForegroundColor Red
  throw
}
finally {
  $conn.Close()
}
