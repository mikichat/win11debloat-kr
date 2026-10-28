<#
    ko-KR 언어 카탈로그 보정 스크립트

    upstream 의 ko-KR/Apps.json 에 남아 있는 영어 표시명을
    공식 한국어 제품명으로 바꾸고, 실제 영어 원문과 어긋난(stale) 값을 수정한다.

    사용법: pwsh -File .\tools\patch-ko-kr.ps1
#>
[CmdletBinding()]
param(
    [string]$ConfigRoot = (Join-Path (Split-Path $PSScriptRoot -Parent) 'win11debloat\Config')
)

$ErrorActionPreference = 'Stop'

$koApps = Join-Path $ConfigRoot 'Languages\ko-KR\Apps.json'

# '영문 원문' -> '한국어 표기'
$replacements = [ordered]@{
    # en-US 원문과 어긋난 stale 값 수정 (en-US: "Game Speech Window")
    '"Xbox Speech To Text"'      = '"게임 음성 창"'
    # Microsoft 제품 공식 한국어 표기
    '"Microsoft Store"'          = '"Microsoft 스토어"'
    '"Game Bar"'                 = '"게임 바"'
    '"Xbox Console Companion"'   = '"Xbox 콘솔 컴패니언"'
    '"Windows App"'              = '"Windows 앱"'
    '"Solitaire & Casual Games"' = '"솔리테어 및 캐주얼 게임"'
    '"Cortana"'                  = '"코르타나"'
    '"3D Builder"'               = '"3D 빌더"'
    '"Network Speed Test"'       = '"네트워크 속도 테스트"'
    '"OneNote for Windows 10"'   = '"Windows 10용 OneNote"'
    '"One Calendar"'             = '"One 일정"'
    '"Xbox Game Overlay"'        = '"Xbox 게임 오버레이"'
    '"Xbox Identity Provider"'   = '"Xbox ID 공급자"'
}

if (-not (Test-Path -LiteralPath $koApps)) {
    throw "ko-KR/Apps.json 을 찾을 수 없습니다: $koApps"
}

# BOM 을 유지하기 위해 바이트 단위로 읽고 쓴다.
$bytes = [System.IO.File]::ReadAllBytes($koApps)
$hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
$text = [System.Text.Encoding]::UTF8.GetString($bytes, $(if ($hasBom) { 3 } else { 0 }), $bytes.Length - $(if ($hasBom) { 3 } else { 0 }))

$changed = 0
foreach ($key in $replacements.Keys) {
    $count = ([regex]::Matches($text, [regex]::Escape($key))).Count
    if ($count -ne 1) {
        Write-Host "  건너뜀 ($count 건): $key" -ForegroundColor Yellow
        continue
    }
    $text = $text.Replace($key, $replacements[$key])
    $changed++
    Write-Host "  수정: $key -> $($replacements[$key])"
}

$outBytes = [System.Text.Encoding]::UTF8.GetBytes($text)
if ($hasBom) {
    $outBytes = [byte[]](0xEF, 0xBB, 0xBF) + $outBytes
}
[System.IO.File]::WriteAllBytes($koApps, $outBytes)

Write-Host ''
Write-Host "ko-KR/Apps.json : $changed 개 항목 수정" -ForegroundColor Green
