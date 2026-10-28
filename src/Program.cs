using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Win11DebloatKR
{
    internal static class Program
    {
        private const string MutexName = "Local\\Win11DebloatKR_SingleInstance";

        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [STAThread]
        private static int Main(string[] args)
        {
            try { SetProcessDPIAware(); }
            catch (EntryPointNotFoundException) { }
            catch (DllNotFoundException) { }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Application.ThreadException += OnThreadException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;

            // 미리보기 모드: 관리자 권한 승인을 요구하지 않고 UI 만 확인한다.
            bool previewMode = false;
            bool selfTest = false;
            int initialTab = -1;
            foreach (string arg in args)
            {
                if (string.Equals(arg, "--preview", StringComparison.OrdinalIgnoreCase))
                {
                    previewMode = true;
                }
                else if (string.Equals(arg, "--selftest", StringComparison.OrdinalIgnoreCase))
                {
                    selfTest = true;
                }
                else if (arg.StartsWith("--tab=", StringComparison.OrdinalIgnoreCase))
                {
                    int parsed;
                    if (int.TryParse(arg.Substring(6), out parsed)) initialTab = parsed;
                }
            }

            // 관리자 권한이 없으면 관리자 권한으로 다시 실행한다.
            if (!previewMode && !IsAdministrator())
            {
                return RelaunchElevated(args);
            }

            bool createdNew;
            using (Mutex mutex = new Mutex(true, MutexName, out createdNew))
            {
                if (!createdNew)
                {
                    MessageBox.Show("이미 실행 중입니다. 기존 창을 확인해 주세요.",
                        "Win11Debloat 한국어 도구", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return 0;
                }

                try
                {
                    Payload.EnsureExtracted();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        "내장 스크립트를 준비하지 못했습니다.\n\n" + ex.Message,
                        "시작 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    return 1;
                }

                try
                {
                    Application.Run(new MainForm(Payload.Root, previewMode, initialTab, selfTest));
                }
                finally
                {
                    mutex.ReleaseMutex();
                }
            }

            return 0;
        }

        private static bool IsAdministrator()
        {
            try
            {
                System.Security.Principal.WindowsIdentity identity =
                    System.Security.Principal.WindowsIdentity.GetCurrent();
                System.Security.Principal.WindowsPrincipal principal =
                    new System.Security.Principal.WindowsPrincipal(identity);
                return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static int RelaunchElevated(string[] args)
        {
            try
            {
                string executable = Application.ExecutablePath;
                if (string.IsNullOrEmpty(executable))
                {
                    executable = Assembly.GetExecutingAssembly().Location;
                }

                ProcessStartInfo info = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = string.Join(" ", args),
                    UseShellExecute = true,
                    Verb = "runas",
                    WorkingDirectory = Path.GetDirectoryName(executable)
                };

                Process.Start(info);
                return 0;
            }
            catch (System.ComponentModel.Win32Exception)
            {
                MessageBox.Show("관리자 권한으로 실행하기 위해 권한 승인을 해 주세요.\n" +
                    "승인하지 않으면 변경 사항을 적용할 수 없습니다.",
                    "관리자 권한 필요", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
            catch (Exception ex)
            {
                MessageBox.Show("관리자 권한으로 다시 실행하지 못했습니다.\n\n" + ex.Message,
                    "실행 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
        }

        private static void OnThreadException(object sender, ThreadExceptionEventArgs e)
        {
            ReportError(e.Exception);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            ReportError(e.ExceptionObject as Exception);
        }

        private static void ReportError(Exception exception)
        {
            string text = exception == null ? "알 수 없는 오류" : exception.Message;
            WriteErrorLog(exception);
            MessageBox.Show("다음 오류가 발생했습니다:\n\n" + text,
                "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        /// <summary>예외를 %LOCALAPPDATA%\Win11DebloatKR\error.log 에 남긴다.</summary>
        private static void WriteErrorLog(Exception exception)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Win11DebloatKR");
                Directory.CreateDirectory(folder);
                File.AppendAllText(
                    Path.Combine(folder, "error.log"),
                    "[" + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "]" +
                    Environment.NewLine + (exception == null ? "알 수 없는 오류" : exception.ToString()) +
                    Environment.NewLine + Environment.NewLine,
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // 로그를 남기지 못해도 원래 오류 처리를 계속한다.
            }
        }
    }
}
