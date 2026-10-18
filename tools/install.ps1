# Instalace / aktualizace produkční NaPohodu na tomto počítači (aktuální účet Windows).
#
#   powershell -ExecutionPolicy Bypass -File tools\install.ps1
#
# - Produkce je oddělená od vývoje: program v %LOCALAPPDATA%\Programs\NaPohodu, data (zálohy MDB)
#   v %LOCALAPPDATA%\NaPohodu, port 5080. Vývoj (dotnet run) běží jako „NaPohodu-vyvoj“ na portu 5081.
# - Produkční appsettings.Local.json (heslo do Pohody) se při aktualizaci nikdy nepřepisuje.
#   Při první instalaci se převezme z vývojové složky (src\NaPohodu.App), jinak ze vzoru.
# - Na plochu vytvoří zástupce „NaPohodu“ (napohodu.exe serve, minimalizované okno).

param(
    [string]$Target = (Join-Path $env:LOCALAPPDATA 'Programs\NaPohodu'),
    [switch]$SkipTests,
    [switch]$NoShortcut
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repo 'src\NaPohodu.App'
$exe = Join-Path $Target 'napohodu.exe'
$localConfig = 'appsettings.Local.json'

function Step($text) { Write-Host "`n== $text" -ForegroundColor Cyan }

if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw 'Chybí .NET SDK (příkaz dotnet).'
}

if (-not $SkipTests) {
    Step 'Testy'
    dotnet test --solution (Join-Path $repo 'NaPohodu.slnx') --nologo
    if ($LASTEXITCODE -ne 0) { throw 'Testy neprošly, instalace zastavena.' }
}

Step 'Sestavení produkční verze'
$staging = Join-Path ([IO.Path]::GetTempPath()) ("napohodu-install-" + [guid]::NewGuid().ToString('N'))
dotnet publish $project -c Release -o $staging --nologo
if ($LASTEXITCODE -ne 0) { throw 'Sestavení selhalo.' }
if (Test-Path (Join-Path $staging $localConfig)) { throw "Publikace obsahuje $localConfig – zastaveno, aby se nepřepsala produkční konfigurace." }

try {
    Step "Instalace do $Target"
    $running = Get-Process napohodu -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path -like "$Target\*" }
    if ($running) {
        Write-Host 'Ukončuji běžící produkční NaPohodu (po instalaci ji spusťte zástupcem znovu).' -ForegroundColor Yellow
        $running | Stop-Process -Force
        $running | Wait-Process -Timeout 15 -ErrorAction SilentlyContinue
    }

    New-Item -ItemType Directory -Force $Target | Out-Null
    # /MIR smaže v cíli soubory staré verze; /XF chrání produkční konfiguraci před kopírováním i mazáním.
    robocopy $staging $Target /MIR /XF $localConfig /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "Kopírování selhalo (robocopy $LASTEXITCODE)." }

    $commit = (git -C $repo log -1 --format='%h %cd' --date=format:'%Y-%m-%d %H:%M' 2>$null)
    $dirty = (git -C $repo status --porcelain 2>$null)
    Set-Content (Join-Path $Target 'VERZE.txt') -Encoding UTF8 -Value @(
        "Commit: $commit$(if ($dirty) { ' + necommitnuté změny' })",
        "Instalováno: $(Get-Date -Format 'yyyy-MM-dd HH:mm')"
    )

    $targetConfig = Join-Path $Target $localConfig
    if (Test-Path $targetConfig) {
        Write-Host "Produkční $localConfig ponechán beze změny."
    }
    elseif (Test-Path (Join-Path $project $localConfig)) {
        Copy-Item (Join-Path $project $localConfig) $targetConfig
        Write-Host "Produkční $localConfig převzat z vývojové složky (přihlašovací údaje do Pohody)."
    }
    else {
        Copy-Item (Join-Path $Target 'appsettings.Local.example.json') $targetConfig
        Write-Host "Vyplňte přihlašovací údaje do $targetConfig" -ForegroundColor Yellow
    }

    if (-not $NoShortcut) {
        Step 'Zástupce na ploše'
        $shortcutPath = Join-Path ([Environment]::GetFolderPath('Desktop')) 'NaPohodu.lnk'
        $shell = New-Object -ComObject WScript.Shell
        $shortcut = $shell.CreateShortcut($shortcutPath)
        $shortcut.TargetPath = $exe
        $shortcut.Arguments = 'serve'
        $shortcut.WorkingDirectory = $Target
        $shortcut.WindowStyle = 7          # minimalizované okno konzole
        $shortcut.Description = 'NaPohodu – doplněk k účetnictví POHODA'
        $shortcut.IconLocation = "$exe,0"
        $shortcut.Save()
        Write-Host "Zástupce: $shortcutPath"
    }

    Step 'Kontrola konfigurace'
    & $exe config
}
finally {
    Remove-Item -Recurse -Force $staging -ErrorAction SilentlyContinue
}

Write-Host "`nHotovo. NaPohodu spustíte zástupcem na ploše (adresa http://127.0.0.1:5080)." -ForegroundColor Green
