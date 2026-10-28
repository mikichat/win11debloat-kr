using System;
using System.Drawing;
using System.IO;
using System.Reflection;

namespace Win11DebloatKR
{
    /// <summary>exe 에 내장된 아이콘을 읽어 온다.</summary>
    internal static class AppIcon
    {
        private const string ResourceName = "Win11DebloatKR.AppIcon.ico";

        private static Icon _icon;

        public static Icon Create()
        {
            if (_icon != null) return _icon;

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                using (Stream stream = assembly.GetManifestResourceStream(ResourceName))
                {
                    if (stream != null)
                    {
                        _icon = new Icon(stream);
                        return _icon;
                    }
                }
            }
            catch (Exception)
            {
                // 내장 아이콘이 없으면 기본 아이콘을 사용한다.
            }

            _icon = SystemIcons.Application;
            return _icon;
        }
    }
}
