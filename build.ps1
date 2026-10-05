<#
.SYNOPSIS
  Builds ChuteFX.dll with the .NET Framework C# compiler, optionally deploys it to KSP and packages a release zip.

.PARAMETER KspPath
  Path of the KSP installation (folder that contains KSP_x64_Data). Defaults to the environment variable KSP_PATH.

.PARAMETER Deploy
  Copy GameData/ChuteFX into <KspPath>/GameData (KSP must be closed: it locks the DLL while running).

.PARAMETER Package
  Create ChuteFX-<version>.zip (GameData/ChuteFX plus README and LICENSE) in the repository root.

.EXAMPLE
  ./build.ps1 -KspPath "C:\Games\Kerbal Space Program" -Deploy
#>
param(
    [string]$KspPath = $env:KSP_PATH,
    [switch]$Deploy,
    [switch]$Package
)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
if (-not $KspPath) { throw 'Set -KspPath or the environment variable KSP_PATH to your KSP installation.' }
$managed = Join-Path $KspPath 'KSP_x64_Data\Managed'
if (-not (Test-Path $managed)) { throw "Not a KSP installation (missing $managed)." }

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$refs = 'mscorlib', 'System', 'System.Core', 'UnityEngine', 'UnityEngine.CoreModule', 'UnityEngine.PhysicsModule',
        'UnityEngine.AnimationModule', 'UnityEngine.UI', 'UnityEngine.InputLegacyModule', 'Assembly-CSharp' |
        ForEach-Object { '-r:' + (Join-Path $managed "$_.dll") }
$out = Join-Path $root 'GameData\ChuteFX\Plugins\ChuteFX.dll'
& $csc -nologo -target:library -nostdlib -noconfig -codepage:65001 -optimize "-out:$out" $refs (Join-Path $root 'Source\ChuteFX.cs')
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
Write-Host "Built $out"

if ($Deploy) {
    $dest = Join-Path $KspPath 'GameData\ChuteFX'
    New-Item -ItemType Directory -Force -Path $dest | Out-Null
    Copy-Item -Recurse -Force (Join-Path $root 'GameData\ChuteFX\*') $dest
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $dest 'Plugins\.gitkeep')
    Write-Host "Deployed to $dest"
}

if ($Package) {
    $ver = (Get-Content (Join-Path $root 'GameData\ChuteFX\ChuteFX.version') -Raw | ConvertFrom-Json).VERSION
    $name = "ChuteFX-$($ver.MAJOR).$($ver.MINOR).$($ver.PATCH).zip"
    $tmp = Join-Path ([IO.Path]::GetTempPath()) ('ChuteFX_pkg_' + [Guid]::NewGuid())
    New-Item -ItemType Directory -Path (Join-Path $tmp 'GameData') | Out-Null
    Copy-Item -Recurse (Join-Path $root 'GameData\ChuteFX') (Join-Path $tmp 'GameData\ChuteFX')
    Remove-Item -Force -ErrorAction SilentlyContinue (Join-Path $tmp 'GameData\ChuteFX\Plugins\.gitkeep')
    Copy-Item (Join-Path $root 'README.md'), (Join-Path $root 'LICENSE') $tmp
    $zip = Join-Path $root $name
    if (Test-Path $zip) { Remove-Item $zip }
    Compress-Archive -Path (Join-Path $tmp '*') -DestinationPath $zip
    Remove-Item -Recurse -Force $tmp
    Write-Host "Packaged $zip"
}
