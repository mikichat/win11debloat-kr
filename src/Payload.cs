using System;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Win11DebloatKR
{
    /// <summary>
    /// exe 에 내장된 win11debloat 실행 파일 묶음(payload.zip)을
    /// %LOCALAPPDATA%\Win11DebloatKR\Payload 폴더로 풀어준다.
    /// </summary>
    internal static class Payload
    {
        private const string ResourceName = "Win11DebloatKR.Payload.zip";

        public static string BaseDir { get; private set; }
        public static string Root { get; private set; }

        public static void EnsureExtracted()
        {
            BaseDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Win11DebloatKR");

            string payloadDir = Path.Combine(BaseDir, "Payload");
            string marker = Path.Combine(BaseDir, "payload.sha256");

            byte[] archive = ReadResource();
            string hash = ComputeHash(archive);

            if (Directory.Exists(payloadDir) && File.Exists(marker))
            {
                try
                {
                    if (File.ReadAllText(marker, Encoding.UTF8).Trim() == hash)
                    {
                        Root = payloadDir;
                        return;
                    }
                }
                catch (IOException)
                {
                    // 아래에서 다시 압축 해제한다.
                }
            }

            if (Directory.Exists(payloadDir))
            {
                try { Directory.Delete(payloadDir, true); }
                catch (IOException) { /* 다른 프로세스가 사용 중이면 그대로 진행 */ }
            }

            Directory.CreateDirectory(payloadDir);
            Extract(archive, payloadDir);

            try
            {
                Directory.CreateDirectory(BaseDir);
                File.WriteAllText(marker, hash, new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // 표식 저장 실패는 무시해도 된다.
            }

            Root = payloadDir;
        }

        private static byte[] ReadResource()
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException(
                        "내부 리소스를 찾을 수 없습니다: " + ResourceName);
                }
                using (MemoryStream buffer = new MemoryStream())
                {
                    stream.CopyTo(buffer);
                    return buffer.ToArray();
                }
            }
        }

        private static void Extract(byte[] archive, string targetDir)
        {
            string root = Path.GetFullPath(targetDir);
            if (!root.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal))
            {
                root += Path.DirectorySeparatorChar;
            }

            using (MemoryStream memory = new MemoryStream(archive))
            using (ZipArchive zip = new ZipArchive(memory, ZipArchiveMode.Read))
            {
                foreach (ZipArchiveEntry entry in zip.Entries)
                {
                    string destination = Path.GetFullPath(Path.Combine(root, entry.FullName));
                    if (!destination.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidDataException("압축 해제 경로가 잘못되었습니다: " + entry.FullName);
                    }

                    if (string.IsNullOrEmpty(entry.Name))
                    {
                        Directory.CreateDirectory(destination);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    entry.ExtractToFile(destination, true);
                }
            }
        }

        private static string ComputeHash(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(data);
                StringBuilder builder = new StringBuilder(hash.Length * 2);
                foreach (byte value in hash)
                {
                    builder.Append(value.ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
