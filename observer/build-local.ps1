$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'Existing .NET Framework compiler was not found.' }
$taskBuild=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskLibrary=Join-Path $taskBuild 'SkyboundSage.ObserverFoundation.dll'
$taskRunner=Join-Path $taskBuild 'SkyboundSage.SubscriptionTests.exe'
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:library "/out:$taskLibrary" (Join-Path $PSScriptRoot 'src\OwnedSubscription.cs')
if ($LASTEXITCODE -ne 0) { throw 'Helper compilation failed.' }
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:exe "/reference:$taskLibrary" "/out:$taskRunner" (Join-Path $PSScriptRoot 'tests\SubscriptionTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $taskRunner
if ($LASTEXITCODE -ne 0) { throw 'Helper verification failed.' }
Write-Output 'Compiled and tested the local helper. It has no game entry point and was not installed in Wukong.'
