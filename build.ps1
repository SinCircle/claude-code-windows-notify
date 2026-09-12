$ErrorActionPreference = 'Stop'
$compiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw 'The x64 .NET Framework C# compiler is required.' }
$output = Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $output -Force | Out-Null
& $compiler /nologo /target:winexe /platform:x64 /reference:System.Web.Extensions.dll "/out:$output\ClaudeNotify.exe" (Join-Path $PSScriptRoot 'src\Notifier.cs')
if ($LASTEXITCODE -ne 0) { throw 'Compilation failed.' }
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'src\ShowToast.ps1') -Destination $output -Force
Get-ChildItem -LiteralPath (Join-Path $PSScriptRoot 'assets') -File | Copy-Item -Destination $output -Force
Write-Output "Built notification helper in $output"
