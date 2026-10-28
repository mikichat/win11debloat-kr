<#
    Win11DebloatKR.exe 빌드 스크립트

    1) win11debloat 실행 파일 묶음(payload.zip) 생성
    2) exe 아이콘(.ico) 생성
    3) Roslyn csc 로 단일 exe 컴파일 (payload/아이콘은 리소스로 내장)

    사용법: pwsh -File .\build.ps1
#>
[CmdletBinding()]
param(
    [string]$SourceDir = (Join-Path $PSScriptRoot 'win11debloat'),
    [string]$SrcDir = (Join-Path $PSScriptRoot 'src'),
    [string]$OutputDir = (Join-Path $PSScriptRoot 'build')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$utf8 = New-Object System.Text.UTF8Encoding($false)

function Write-Step([string]$Message) {
    Write-Host ''
    Write-Host "==> $Message" -ForegroundColor Cyan
}

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
    $found = Get-ChildItem 'C:\Program Files\Microsoft Visual Studio', 'C:\Program Files (x86)\Microsoft Visual Studio' `
        -Recurse -Filter 'csc.exe' -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match 'Roslyn' } |
        Select-Object -First 1
    if ($found) { return $found.FullName }
    throw 'C# 컴파일러(csc.exe)를 찾을 수 없습니다.'
}

function Resolve-ReferenceDir {
    $base = 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework'
    if (-not (Test-Path -LiteralPath $base)) { throw '.NET Framework 참조 어셈블리를 찾을 수 없습니다.' }
    $versions = @('v4.8', 'v4.7.2', 'v4.6.2', 'v4.6.1', 'v4.6')
    foreach ($version in $versions) {
        $dir = Join-Path $base $version
        if ((Test-Path -LiteralPath (Join-Path $dir 'mscorlib.dll')) -and
            (Test-Path -LiteralPath (Join-Path $dir 'System.Windows.Forms.dll'))) {
            return $dir
        }
    }
    throw '사용 가능한 .NET Framework 참조 어셈블리 폴더를 찾을 수 없습니다.'
}

function New-AppIcon([string]$Path) {
    Add-Type -AssemblyName System.Drawing

    $sizes = @(16, 24, 32, 48, 64, 256)
    $images = New-Object System.Collections.Generic.List[byte[]]

    foreach ($size in $sizes) {
        $bitmap = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        try {
            $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $graphics.Clear([System.Drawing.Color]::Transparent)

            # 둥근 사각형 배경
            $radius = [Math]::Max(3, [int]($size * 0.18))
            $inset = [Math]::Max(1, [int]($size * 0.04))
            $rect = New-Object System.Drawing.Rectangle($inset, $inset, ($size - 2 * $inset), ($size - 2 * $inset))
            $gPath = New-Object System.Drawing.Drawing2D.GraphicsPath
            $d = $radius * 2
            $gPath.AddArc($rect.X, $rect.Y, $d, $d, 180, 90)
            $gPath.AddArc($rect.Right - $d, $rect.Y, $d, $d, 270, 90)
            $gPath.AddArc($rect.Right - $d, $rect.Bottom - $d, $d, $d, 0, 90)
            $gPath.AddArc($rect.X, $rect.Bottom - $d, $d, $d, 90, 90)
            $gPath.CloseFigure()

            $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
                $rect,
                [System.Drawing.Color]::FromArgb(45, 125, 210),
                [System.Drawing.Color]::FromArgb(21, 82, 145),
                55.0)
            $graphics.FillPath($brush, $gPath)
            $borderPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 255, 255), [Math]::Max(1, $size / 32.0))
            $graphics.DrawPath($borderPen, $gPath)

            # 체크 마크
            $stroke = [Math]::Max(1.6, $size * 0.11)
            $pen2 = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $stroke)
            $pen2.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen2.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
            $pen2.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
            $points = @(
                (New-Object System.Drawing.PointF(($size * 0.28), ($size * 0.52))),
                (New-Object System.Drawing.PointF(($size * 0.44), ($size * 0.68))),
                (New-Object System.Drawing.PointF(($size * 0.74), ($size * 0.33)))
            )
            $graphics.DrawLines($pen2, [System.Drawing.PointF[]]$points)

            $ms = New-Object System.IO.MemoryStream
            $bitmap.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
            $images.Add($ms.ToArray())
            $ms.Dispose()
        }
        finally {
            $graphics.Dispose()
            $bitmap.Dispose()
        }
    }

    # ICO 파일 직접 작성 (각 항목은 PNG 압축)
    $count = $images.Count
    $stream = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($stream)
    try {
        $writer.Write([UInt16]0)   # reserved
        $writer.Write([UInt16]1)   # type = icon
        $writer.Write([UInt16]$count)

        $offset = 6 + (16 * $count)
        for ($i = 0; $i -lt $count; $i++) {
            $size = $sizes[$i]
            $byteSize = if ($size -ge 256) { 0 } else { $size }
            $writer.Write([Byte]$byteSize)
            $writer.Write([Byte]$byteSize)
            $writer.Write([Byte]0)
            $writer.Write([Byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$images[$i].Length)
            $writer.Write([UInt32]$offset)
            $offset += $images[$i].Length
        }
        foreach ($image in $images) {
            $writer.Write($image)
        }
    }
    finally {
        $writer.Dispose()
        $stream.Dispose()
    }
}

# ----------------------------------------------------------------------
Write-Step '경로 확인'
if (-not (Test-Path -LiteralPath $SrcDir)) {
    throw "C# 소스 폴더를 찾을 수 없습니다: $SrcDir"
}
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

# upstream 원본은 저장소에 포함하지 않고, 빌드 시 자동으로 받아 쓴다.
if (-not (Test-Path -LiteralPath (Join-Path $SourceDir 'Win11Debloat.ps1'))) {
    Write-Host "  upstream 원본이 없어 클론합니다: $SourceDir"
    if (-not (Get-Command git -ErrorAction SilentlyContinue)) {
        throw ("git 이 필요합니다. https://github.com/raphire/win11debloat 를 '{0}' 에 클론해 주세요." -f $SourceDir)
    }
    & git clone --depth 1 https://github.com/raphire/win11debloat.git $SourceDir
    if ($LASTEXITCODE -ne 0) {
        throw "upstream 클론에 실패했습니다 (종료 코드 $LASTEXITCODE)."
    }
}

# ko-KR 카탈로그 보정. 이미 적용돼 있으면 건너뛰므로 반복 실행해도 안전하다.
Write-Host '  ko-KR 카탈로그 보정 확인 중'
& (Join-Path $PSScriptRoot 'tools\patch-ko-kr.ps1') | Out-Null

$csc = Resolve-CscPath
$refDir = Resolve-ReferenceDir
Write-Host "  컴파일러 : $csc"
Write-Host "  참조     : $refDir"

# ----------------------------------------------------------------------
Write-Step 'payload 묶음 생성'
$stageDir = Join-Path $OutputDir 'payload-stage'
if (Test-Path -LiteralPath $stageDir) { Remove-Item -LiteralPath $stageDir -Recurse -Force }
New-Item -ItemType Directory -Path $stageDir -Force | Out-Null

$includeDirs = @('Config', 'Scripts', 'Regfiles', 'Assets', 'Schemas')
foreach ($dir in $includeDirs) {
    $source = Join-Path $SourceDir $dir
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $stageDir -Recurse -Force
    }
}
foreach ($file in @('Win11Debloat.ps1', 'Run.bat', 'LICENSE', 'README.md')) {
    $source = Join-Path $SourceDir $file
    if (Test-Path -LiteralPath $source) {
        Copy-Item -LiteralPath $source -Destination $stageDir -Force
    }
}

# ----------------------------------------------------------------------
# ko-KR 병합
#
# upstream 은 콘솔 진행 메시지와 대기 목록을 Config/Features.json(영어 기본값)에서
# 읽기 때문에 언어를 지정해도 영어로 출력된다. payload 에 담길 기본 Features.json 의
# 표시 전용 필드를 ko-KR 로 덮어써서 콘솔 출력까지 한글이 되도록 한다.
# ----------------------------------------------------------------------
Write-Step 'ko-KR 언어 병합'
$stagedFeatures = Join-Path $stageDir 'Config\Features.json'
$koFeatures = Join-Path $SourceDir 'Config\Languages\ko-KR\Features.json'
if ((Test-Path -LiteralPath $stagedFeatures) -and (Test-Path -LiteralPath $koFeatures)) {
    & (Join-Path $PSScriptRoot 'tools\merge-ko-kr-features.ps1') `
        -BaseFeaturesPath $stagedFeatures `
        -LanguageFeaturesPath $koFeatures
}
else {
    Write-Host '  (ko-KR 카탈로그가 없어 병합을 건너뜁니다)' -ForegroundColor DarkGray
}

$payloadZip = Join-Path $OutputDir 'payload.zip'
if (Test-Path -LiteralPath $payloadZip) { Remove-Item -LiteralPath $payloadZip -Force }
Add-Type -AssemblyName System.IO.Compression.FileSystem
[System.IO.Compression.ZipFile]::CreateFromDirectory($stageDir, $payloadZip, [System.IO.Compression.CompressionLevel]::Optimal, $false)
Remove-Item -LiteralPath $stageDir -Recurse -Force
Write-Host ("  payload.zip : {0:N2} MB" -f ((Get-Item $payloadZip).Length / 1MB))

# ----------------------------------------------------------------------
Write-Step '아이콘 생성'
$iconPath = Join-Path $OutputDir 'appicon.ico'
New-AppIcon -Path $iconPath
Write-Host ("  appicon.ico : {0:N0} bytes" -f (Get-Item $iconPath).Length)

# ----------------------------------------------------------------------
Write-Step '컴파일'
$exePath = Join-Path $OutputDir 'Win11DebloatKR.exe'
if (Test-Path -LiteralPath $exePath) {
    # 실행 중인 프로그램이 파일을 잡고 있으면 잠시 뒤 다시 시도한다.
    $removed = $false
    for ($attempt = 1; $attempt -le 10; $attempt++) {
        try {
            Remove-Item -LiteralPath $exePath -Force -ErrorAction Stop
            $removed = $true
            break
        }
        catch {
            if ($attempt -eq 10) { throw }
            Write-Host ("  실행 중인 파일이라 {0}초 후 재시도합니다 ({1}/10)" -f 2, $attempt) -ForegroundColor DarkGray
            Start-Sleep -Seconds 2
        }
    }
    if (-not $removed) { throw "이전 실행 파일을 삭제하지 못했습니다: $exePath" }
}

$references = @(
    'mscorlib.dll', 'System.dll', 'System.Core.dll', 'System.Data.dll', 'System.Xml.dll',
    'System.Drawing.dll', 'System.Windows.Forms.dll', 'System.Web.Extensions.dll',
    'System.IO.Compression.dll', 'System.IO.Compression.FileSystem.dll'
)
$refArgs = @()
foreach ($name in $references) {
    $path = Join-Path $refDir $name
    if (Test-Path -LiteralPath $path) {
        $refArgs += "/reference:`"$path`""
    }
    else {
        Write-Host "  (참조 생략: $name)"
    }
}

