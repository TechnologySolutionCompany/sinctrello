$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$env:DOTNET_CLI_HOME = Join-Path $root '.tools/cli'
$env:NUGET_PACKAGES = Join-Path $root '.tools/packages'
$dotnet = Join-Path $root '.tools/dotnet/dotnet.exe'
if (!(Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet run --project (Join-Path $root 'src/SincTrello.App')
exit $LASTEXITCODE
