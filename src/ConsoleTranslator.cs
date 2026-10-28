using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Win11DebloatKR
{
    /// <summary>
    /// upstream 스크립트가 콘솔(표준 출력/오류)로 출력하는 하드코딩 영어 메시지를
    /// 한국어로 바꾼다.
    ///
    /// upstream 의 ko-KR 언어 카탈로그는 Features / Apps / Categories / Chrome 항목만
    /// 번역한다. 스크립트 내부에 직접 적힌 문장(배너, 경고, 진행 메시지 등)은
    /// 언어 파일과 무관하게 영문이므로, 이 클래스에서 별도로 번역한다.
    ///
    /// 규칙은 정규식 한 줄 단위이며, 규칙에 맞지 않는 줄은 원문 그대로 둔다.
    /// 그래서 번역이 불가능하거나 문장이 바뀐 경우에도 오류 내용이 사라지지 않는다.
    /// </summary>
    internal static class ConsoleTranslator
    {
        private sealed class Rule
        {
            public Regex Pattern;
            public string Replacement;      // Evaluator 가 있으면 사용하지 않는다.
            public MatchEvaluator Evaluator;
        }

        private static readonly Rule[] Rules = BuildRules();

        /// <summary>PowerShell 이 경고/오류 메시지 앞에 붙이는 접두사.</summary>
        private static readonly string[] MessagePrefixes =
        {
            "경고: ", "오류: ", "VERBOSE: ", "WARNING: ", "ERROR: "
        };

        /// <summary>영문 원문을 그대로 보여줄지 여부.</summary>
        internal static bool ShowOriginalText;

        /// <summary>한 줄을 한국어로 번역한다. 규칙이 없으면 원문을 그대로 반환한다.</summary>
        internal static string Translate(string line)
        {
            if (string.IsNullOrEmpty(line)) return line;
            if (ShowOriginalText) return line;

            // PowerShell 의 경고/오류 접두사를 떼어 내고, 본문만 번역한 뒤 되돌린다.
            string prefix = null;
            foreach (string candidate in MessagePrefixes)
            {
                if (line.StartsWith(candidate, StringComparison.Ordinal))
                {
                    prefix = candidate;
                    line = line.Substring(candidate.Length);
                    break;
                }
            }

            string translated = line;
            for (int i = 0; i < Rules.Length; i++)
            {
                if (!Rules[i].Pattern.IsMatch(line)) continue;

                translated = Rules[i].Evaluator != null
                    ? Rules[i].Pattern.Replace(line, Rules[i].Evaluator)
                    : Rules[i].Pattern.Replace(line, Rules[i].Replacement);
                break;
            }

            return prefix == null ? translated : prefix + translated;
        }

        // ------------------------------------------------------------------
        // 규칙 정의
        // ------------------------------------------------------------------

        private static Rule[] BuildRules()
        {
            List<Rule> rules = new List<Rule>();

            // ---- 시작 배너 / 헤더 ----
            Add(rules, @"^(\s*)Win11Debloat is launching\.\.\.$", "$1Win11Debloat 실행 중...");
            Add(rules, @"^(\s*)Keep this window open$", "$1이 창을 닫지 마세요");
            AddEval(rules, @"^(\s*)Win11Debloat Script - (.*?)(\s*\(User: (.+?)\)|\s*\(Sysprep mode\))?$", HeaderEvaluator);

            // ---- 레지스트리 적용 ----
            Add(rules, @"^\[WhatIf\] Apply (\d+) registry changes from '(.+?)'$", "[WhatIf] '$2' 파일에서 레지스트리 변경 $1건 적용");
            Add(rules, @"^The operation completed successfully via PowerShell registry writer\.$", "PowerShell 레지스트리 기록기를 통해 작업이 성공적으로 완료되었습니다.");
            Add(rules, @"^Backup successfully created: (.+)$", "백업을 성공적으로 만들었습니다: $1");
            Add(rules, @"^Skipping operation '(.+?)' on key '(.+?)' value '(.+?)' due to access restrictions: (.+)$", "'$2' 의 '$3' 값에 대한 '$1' 작업을 액세스 제한 때문에 건너뜁니다: $4");
            Add(rules, @"^Skipping operation '(.+?)' on key '(.+?)' due to access restrictions: (.+)$", "'$2' 에 대한 '$1' 작업을 액세스 제한 때문에 건너뜁니다: $3");
            Add(rules, @"^Registry fallback import completed with (\d+) access-restricted operation\(s\) skipped in '(.+?)'\.$", "'$2' 에서 액세스 제한으로 건너뛴 작업 $1 건과 함께 레지스트리 대체 가져오기를 완료했습니다.");
            Add(rules, @"^Skipping unsupported or malformed registry value '(.+?)' in '(.+?)'\.$", "'$2' 의 지원되지 않거나 형식이 잘못된 레지스트리 값 '$1' 을(를) 건너뜁니다.");

            // ---- 기능 적용 / 되돌리기 ----
            Add(rules, @"^Unknown feature '(.+?)' could not be applied\.$", "알 수 없는 기능 '$1' 을(를) 적용하지 못했습니다.");
            Add(rules, @"^Unknown feature '(.+?)' could not be undone\.$", "알 수 없는 기능 '$1' 을(를) 되돌리지 못했습니다.");
            Add(rules, @"^Failed to apply '(.+?)': (.+)$", "'$1' 적용에 실패했습니다: $2");
            Add(rules, @"^Failed to undo '(.+?)': (.+)$", "'$1' 되돌리기에 실패했습니다: $2");
            Add(rules, @"^Unable to apply '(.+?)': (.+)$", "'$1' 을(를) 적용할 수 없습니다: $2");
            AddEval(rules, @"^Skipping (.+?): it is only supported on Windows build (.+?), this system runs build (\d+)\.$", SkippingBuildEvaluator);
            Add(rules, @"^No valid apps were selected for removal$", "제거할 앱이 선택되지 않았습니다");
            Add(rules, @"^Unable to verify if all apps were uninstalled successfully\.$", "모든 앱이 정상적으로 제거되었는지 확인할 수 없습니다.");

            // ---- 탐색기 재시작 ----
            Add(rules, @"^\[WhatIf\] Restart the Windows Explorer process$", "[WhatIf] Windows 탐색기 프로세스 다시 시작");
            Add(rules, @"^> Attempting to restart the Windows Explorer process to apply all changes\.\.\.$", "> 변경 사항을 적용하기 위해 Windows 탐색기 프로세스를 다시 시작하는 중...");
            Add(rules, @"^Restarting the Windows Explorer process\.\.\. \(This may cause your screen to flicker\)$", "Windows 탐색기 프로세스를 다시 시작하는 중... (화면이 잠시 깜빡일 수 있습니다)");
            Add(rules, @"^Explorer process restart was skipped, please manually reboot your PC to apply all changes$", "탐색기 프로세스 재시작을 건너뛰었습니다. 변경 사항을 적용하려면 PC를 다시 시작해 주세요.");
            Add(rules, @"^Unable to restart Windows Explorer process, please manually reboot your PC to apply all changes$", "Windows 탐색기 프로세스를 다시 시작하지 못했습니다. 변경 사항을 적용하려면 PC를 다시 시작해 주세요.");

            // ---- 백업 / 복원 지점 ----
            Add(rules, @"^\[WhatIf\] Create registry backup$", "[WhatIf] 레지스트리 백업 생성");
            Add(rules, @"^> Creating registry backup\.\.\.$", "> 레지스트리 백업 생성 중...");
            Add(rules, @"^\[WhatIf\] Create system restore point$", "[WhatIf] 시스템 복원 지점 생성");
            Add(rules, @"^> Creating a system restore point\.\.\.$", "> 시스템 복원 지점 생성 중...");
            Add(rules, @"^Failed to create a system restore point\. Do you want to continue without a restore point\? \(y/n\)$", "시스템 복원 지점을 만들지 못했습니다. 복원 지점 없이 계속하시겠습니까? (y/n)");

            // ---- 시작 메뉴 ----
            Add(rules, @"^\[WhatIf\] Replace Start Menu for Default user profile with template (.+)$", "[WhatIf] 기본 사용자 프로필의 시작 메뉴를 템플릿 $1 로 교체");
            Add(rules, @"^\[WhatIf\] Replace Start Menu for user (.+?) with template (.+)$", "[WhatIf] 사용자 $1 의 시작 메뉴를 템플릿 $2 로 교체");
            Add(rules, @"^\[WhatIf\] Restore start menu for user (.+?) from backup (.+)$", "[WhatIf] 백업 $2 에서 사용자 $1 의 시작 메뉴 복원");
            Add(rules, @"^\[WhatIf\] Remove start2\.bin for the default user profile$", "[WhatIf] 기본 사용자 프로필의 start2.bin 제거");
            Add(rules, @"^> Removing all pinned apps from the start menu for all users\.\.\.$", "> 모든 사용자의 시작 메뉴에서 고정된 앱을 제거하는 중...");
            Add(rules, @"^Replaced start menu for the default user profile$", "기본 사용자 프로필의 시작 메뉴를 교체했습니다");
            Add(rules, @"^Replaced start menu for user (.+)$", "사용자 $1 의 시작 메뉴를 교체했습니다");
            Add(rules, @"^Restoring start menu for user (.+?) from backup\.\.\.$", "백업에서 사용자 $1 의 시작 메뉴를 복원하는 중...");
            Add(rules, @"^Restoring start menu for all users from backup\.\.\.$", "백업에서 모든 사용자의 시작 메뉴를 복원하는 중...");
            Add(rules, @"^Failed to create the Default profile Start Menu directory: (.+)$", "기본 프로필의 시작 메뉴 폴더를 만들지 못했습니다: $1");
            Add(rules, @"^Unable to find original start2\.bin file for user (.+?), no backup was created for this user$", "사용자 $1 의 원본 start2.bin 파일을 찾지 못해 이 사용자의 백업을 만들지 않았습니다");
            Add(rules, @"^Failed to replace Start Menu for user (.+?): (.+)$", "사용자 $1 의 시작 메뉴를 교체하지 못했습니다: $2");
            Add(rules, @"^Failed to replace Start Menu for user (.+?)$", "사용자 $1 의 시작 메뉴를 교체하지 못했습니다");

            // ---- 레지스트리 백업 복원 ----
            Add(rules, @"^\[WhatIf\] Restore registry backup for (.+)$", "[WhatIf] $1 의 레지스트리 백업 복원");
            Add(rules, @"^Registry restore for (.+?) requires confirmation of (\d+) unrecognized executable setting\(s\)\.$", "$1 의 레지스트리 복원에 확인이 필요한 실행 파일 설정 $2 건이 있습니다.");
            Add(rules, @"^Unrecognized executable registry setting: (.+)$", "인식되지 않은 실행 파일 레지스트리 설정: $1");
            Add(rules, @"^Registry restore blocked: executable settings require explicit user confirmation\.$", "레지스트리 복원이 차단되었습니다. 실행 파일 설정에 대한 명시적인 확인이 필요합니다.");
            Add(rules, @"^Registry restore canceled: user did not approve the unrecognized executable settings\.$", "레지스트리 복원이 취소되었습니다. 인식되지 않은 실행 파일 설정을 승인하지 않았습니다.");
            Add(rules, @"^User approved restoring the unrecognized executable settings\.$", "인식되지 않은 실행 파일 설정을 복원하도록 승인했습니다.");
            Add(rules, @"^Applying registry restore from (\d+) root snapshot\(s\)\.$", "루트 스냅샷 $1 개에서 레지스트리를 복원하는 중...");
            Add(rules, @"^Starting restore for (.+)\.$", "$1 의 복원을 시작합니다.");
            Add(rules, @"^Restore requires loading target user hive\.$", "대상 사용자 하이브를 불러와야 복원할 수 있습니다.");
            Add(rules, @"^Restore completed for (.+)\.$", "$1 의 복원이 완료되었습니다.");
            Add(rules, @"^Skipping unknown backup registry value '(.+?)'\.$", "알 수 없는 백업 레지스트리 값 '$1' 을(를) 건너뜁니다.");
            Add(rules, @"^Skipping unknown backup registry key '(.+?)'\.$", "알 수 없는 백업 레지스트리 키 '$1' 을(를) 건너뜁니다.");
            Add(rules, @"^Skipping unknown backup feature '(.+?)'\.$", "알 수 없는 백업 기능 '$1' 을(를) 건너뜁니다.");

            // ---- 앱 제거 ----
            Add(rules, @"^\[WhatIf\] Remove App Package: (.+)$", "[WhatIf] 앱 패키지 제거: $1");
            Add(rules, @"^\[WhatIf\] Forcefully uninstall Microsoft Edge$", "[WhatIf] Microsoft Edge 강제 제거");
            Add(rules, @"^> Forcefully uninstalling Microsoft Edge\.\.\.$", "> Microsoft Edge 를 강제로 제거하는 중...");
            Add(rules, @"^Removing (.+)$", "$1 제거 중...");
            Add(rules, @"^Skipping (.+?) because its uninstall is already scheduled\.$", "$1 은(는) 제거가 이미 예약되어 있어 건너뜁니다.");
            Add(rules, @"^Unable to uninstall Microsoft Edge via WinGet$", "WinGet 을 통해 Microsoft Edge 를 제거하지 못했습니다");
            Add(rules, @"^Unable to uninstall (.+?) via WinGet$", "WinGet 을 통해 $1 을(를) 제거하지 못했습니다");
            Add(rules, @"^Removal of app '(.+?)' is not supported and will be skipped$", "'$1' 앱은 제거를 지원하지 않아 건너뜁니다.");
            Add(rules, @"^Running uninstaller\.\.\.$", "제거 프로그램 실행 중...");
            Add(rules, @"^Removing leftover files\.\.\.$", "남은 파일 제거 중...");
            Add(rules, @"^Cleaning up registry\.\.\.$", "레지스트리 정리 중...");
            Add(rules, @"^Unable to forcefully uninstall Microsoft Edge, uninstaller could not be found$", "제거 프로그램을 찾지 못해 Microsoft Edge 를 강제로 제거하지 못했습니다");

            // ---- Microsoft Store 검색 제안 ----
            Add(rules, @"^\[WhatIf\] Disable Microsoft Store search suggestions for user (.+?) by restricting access to (.+)$", "[WhatIf] 접근 권한을 제한하여 사용자 $1 의 Microsoft Store 검색 제안 사용 안 함 설정");
            Add(rules, @"^\[WhatIf\] Re-enable Microsoft Store search suggestions for user (.+?) by restoring access to (.+)$", "[WhatIf] 접근 권한을 복원하여 사용자 $1 의 Microsoft Store 검색 제안 다시 사용 설정");
            Add(rules, @"^> Disabling Microsoft Store search suggestions in the start menu for all users\.\.\.$", "> 모든 사용자의 시작 메뉴에서 Microsoft Store 검색 제안을 사용하지 않도록 설정하는 중...");
            Add(rules, @"^> Disabling Microsoft Store search suggestions for user (.+?)\.\.\.$", "> 사용자 $1 의 시작 메뉴에서 Microsoft Store 검색 제안을 사용하지 않도록 설정하는 중...");
            Add(rules, @"^> Re-enabling Microsoft Store search suggestions in the start menu for all users\.\.\.$", "> 모든 사용자의 시작 메뉴에서 Microsoft Store 검색 제안을 다시 사용하도록 설정하는 중...");
            Add(rules, @"^> Re-enabling Microsoft Store search suggestions for user (.+?)\.\.\.$", "> 사용자 $1 의 시작 메뉴에서 Microsoft Store 검색 제안을 다시 사용하도록 설정하는 중...");
            Add(rules, @"^Unable to disable Microsoft Store search suggestions because the Store database for user (.+?) could not be resolved\.$", "사용자 $1 의 Microsoft Store 데이터베이스를 확인할 수 없어 Store 검색 제안을 사용하지 않도록 설정하지 못했습니다.");
            Add(rules, @"^Unable to re-enable Microsoft Store search suggestions because the Store database for user (.+?) could not be resolved\.$", "사용자 $1 의 Microsoft Store 데이터베이스를 확인할 수 없어 Store 검색 제안을 다시 사용하도록 설정하지 못했습니다.");
            Add(rules, @"^Unable to find Store app database for user (.+?), creating it now to prevent Windows from creating it later\.\.\.$", "사용자 $1 의 Store 앱 데이터베이스가 없어 Windows 가 나중에 만들지 않도록 지금 생성합니다...");
            Add(rules, @"^Store app database not found for user (.+?), nothing to undo$", "사용자 $1 의 Store 앱 데이터베이스가 없어 되돌릴 작업이 없습니다");

            // ---- 예약 작업 (원격 측정) ----
            Add(rules, @"^\[WhatIf\] Disable Scheduled Task: (.+)$", "[WhatIf] 예약 작업 사용 안 함 설정: $1");
            Add(rules, @"^\[WhatIf\] Enable Scheduled Task: (.+)$", "[WhatIf] 예약 작업 사용 설정: $1");
            Add(rules, @"^> Disabling telemetry scheduled tasks\.\.\.$", "> 원격 측정 예약 작업을 사용하지 않도록 설정하는 중...");
            Add(rules, @"^> Enabling telemetry scheduled tasks\.\.\.$", "> 원격 측정 예약 작업을 사용하도록 설정하는 중...");
            Add(rules, @"^Failed to disable Scheduled Task: (.+)$", "예약 작업을 사용하지 않도록 설정하지 못했습니다: $1");
            Add(rules, @"^Failed to enable Scheduled Task: (.+)$", "예약 작업을 사용하도록 설정하지 못했습니다: $1");
            Add(rules, @"^Unable to determine the result of disabling Scheduled Task: (.+?)\.$", "예약 작업을 사용하지 않도록 설정한 결과를 확인할 수 없습니다: $1");

            // ---- Windows 선택적 기능 ----
            Add(rules, @"^\[WhatIf\] Enable Windows feature: (.+)$", "[WhatIf] Windows 기능 사용 설정: $1");
            Add(rules, @"^\[WhatIf\] Disable Windows feature: (.+)$", "[WhatIf] Windows 기능 사용 안 함 설정: $1");
            Add(rules, @"^Failed to enable Windows feature '(.+?)': (.+)$", "Windows 기능 '$1' 을(를) 사용하도록 설정하지 못했습니다: $2");
            Add(rules, @"^Failed to disable Windows feature '(.+?)': (.+)$", "Windows 기능 '$1' 을(를) 사용하지 않도록 설정하지 못했습니다: $2");

            // ---- 설정 저장 / 내보내기 ----
            Add(rules, @"^\[WhatIf\] Save settings to LastUsedSettings\.json$", "[WhatIf] 설정을 LastUsedSettings.json 에 저장");
            Add(rules, @"^\[WhatIf\] Export configuration to '(.+?)'$", "[WhatIf] 설정을 '$1' 로 내보내기");

            // ---- 완료 / 안내 ----
            Add(rules, @"^Script completed! Please check above for any errors\.$", "스크립트가 완료되었습니다. 위 내용에서 오류가 있는지 확인해 주세요.");
            Add(rules, @"^Script completed with (\d+) error\(s\)\.$", "스크립트가 오류 $1 건과 함께 완료되었습니다.");
            Add(rules, @"^All changes have been applied successfully!$", "모든 변경 사항이 성공적으로 적용되었습니다!");
            Add(rules, @"^The script completed without making any changes\.$", "변경 사항을 적용하지 않고 스크립트가 종료되었습니다.");
            Add(rules, @"^Press any key to exit\.\.\.$", "종료하려면 아무 키나 누르세요...");
            Add(rules, @"^Press enter to exit\.\.\.$", "종료하려면 Enter 키를 누르세요...");
            Add(rules, @"^Press enter to execute the script or press CTRL\+C to quit\.\.\.$", "스크립트를 실행하려면 Enter 키를, 중단하려면 CTRL+C 를 누르세요...");
            Add(rules, @"^Press enter to remove the selected apps or press CTRL\+C to quit\.\.\.$", "선택한 앱을 제거하려면 Enter 키를, 중단하려면 CTRL+C 를 누르세요...");
            Add(rules, @"^You have selected (\d+) apps for removal$", "제거할 앱 $1 개를 선택했습니다");
            Add(rules, @"^Selection was cancelled, no apps have been removed$", "선택이 취소되어 어떤 앱도 제거되지 않았습니다");
            Add(rules, @"^Cancelled application selection, please try again$", "앱 선택이 취소되었습니다. 다시 시도해 주세요");
            Add(rules, @"^WinGet is not installed or outdated, this may prevent Win11Debloat from removing certain apps$", "WinGet 이 설치되어 있지 않거나 버전이 오래되어 일부 앱을 제거하지 못할 수 있습니다");
            Add(rules, @"^Failed to load feature info from Features\.json file$", "Features.json 파일에서 기능 정보를 불러오지 못했습니다");
            Add(rules, @"^Failed to load language '(.+?)', falling back to en-US\.$", "언어 '$1' 을(를) 불러오지 못해 en-US 로 대체합니다.");
            Add(rules, @"^Unable to load the en-US language files\. The GUI cannot continue without them\.$", "en-US 언어 파일을 불러올 수 없습니다. 계속하려면 해당 파일이 필요합니다.");

            return rules.ToArray();
        }

        /// <summary>
        /// "Skipping X: it is only supported on Windows build 22621 or older, this system runs build 26100."
        /// 형태의 메시지를 번역한다. 빌드 범위 표현("build N or newer" / "build N or older")도 함께 바꾼다.
        /// </summary>
        private static string SkippingBuildEvaluator(Match match)
        {
            string feature = match.Groups[1].Value;
            string range = match.Groups[2].Value;
            string currentBuild = match.Groups[3].Value;

            range = range.Replace("build ", "빌드 ");
            range = range.Replace(" or newer", " 이상");
            range = range.Replace(" or older", " 이하");
            range = range.Replace(" and ", " 및 ");

            return feature + " 은(는) Windows 빌드 " + range + " 에서만 지원되어 이 시스템(빌드 " +
                   currentBuild + ")에서는 건너뜁니다.";
        }

        /// <summary>upstream CLI 헤더 제목에 대응하는 한국어.</summary>
        private static string TranslateHeaderTitle(string title)        {
            switch (title)
            {
                case "Configuration": return "구성";
                case "Default Mode": return "기본 모드";
                case "Custom Mode": return "사용자 지정 모드";
                case "Menu": return "메뉴";
                case "App Removal": return "앱 제거";
                default: return title;
            }
        }

        /// <summary>" Win11Debloat Script - Configuration (User: x)" 형태의 헤더를 번역한다.</summary>
        private static string HeaderEvaluator(Match match)
        {
            string indent = match.Groups[1].Value;
            string title = TranslateHeaderTitle(match.Groups[2].Value.Trim());
            string tail = string.Empty;

            if (match.Groups[3].Success)
            {
                tail = match.Groups[3].Value;
                tail = tail.Replace("(User:", "(사용자:");
                tail = tail.Replace("(Sysprep mode)", "(Sysprep 모드)");
            }

            return indent + "Win11Debloat 스크립트 - " + title + tail;
        }

        private static void Add(List<Rule> rules, string pattern, string replacement)
        {
            rules.Add(new Rule
            {
                Pattern = new Regex(pattern, RegexOptions.CultureInvariant),
                Replacement = replacement
            });
        }

        private static void AddEval(List<Rule> rules, string pattern, MatchEvaluator evaluator)
        {
            rules.Add(new Rule
            {
                Pattern = new Regex(pattern, RegexOptions.CultureInvariant),
                Evaluator = evaluator
            });
        }
    }
}