$sources = Get-ChildItem -LiteralPath $SrcDir -Filter '*.cs' | ForEach-Object { "`"$($_.FullName)`"" }

# csc 는 인자 문자열을 직접 파싱하므로 하나의 문자열로 만들어 그대로 전달한다.
$argParts = @(
    '/target:winexe',
    '/platform:anycpu',
    '/optimize+',
    '/debug:portable',
    '/utf8output',
    '/codepage:65001',
    '/warn:4',
    '/nostdlib+',
    '/noconfig',
    "/out:`"$exePath`"",
    "/win32icon:`"$iconPath`"",
    "/resource:`"$payloadZip`",Win11DebloatKR.Payload.zip",
    "/resource:`"$iconPath`",Win11DebloatKR.AppIcon.ico"
) + $refArgs + $sources
$argString = ($argParts -join ' ')

$process = Start-Process -FilePath $csc -ArgumentList $argString -NoNewWindow -Wait -PassThru
if ($process.ExitCode -ne 0) {
    throw "컴파일에 실패했습니다 (종료 코드 $($process.ExitCode))."
}

Write-Host ''
Write-Host ("  결과 : {0} ({1:N2} MB)" -f $exePath, ((Get-Item $exePath).Length / 1MB))
Write-Host ''
Write-Host '빌드가 완료되었습니다.' -ForegroundColor Green
