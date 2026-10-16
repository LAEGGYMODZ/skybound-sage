$ErrorActionPreference='Stop'
$taskCompiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$taskBuild=Join-Path $PSScriptRoot 'build'
New-Item -ItemType Directory -Path $taskBuild -Force | Out-Null
$taskLibrary=Join-Path $taskBuild 'SkyboundSage.RequestGuard.dll'
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:library ('/out:'+$taskLibrary) (Join-Path $PSScriptRoot 'RequestGuard.cs') (Join-Path $PSScriptRoot 'AssemblyInfo.cs')
if($LASTEXITCODE -ne 0){throw 'Guard compilation failed.'}
$taskRunner=Join-Path $taskBuild 'RequestGuardTests.exe'
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:exe ('/reference:'+$taskLibrary) ('/out:'+$taskRunner) (Join-Path $PSScriptRoot 'RequestGuardTests.cs')
if($LASTEXITCODE -ne 0){throw 'Guard test compilation failed.'}
& $taskRunner
if($LASTEXITCODE -ne 0){throw 'Guard checks failed.'}
Write-Output 'Offline protocol checks complete. No game adapter, installation or effect.'
