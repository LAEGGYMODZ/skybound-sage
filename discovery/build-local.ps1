param([Parameter(Mandatory=$true)][string]$LoaderInterfacePath)
$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Existing .NET Framework compiler was not found.' }
$taskInterface=(Resolve-Path -LiteralPath $LoaderInterfacePath).Path
if ((Get-FileHash -LiteralPath $taskInterface -Algorithm SHA256).Hash.ToLowerInvariant() -ne 'f057030a3b3dc4ef4c8b38fa947ffa92b2e7652b8efd07b7b94605c504a31732') { throw 'The interface does not match the reviewed release.' }
$taskBuild=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskCatalog=Join-Path $taskBuild 'SkyboundSage.FocusedCatalog.dll'
$taskProbe=Join-Path $taskBuild 'SkyboundSageFocused.dll'
$taskRunner=Join-Path $taskBuild 'SkyboundSage.FocusedTests.exe'
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:library "/out:$taskCatalog" (Join-Path $PSScriptRoot 'src\ApiCatalog.cs')
if ($LASTEXITCODE -ne 0) { throw 'Catalog compilation failed.' }
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:library "/reference:$taskInterface" "/out:$taskProbe" (Join-Path $PSScriptRoot 'src\ApiCatalog.cs') (Join-Path $PSScriptRoot 'src\DiscoveryProbe.cs') (Join-Path $PSScriptRoot 'src\AssemblyInfo.cs')
if ($LASTEXITCODE -ne 0) { throw 'Module compilation failed.' }
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:exe "/reference:$taskCatalog" "/out:$taskRunner" (Join-Path $PSScriptRoot 'tests\FocusedTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Fixture test compilation failed.' }
& $taskRunner
if ($LASTEXITCODE -ne 0) { throw 'Fixture verification failed.' }
Write-Output 'Local compilation and fixture checks complete. Nothing installed or launched in Wukong.'
