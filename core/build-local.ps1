$ErrorActionPreference = 'Stop'
# Build only this original project with the compiler already shipped on Windows.
# No download, installation, game access, Git operation or network request.
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw 'The existing .NET Framework compiler was not found.' }
$taskProjectRoot = [System.IO.Path]::GetFullPath($PSScriptRoot)
$taskBuildRoot = Join-Path $taskProjectRoot 'build'
New-Item -ItemType Directory -Path $taskBuildRoot -Force | Out-Null
$taskLibrary = Join-Path $taskBuildRoot 'SkyboundSage.Core.dll'
$taskRunner = Join-Path $taskBuildRoot 'SkyboundSage.Tests.exe'
$taskSources = @('src\AssemblyInfo.cs', 'src\CombatRules.cs', 'src\ObservationSession.cs') | ForEach-Object { Join-Path $taskProjectRoot $_ }
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:library /platform:anycpu "/out:$taskLibrary" $taskSources
if ($LASTEXITCODE -ne 0) { throw 'Core compilation failed.' }
& $taskCompiler /nologo /warn:4 /warnaserror+ /optimize+ /target:exe /platform:anycpu "/reference:$taskLibrary" "/out:$taskRunner" (Join-Path $taskProjectRoot 'tests\NativeTests.cs')
if ($LASTEXITCODE -ne 0) { throw 'Test compilation failed.' }
& $taskRunner
if ($LASTEXITCODE -ne 0) { throw 'Native verification failed.' }
Write-Output 'Compiled and tested the standalone core. This did not install a Wukong mod.'
