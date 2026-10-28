# AGENTS.md

win11debloat(upstream: https://github.com/raphire/win11debloat)을 구동하는 **한국어 GUI 단일 실행 파일**.
C# WinForms 래퍼(exe) + PowerShell 7 빌드 스크립트로 구성되며, 실제 적용 로직은 upstream CLI 엔진에 위임한다.

## Setup commands

- 의존성 설치: 없음(NuGet 미사용). Roslyn `csc.exe` 와 .NET Framework 4.7.2+ 참조 어셈블리만 필요
- upstream 원본: 없으면 `build.ps1` 이 `win11debloat/` 에 자동 클론한다 (git 미추적)
- 빌드:      `pwsh -File .\build.ps1` → `build\Win11DebloatKR.exe` (단일 파일, payload 내장)
- 테스트:    `pwsh -File .\tools\test-translator.ps1` (번역 규칙 45케이스)
- 실행 확인: `.\build\Win11DebloatKR.exe --preview` (권한 상승 없음) / `--selftest` (WhatIf 자체진단)

## Project layout

- `src/` — C# GUI 소스 (WinForms, .NET Framework 4.7.2, `namespace Win11DebloatKR`)
- `tools/` — 빌드·검증 보조 스크립트와 단위 테스트
- `build/` — 빌드 산출물. `Win11DebloatKR.exe` 만 git 추적이고 `payload.zip`·`appicon.ico` 는 재생성
- `win11debloat/` — upstream 원본 clone. `.gitignore` 대상이며 빌드 시 자동 확보

## Code style

- C# 4칸 들여쓰기, Allman 중괄호, 네임스페이스는 `Win11DebloatKR` 고정
- private 필드 `_camelCase`, 공개 멤버 `PascalCase`, const `PascalCase`
- 주석과 XML doc 은 한국어. **사용자에게 노출되는 문자열(메뉴·버튼·툴팁·상태 표시)은 전부 한국어**
- 문자열 리소스 파일을 만들지 않는다 — exe 단일 파일 유지를 위해 코드에 직접 작성
- PowerShell 스크립트는 `$ErrorActionPreference = 'Stop'` + `Set-StrictMode -Version Latest`
- 인코딩은 UTF-8. `Config/Languages/ko-KR/*.json` 은 **BOM 유지 필수**
  (Windows PowerShell 5.1 의 `Get-Content -Raw` 는 BOM 이 없으면 시스템 ANSI 로 읽어 JSON 파싱이 실패한다)

## Testing instructions

- 단위 테스트: `pwsh -File .\tools\test-translator.ps1` — 번역 규칙 검증.
  새 규칙을 추가할 때는 `tools\TranslatorTest.cs` 에 케이스도 함께 추가한다
- 통합 확인: `pwsh -File .\tools\capture.ps1 -Output <png> -TabIndex 0..3` 로 탭별 스크린샷,
  `Win11DebloatKR.exe --selftest` 로 WhatIf 자체진단
- GUI 확인만 할 때는 `--preview` 를 쓴다 (관리자 권한 상승 없음)
- **테스트 중 시스템 변경 금지** — 레지스트리/앱 제거는 `-WhatIf` 로도 부수 효과가 남을 수 있다

## 도메인 규칙 (반드시 지킬 것)

- **사용자 동의 없이 시스템 변경 금지.** 레지스트리 쓰기·앱 제거·예약 작업 삭제는 실행하지 않는다.
  검증은 `--selftest`(`-WhatIf`) 로만 한다
- **링크 키는 손대지 않는다.** `Config/Features.json` 의 `Category`, `UiGroups[].GroupId`,
  `UiGroups[].Values[].Label` 은 upstream 의 매칭 키이자 `(Default)` 판정 근거다.
  ko-KR 병합은 `Label`/`ApplyText`/`UndoLabel`/`ApplyUndoText`/`ToolTip` 만 바꾼다
- **PowerShell 5.1 만 사용** — upstream 은 PowerShell 7 을 지원하지 않는다
- **`-EncodedCommand` 금지** — 콘솔 없는 GUI(winexe) 프로세스에서 `Win32Exception(native=5)` 로 실패한다.
  upstream `Run.bat` 과 같은 `-Command` 방식으로 전달한다
- 자식 프로세스에는 `PSModulePath` 앞에 Windows PowerShell 5.1 Modules 경로를 붙여 준다
  (PS7 에서 실행된 프로그램은 PS7 경로로 덮여써져 Appx/Security 모듈 자동 로드가 안 된다)
- 그룹(라디오) 기본값은 "변경 없음" — 기본 상태에서는 해당 스위치를 CLI 에 넘기지 않는다
- 분류 무관 8개 기능(`RemoveApps`, `Apps`, `RemoveGamingApps`, `RemoveHPApps`, `CreateRestorePoint`,
  `ReplaceStart`, `ReplaceStartAllUsers`, `ForceRemoveEdge`)은 기능 탭이 아니라 앱 제거/기본 탭에 둔다
- 스크립트 하드코딩 영문 문장은 `src\ConsoleTranslator.cs` 규칙으로 번역한다.
  규칙에 안 맞는 줄은 원문 그대로 남겨 오류 내용을 숨기지 않는다

## PR & commit conventions

- `main` 에서 브랜치를 파고 `main` 에 직접 push 하지 않는다
- 커밋 메시지: `feat:` / `fix:` / `docs:` / `refactor:` 접두어 + 한국어 요약
- PR 은 `gh pr create` 로 연다

## Security

- 토큰·키·`.env` 는 커밋하지 않는다 (`.gitignore` 에 `*.log` 포함)
- 이 프로그램은 관리자 권한으로 실행되어 시스템 설정을 바꾼다 — 테스트 시 실제 적용 금지
- upstream 은 MIT 라이선스 — 저장소 루트 `LICENSE` 에 upstream(Raphire) 과 본 래퍼(mikichat) copyrights 가 함께 명시돼 있다.
  `win11debloat/` 은 git 미추적이므로 `win11debloat\LICENSE` 를 가리키면 안 된다
