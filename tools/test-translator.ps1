<#
    ConsoleTranslator 규칙 단위 테스트 실행 스크립트.

    src\ConsoleTranslator.cs 와 tools\TranslatorTest.cs 를 임시 exe 로 컴파일한 뒤
    실행해 번역 규칙 45개 케이스를 검증한다. 빌드 산출물(build\)은 건드리지 않는다.

    사용법:
      pwsh -File .\tools\test-translator.ps1
#>
[CmdletBinding()]
param(
    [string]$RootDir = (Split-Path $PSScriptRoot -Parent)
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

function Resolve-CscPath {
    $candidates = @(
        'C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Program Files (x86)\Microsoft Visual Studio\2019\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe',
        'C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    )
    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) { return $candidate }
    }
    throw 'C# 컴파일러(csc.exe)를 찾을 수 없습니다.'
}

function Resolve-ReferenceDir {
    $base = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
    if (-not (Test-Path -LiteralPath $base)) { throw '.NET Framework 참조 어셈블리를 찾을 수 없습니다.' }
    foreach ($version in @('v4.8', 'v4.7.2', 'v4.6.2', 'v4.6.1', 'v4.6')) {
        $dir = Join-Path $base $version
        if ((Test-Path -LiteralPath (Join-Path $dir 'mscorlib.dll')) -and
            (Test-Path -LiteralPath (Join-Path $dir 'System.dll'))) {
            return $dir
        }
    }
    throw '사용 가능한 .NET Framework 참조 어셈블리 폴더를 찾을 수 없습니다.'
}

$translatorSource = Join-Path $RootDir 'src\ConsoleTranslator.cs'
$testSource = Join-Path $RootDir 'tools\TranslatorTest.cs'
foreach ($path in @($translatorSource, $testSource)) {
    if (-not (Test-Path -LiteralPath $path)) { throw "소스를 찾을 수 없습니다: $path" }
}

$csc = Resolve-CscPath
$refDir = Resolve-ReferenceDir
$outExe = Join-Path ([System.IO.Path]::GetTempPath()) 'win11debloatkr-translator-test.exe'

Write-Host "컴파일러 : $csc" -ForegroundColor DarkGray
Write-Host "참조     : $refDir" -ForegroundColor DarkGray

$argParts = @(
    '/target:exe',
    '/platform:anycpu',
    '/utf8output',
    '/codepage:65001',
    '/nostdlib+',
    '/noconfig',
    "/out:`"$outExe`"",
    "/reference:`"$(Join-Path $refDir 'mscorlib.dll')`"",
    "/reference:`"$(Join-Path $refDir 'System.dll')`"",
    "/reference:`"$(Join-Path $refDir 'System.Core.dll')`"",
    "`"$translatorSource`"",
    "`"$testSource`""
)

$process = Start-Process -FilePath $csc -ArgumentList ($argParts -join ' ') -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "테스트 하네스 컴파일에 실패했습니다 (종료 코드 $($process.ExitCode))."
}

& $outExe
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host ''
    Write-Host '번역 규칙 테스트 통과' -ForegroundColor Green
}
else {
    Write-Host ''
    Write-Host "번역 규칙 테스트 실패 (종료 코드 $exitCode)" -ForegroundColor Red
}

exit $exitCode
