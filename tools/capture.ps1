<#
    Win11DebloatKR.exe GUI 스크린샷 캡처 도구 (검증용)

    Usage:
      pwsh -File .\tools\capture.ps1 -Output shot.png [-DelaySeconds 4] [-Keys "{TAB}"]

    앱을 --preview 모드로 실행하고, 창을 캡처한 뒤 종료한다.
#>
param(
    [string]$Output = (Join-Path $PSScriptRoot 'shot.png'),
    [int]$DelaySeconds = 4,
    [string]$Keys = '',
    [int]$TabIndex = -1,
    [switch]$SelfTest,
    [string]$Exe = (Join-Path (Split-Path $PSScriptRoot -Parent) 'build\Win11DebloatKR.exe')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
Add-Type -AssemblyName System.Windows.Forms

Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class Win32Capture {
    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint flags);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
}
'@

Write-Host "실행 중: $Exe --preview"
$argList = @('--preview')
if ($SelfTest) {
    $argList = @('--selftest')
    Write-Host "실행 중: $Exe --selftest"
}
if ($TabIndex -ge 0) { $argList += "--tab=$TabIndex" }
$process = Start-Process -FilePath $Exe -ArgumentList $argList -PassThru
Start-Sleep -Seconds $DelaySeconds

$process.Refresh()
$handle = $process.MainWindowHandle
if ($handle -eq [IntPtr]::Zero) {
    Write-Host '창 핸들을 찾지 못했습니다.'
    try { $process.Kill() } catch { }
    exit 1
}

[void][Win32Capture]::ShowWindow($handle, 5)   # SW_SHOW
[void][Win32Capture]::SetForegroundWindow($handle)
Start-Sleep -Milliseconds 700

Write-Host "창 제목: $($process.MainWindowTitle)"

if ($Keys) {
    [System.Windows.Forms.SendKeys]::SendWait($Keys)
    Start-Sleep -Milliseconds 900
}

$rect = New-Object Win32Capture+RECT
[void][Win32Capture]::GetWindowRect($handle, [ref]$rect)
$width = $rect.Right - $rect.Left
$height = $rect.Bottom - $rect.Top
Write-Host "창 크기: ${width}x${height}"

$bitmap = New-Object System.Drawing.Bitmap($width, $height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$hdc = $graphics.GetHdc()
try {
    [void][Win32Capture]::PrintWindow($handle, $hdc, 2)
}
finally {
    $graphics.ReleaseHdc($hdc)
    $graphics.Dispose()
}

$bitmap.Save($Output, [System.Drawing.Imaging.ImageFormat]::Png)
$bitmap.Dispose()
Write-Host "저장됨: $Output"

try { $process.CloseMainWindow() | Out-Null } catch { }
Start-Sleep -Milliseconds 800
try { if (-not $process.HasExited) { $process.Kill() } } catch { }
