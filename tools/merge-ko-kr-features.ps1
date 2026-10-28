<#
    upstream 기본 Config/Features.json 에 ko-KR 번역을 병합한다.

    upstream 은 $script:Features 를 Config/Features.json(영어 기본값)에서만 읽고,
    언어 파일은 Get-Translation 을 통해서만 조회한다. 그 결과 콘솔 진행 메시지
    ("> $applyText...") 와 대기 목록("- $feature.Label") 은 영어로 출력된다.

    이 스크립트는 표시 전용 필드만 한국어로 덮어써서, 콘솔 출력까지 한글이 되도록
    만든다. 분류(Category) / 그룹(GroupId) / 옵션 라벨은 upstream 이 linkage 키로
    사용하므로 건드리지 않는다.

    병합 대상 필드 : Label, ApplyText, UndoLabel, ApplyUndoText, ToolTip

    사용법:
      pwsh -File .\tools\merge-ko-kr-features.ps1 `
           -BaseFeaturesPath <수정할 Features.json> `
           -LanguageFeaturesPath <ko-KR\Features.json>
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$BaseFeaturesPath,

    [Parameter(Mandatory = $true)]
    [string]$LanguageFeaturesPath
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# 표시 전용 필드. linkage 키(Category, FeatureId, GroupId 등)는 포함하지 않는다.
$displayFields = @('Label', 'ApplyText', 'UndoLabel', 'ApplyUndoText', 'ToolTip')

if (-not (Test-Path -LiteralPath $BaseFeaturesPath)) {
    throw "기본 Features.json 을 찾을 수 없습니다: $BaseFeaturesPath"
}
if (-not (Test-Path -LiteralPath $LanguageFeaturesPath)) {
    throw "언어 Features.json 을 찾을 수 없습니다: $LanguageFeaturesPath"
}

$base = Get-Content -LiteralPath $BaseFeaturesPath -Raw -Encoding UTF8 | ConvertFrom-Json
$lang = Get-Content -LiteralPath $LanguageFeaturesPath -Raw -Encoding UTF8 | ConvertFrom-Json

$koFeatures = $lang.Features
if (-not $koFeatures) {
    throw "언어 Features.json 에 Features 섹션이 없습니다: $LanguageFeaturesPath"
}

$mergedFields = 0
$mergedFeatures = 0
$missing = New-Object System.Collections.Generic.List[string]

foreach ($feature in @($base.Features)) {
    if (-not $feature) { continue }
    $id = [string]$feature.FeatureId
    if ([string]::IsNullOrWhiteSpace($id)) { continue }

    $entry = $koFeatures.PSObject.Properties[$id]
    if (-not $entry) {
        $missing.Add($id)
        continue
    }
    $koEntry = $entry.Value
    if (-not $koEntry) { continue }

    $touched = $false
    foreach ($field in $displayFields) {
        $source = $koEntry.PSObject.Properties[$field]
        if (-not $source) { continue }

        $value = [string]$source.Value
        if ([string]::IsNullOrWhiteSpace($value)) { continue }

        # 기본 파일에 이미 있는 필드만 덮어써서 JSON 구조를 바꾸지 않는다.
        $target = $feature.PSObject.Properties[$field]
        if (-not $target) { continue }

        if ([string]$target.Value -ne $value) {
            $feature.$field = $value
            $mergedFields++
            $touched = $true
        }
    }
    if ($touched) { $mergedFeatures++ }
}

$json = $base | ConvertTo-Json -Depth 12
# 원본과 동일하게 CRLF 로 저장한다.
$json = $json -replace "`r?`n", "`r`n"
# BOM 을 반드시 넣는다.
# upstream 은 Config/Features.json 을 'Get-Content -Raw' (인코딩 지정 없음) 로 읽는데,
# Windows PowerShell 5.1 은 BOM 이 없으면 시스템 ANSI 코드페이지로 해석한다.
# 한글 문자열이 섞이면 JSON 파싱이 실패하므로, upstream 의 ko-KR 언어 파일과 같은
# 규칙(UTF-8 BOM)으로 저장한다.
[System.IO.File]::WriteAllText(
    $BaseFeaturesPath,
    $json,
    (New-Object System.Text.UTF8Encoding($true)))

Write-Host ("  ko-KR 병합 : 기능 {0}건, 필드 {1}건" -f $mergedFeatures, $mergedFields)
if ($missing.Count -gt 0) {
    Write-Host ("  번역 없음  : {0}건 ({1})" -f $missing.Count, (($missing | Select-Object -First 5) -join ', ')) -ForegroundColor DarkGray
}
