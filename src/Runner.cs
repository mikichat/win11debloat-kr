using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Win11DebloatKR
{
    /// <summary>
    /// Windows PowerShell 5.1 로 win11debloat 스크립트를 실행하고
    /// 표준 출력/오류를 실시간으로 전달한다.
    /// </summary>
    internal sealed class Runner
    {
        private Process _process;

        /// <summary>(출력 문자열, 오류 여부) 형태의 이벤트.</summary>
        public event Action<string, bool> Output;

        public bool IsRunning
        {
            get { return _process != null && !_process.HasExited; }
        }

        public int ExitCode
        {
            get
            {
                if (_process == null) return -1;
                try { return _process.ExitCode; }
                catch (InvalidOperationException) { return -1; }
            }
        }

        /// <summary>스크립트를 실행한다. 실행은 별도 스레드에서 진행된다.</summary>
        public void Start(string scriptPath, IList<string> arguments, string workingDirectory)
        {
            if (IsRunning) throw new InvalidOperationException("이미 실행 중입니다.");

            string powershell = Path.Combine(
                Environment.SystemDirectory,
                Path.Combine("WindowsPowerShell", Path.Combine("v1.0", "powershell.exe")));

            if (!File.Exists(powershell))
            {
                throw new FileNotFoundException("Windows PowerShell 5.1 을 찾을 수 없습니다.", powershell);
            }

            string command = BuildCommand(scriptPath, arguments);

            ProcessStartInfo info = new ProcessStartInfo
            {
                FileName = powershell,
                // -EncodedCommand 는 콘솔이 없는 GUI 프로세스에서 시작할 때
                // '액세스가 거부됩니다' 로 실패하는 경우가 있어 -Command 로 전달한다.
                // (upstream 의 Run.bat 과 동일한 방식)
                Arguments = "-NoProfile -ExecutionPolicy Bypass -Command \"" +
                            command.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                // 콘솔이 없는 GUI 프로세스에서는 상속된 표준 입력 핸들이 비어 있어
                // 자식 실행이 실패할 수 있으므로 전용 파이프를 만들어 넘긴다.
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = workingDirectory
            };

            // PowerShell 7 에서 실행된 프로그램은 PSModulePath 에 PowerShell 7 경로가 들어가 있다.
            // 그대로 넘기면 Windows PowerShell 5.1 의 내장 모듈(Appx, Security 등)을
            // 자동 로드하지 못하므로 Windows PowerShell 의 Modules 경로를 앞에 붙여 준다.
            try
            {
                string psHome = Path.GetDirectoryName(powershell);
                string psModules = Path.Combine(psHome, "Modules");
                string current = Environment.GetEnvironmentVariable("PSModulePath") ?? string.Empty;
                if (!current.StartsWith(psModules, StringComparison.OrdinalIgnoreCase))
                {
                    info.EnvironmentVariables["PSModulePath"] =
                        current.Length > 0 ? psModules + ";" + current : psModules;
                }
            }
            catch (Exception)
            {
                // 환경 변수 설정 실패는 무시한다.
            }

            _process = new Process { StartInfo = info, EnableRaisingEvents = true };
            _process.OutputDataReceived += OnOutput;
            _process.ErrorDataReceived += OnError;
            _process.Exited += OnExited;

            try
            {
                _process.Start();
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                string detail =
                    "PowerShell 실행 실패 (네이티브 오류 " + ex.NativeErrorCode + ")\r\n" +
                    "  FileName        : " + info.FileName + "\r\n" +
                    "  FileName 존재   : " + File.Exists(info.FileName) + "\r\n" +
                    "  WorkingDirectory: " + info.WorkingDirectory + "\r\n" +
                    "  디렉터리 존재   : " + Directory.Exists(info.WorkingDirectory) + "\r\n" +
                    "  SystemDirectory  : " + Environment.SystemDirectory + "\r\n" +
                    "  64비트 프로세스  : " + Environment.Is64BitProcess + "\r\n" +
                    "  명령줄 길이     : " + info.Arguments.Length;
                WriteDiagnostic(detail);
                Raise(detail, true);
                throw;
            }

            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();

            // 표준 입력을 닫아서 스크립트가 입력을 기다리지 않도록 한다.
            try { _process.StandardInput.Close(); }
            catch (Exception) { }
        }

        /// <summary>실행 중인 프로세스를 강제 종료한다.</summary>
        public void Kill()
        {
            if (_process == null) return;
            try
            {
                if (!_process.HasExited) _process.Kill();
            }
            catch (InvalidOperationException)
            {
                // 이미 종료됨
            }
            catch (System.ComponentModel.Win32Exception)
            {
                // 종료할 수 없음
            }
        }

        /// <summary>PowerShell 명령 문자열을 만든다.</summary>
        /// <remarks>
        /// 콘솔이 없는 환경에서 Clear-Host 가 예외를 던지는 것을 막기 위해
        /// Clear-Host 를 빈 함수로 대체하고, 출력을 UTF-8 로 맞춘다.
        /// -WhatIf 실행에서는 Start-Transcript 가 수행되지 않으므로
        /// Stop-Transcript 도 빈 함수로 대체한다.
        /// -Command 로 전달하므로 한 줄로 작성하고 큰따옴표를 쓰지 않는다.
        /// </remarks>
        private static string BuildCommand(string scriptPath, IList<string> arguments)
        {
            bool whatIf = false;
            foreach (string argument in arguments)
            {
                if (string.Equals(argument, "-WhatIf", StringComparison.OrdinalIgnoreCase))
                {
                    whatIf = true;
                    break;
                }
            }

            StringBuilder builder = new StringBuilder();
            builder.Append("try { [Console]::OutputEncoding = [System.Text.Encoding]::UTF8 } catch { }; ");
            builder.Append("$OutputEncoding = [System.Text.Encoding]::UTF8; ");
            builder.Append("function Clear-Host { param() }; ");

            // -WhatIf 모드에서는 PowerShell 의 Start-Transcript 자체가 수행되지 않는다.
            // upstream 이 마지막에 호출하는 Stop-Transcript 는 '기록 중이 아니다' 는
            // 오류를 내므로, 진단 검사(WhatIf)에서는 빈 함수로 대체해 로그를 깨끗하게 유지한다.
            // 실제 적용 모드에서는 기록을 정상적으로 끝내야 하므로 대체하지 않는다.
            if (whatIf)
            {
                builder.Append("function Stop-Transcript { param() }; ");
            }

            builder.Append("& '");
            builder.Append(scriptPath.Replace("'", "''"));
            builder.Append("'");

            foreach (string argument in arguments)
            {
                builder.Append(' ');
                builder.Append(argument);
            }

            return builder.ToString();
        }

        private void OnOutput(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            Raise(ConsoleTranslator.Translate(e.Data), false);
        }

        private void OnError(object sender, DataReceivedEventArgs e)
        {
            if (e.Data == null) return;
            // PowerShell 이 비동기 진행률 레코드를 CLIXML 로 stderr 에 남긴다.
            // 사용자에게는 noise 이므로 걸러낸다.
            if (e.Data.StartsWith("#< CLIXML", StringComparison.Ordinal) ||
                e.Data.StartsWith("<Objs", StringComparison.Ordinal))
            {
                return;
            }
            Raise(ConsoleTranslator.Translate(e.Data), true);
        }

        private void OnExited(object sender, EventArgs e)
        {
            Raise("=== 실행이 종료되었습니다 (종료 코드: " + ExitCode + ") ===", false);
        }

        private void Raise(string text, bool isError)
        {
            Action<string, bool> handler = Output;
            if (handler != null) handler(text, isError);
        }

        /// <summary>진단 정보를 파일에 남긴다.</summary>
        internal static void WriteDiagnostic(string text)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Win11DebloatKR");
                Directory.CreateDirectory(folder);
                File.AppendAllText(
                    Path.Combine(folder, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" + Environment.NewLine +
                    text + Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // 진단 기록 실패는 무시한다.
            }
        }
    }
}
