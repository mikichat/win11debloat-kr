// ConsoleTranslator 규칙 검증용 임시 테스트 하네스 (빌드 산출물에 포함되지 않음)
using System;
using System.Collections.Generic;

namespace Win11DebloatKR
{
    internal static class TranslatorTest
    {
        private static int _failed;

        private static void Main()
        {
            Check("Win11Debloat is launching...", "Win11Debloat 실행 중...");
            Check("                Keep this window open", "                이 창을 닫지 마세요");
            Check(" Win11Debloat Script - Configuration (User: gunsan1)", " Win11Debloat 스크립트 - 구성 (사용자: gunsan1)");
            Check(" Win11Debloat Script - Configuration", " Win11Debloat 스크립트 - 구성");
            Check(" Win11Debloat Script - Default Mode (User: gunsan1)", " Win11Debloat 스크립트 - 기본 모드 (사용자: gunsan1)");
            Check(" Win11Debloat Script - Configuration (Sysprep mode)", " Win11Debloat 스크립트 - 구성 (Sysprep 모드)");
            Check("[WhatIf] Apply 18 registry changes from 'C:\\x\\Disable_Windows_Suggestions.reg'",
                  "[WhatIf] 'C:\\x\\Disable_Windows_Suggestions.reg' 파일에서 레지스트리 변경 18건 적용");
            Check("[WhatIf] Create registry backup", "[WhatIf] 레지스트리 백업 생성");
            Check("[WhatIf] Create system restore point", "[WhatIf] 시스템 복원 지점 생성");
            Check("[WhatIf] Restart the Windows Explorer process", "[WhatIf] Windows 탐색기 프로세스 다시 시작");
            Check("[WhatIf] Remove App Package: Microsoft.XboxApp", "[WhatIf] 앱 패키지 제거: Microsoft.XboxApp");
            Check("[WhatIf] Disable Scheduled Task: \\Microsoft\\Windows\\X", "[WhatIf] 예약 작업 사용 안 함 설정: \\Microsoft\\Windows\\X");
            Check("[WhatIf] Enable Windows feature: Containers-DisposableClientVM", "[WhatIf] Windows 기능 사용 설정: Containers-DisposableClientVM");
            Check("[WhatIf] Save settings to LastUsedSettings.json", "[WhatIf] 설정을 LastUsedSettings.json 에 저장");
            Check("Skipping HideChat: it is only supported on Windows build 22621 or older, this system runs build 26100.",
                  "HideChat 은(는) Windows 빌드 22621 이하 에서만 지원되어 이 시스템(빌드 26100)에서는 건너뜁니다.");
            Check("Skipping Foo: it is only supported on Windows build 19045 or newer, this system runs build 26100.",
                  "Foo 은(는) Windows 빌드 19045 이상 에서만 지원되어 이 시스템(빌드 26100)에서는 건너뜁니다.");
            Check("경고: Skipping HideChat: it is only supported on Windows build 22621 or older, this system runs build 26100.",
                  "경고: HideChat 은(는) Windows 빌드 22621 이하 에서만 지원되어 이 시스템(빌드 26100)에서는 건너뜁니다.");
            Check("Script completed! Please check above for any errors.",
                  "스크립트가 완료되었습니다. 위 내용에서 오류가 있는지 확인해 주세요.");
            Check("Explorer process restart was skipped, please manually reboot your PC to apply all changes",
                  "탐색기 프로세스 재시작을 건너뛰었습니다. 변경 사항을 적용하려면 PC를 다시 시작해 주세요.");
            Check("Restarting the Windows Explorer process... (This may cause your screen to flicker)",
                  "Windows 탐색기 프로세스를 다시 시작하는 중... (화면이 잠시 깜빡일 수 있습니다)");
            Check("Removing Microsoft Edge", "Microsoft Edge 제거 중...");
            Check("Skipping Spotify because its uninstall is already scheduled.",
                  "Spotify 은(는) 제거가 이미 예약되어 있어 건너뜁니다.");
            Check("Unable to uninstall Microsoft Edge via WinGet", "WinGet 을 통해 Microsoft Edge 를 제거하지 못했습니다");
            Check("The operation completed successfully via PowerShell registry registry writer.",
                  "The operation completed successfully via PowerShell registry registry writer.");
            Check("Backup successfully created: C:\\x.bak", "백업을 성공적으로 만들었습니다: C:\\x.bak");
            Check("Unknown feature 'Foo' could not be applied.", "알 수 없는 기능 'Foo' 을(를) 적용하지 못했습니다.");
            Check("No valid apps were selected for removal", "제거할 앱이 선택되지 않았습니다");
            Check("> Attempting to restart the Windows Explorer process to apply all changes...",
                  "> 변경 사항을 적용하기 위해 Windows 탐색기 프로세스를 다시 시작하는 중...");
            Check("> Creating registry backup...", "> 레지스트리 백업 생성 중...");
            Check("> Creating a system restore point...", "> 시스템 복원 지점 생성 중...");
            Check("Replaced start menu for the default user profile", "기본 사용자 프로필의 시작 메뉴를 교체했습니다");
            Check("Replaced start menu for user gunsan1", "사용자 gunsan1 의 시작 메뉴를 교체했습니다");
            Check("All changes have been applied successfully!", "모든 변경 사항이 성공적으로 적용되었습니다!");
            Check("Press any key to exit...", "종료하려면 아무 키나 누르세요...");
            Check("Failed to apply 'Disable Suggestions': Access denied", "'Disable Suggestions' 적용에 실패했습니다: Access denied");

            // 번역 대상이 아닌 줄은 원문 그대로 유지되어야 한다.
            CheckUnchanged("> Windows 전체의 팁, 트릭, 추천 및 광고 사용 안 함 설정 중...");
            CheckUnchanged("+     Stop-Transcript");
            CheckUnchanged("    + CategoryInfo          : InvalidOperation: (:) [Stop-Transcript], PSInvalidOperationException");
            CheckUnchanged("");

            Console.WriteLine(_failed == 0 ? "모든 규칙 검증 통과" : ("실패 " + _failed + "건"));
            Environment.Exit(_failed == 0 ? 0 : 1);
        }

        private static void Check(string input, string expected)
        {
            string actual = ConsoleTranslator.Translate(input);
            if (actual == expected)
            {
                Console.WriteLine("  OK   " + input);
                return;
            }

            _failed++;
            Console.WriteLine("  FAIL " + input);
            Console.WriteLine("       기대: " + expected);
            Console.WriteLine("       실제: " + actual);
        }

        private static void CheckUnchanged(string input)
        {
            string actual = ConsoleTranslator.Translate(input);
            if (actual == input)
            {
                Console.WriteLine("  OK   (원문 유지) " + input);
                return;
            }

            _failed++;
            Console.WriteLine("  FAIL (원문 유지 실패) " + input);
            Console.WriteLine("       실제: " + actual);
        }
    }
}
