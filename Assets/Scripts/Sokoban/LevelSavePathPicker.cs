using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;

/// <summary>
/// 关卡 JSON 的保存 / 打开对话框，默认目录为 <c>Assets/LevelFiles</c>。
/// 编辑器内使用 <c>EditorUtility</c>；Windows 独立构建使用 <c>comdlg32</c>。
/// </summary>
public static class LevelSavePathPicker
{
    const string DefaultBaseFileName = "mylevel";

    /// <summary>
    /// 默认关卡目录：<c>Assets/LevelFiles</c>（即 <c>Application.dataPath/LevelFiles</c>）。
    /// </summary>
    public static string GetDefaultLevelFilesDirectory()
    {
        return Path.GetFullPath(Path.Combine(Application.dataPath, "LevelFiles"));
    }

    /// <summary>
    /// 弹出保存对话框；用户取消则返回 false。
    /// </summary>
    public static bool TryPickSaveJsonPath(out string fullPath, string defaultBaseName = null)
    {
        fullPath = null;
        var dir = GetDefaultLevelFilesDirectory();
        try
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Sokoban] 无法创建 LevelFiles 目录：" + ex.Message);
        }

        var baseName = string.IsNullOrWhiteSpace(defaultBaseName) ? DefaultBaseFileName : defaultBaseName.Trim();

#if UNITY_EDITOR
        fullPath = UnityEditor.EditorUtility.SaveFilePanel(
            "保存关卡",
            dir,
            baseName,
            "json");
        if (string.IsNullOrEmpty(fullPath))
            return false;
        if (!fullPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            fullPath += ".json";
        return true;
#elif UNITY_STANDALONE_WIN
        if (TryWindowsSaveDialog(dir, baseName, out fullPath))
            return true;
        Debug.LogWarning("[Sokoban] 系统保存对话框不可用，已取消保存。");
        return false;
#else
        Debug.LogWarning("[Sokoban] 当前平台未集成保存对话框，请在 Unity 编辑器内保存，或扩展 LevelSavePathPicker。");
        return false;
#endif
    }

    /// <summary>
    /// 弹出打开文件对话框；默认打开 <see cref="GetDefaultLevelFilesDirectory"/>；用户取消则返回 false。
    /// </summary>
    public static bool TryPickOpenJsonPath(out string fullPath)
    {
        fullPath = null;
        var dir = GetDefaultLevelFilesDirectory();
        try
        {
            if (!Directory.Exists(dir))
                Directory.CreateDirectory(dir);
        }
        catch (Exception ex)
        {
            Debug.LogWarning("[Sokoban] 无法创建 LevelFiles 目录：" + ex.Message);
        }

#if UNITY_EDITOR
        fullPath = UnityEditor.EditorUtility.OpenFilePanel(
            "打开关卡",
            dir,
            "json");
        return !string.IsNullOrEmpty(fullPath);
#elif UNITY_STANDALONE_WIN
        if (TryWindowsOpenDialog(dir, out fullPath))
            return true;
        Debug.LogWarning("[Sokoban] 系统打开对话框不可用，已取消。");
        return false;
#else
        Debug.LogWarning("[Sokoban] 当前平台未集成打开对话框，请在 Unity 编辑器内打开，或扩展 LevelSavePathPicker。");
        return false;
#endif
    }

#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
    const int OFN_PATHMUSTEXIST = 0x800;
    const int OFN_OVERWRITEPROMPT = 0x2;
    const int OFN_NOCHANGEDIR = 0x8;
    const int OFN_EXPLORER = 0x80000;
    const int OFN_HIDEREADONLY = 0x4;
    const int OFN_ENABLESIZING = 0x800000;

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetSaveFileNameW(ref OpenFileNameW ofn);

    [DllImport("comdlg32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool GetOpenFileNameW(ref OpenFileNameW ofn);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    struct OpenFileNameW
    {
        public int lStructSize;
        public IntPtr hwndOwner;
        public IntPtr hInstance;
        public string lpstrFilter;
        public string lpstrCustomFilter;
        public int nMaxCustFilter;
        public int nFilterIndex;
        public IntPtr lpstrFile;
        public int nMaxFile;
        public string lpstrFileTitle;
        public int nMaxFileTitle;
        public string lpstrInitialDir;
        public string lpstrTitle;
        public int Flags;
        public short nFileOffset;
        public short nFileExtension;
        public string lpstrDefExt;
        public IntPtr lCustData;
        public IntPtr lpfnHook;
        public string lpTemplateName;
        public IntPtr pvReserved;
        public int dwReserved;
        public int FlagsEx;
    }

    static bool TryWindowsSaveDialog(string initialDir, string baseName, out string fullPath)
    {
        fullPath = null;
        const int maxChars = 32768;
        var fileBuffer = Marshal.AllocHGlobal(maxChars * 2);
        try
        {
            for (var i = 0; i < maxChars * 2; i++)
                Marshal.WriteByte(fileBuffer, i, 0);

            var start = baseName;
            if (start.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                start = start[..^5];
            var utf16 = Encoding.Unicode.GetBytes(start + "\0\0");
            if (utf16.Length >= maxChars * 2)
                utf16 = Encoding.Unicode.GetBytes(DefaultBaseFileName + "\0\0");
            Marshal.Copy(utf16, 0, fileBuffer, utf16.Length);

            var ofn = new OpenFileNameW
            {
                lStructSize = Marshal.SizeOf(typeof(OpenFileNameW)),
                hwndOwner = IntPtr.Zero,
                lpstrFilter = "JSON 关卡\0*.json\0所有文件\0*.*\0\0",
                lpstrFile = fileBuffer,
                nMaxFile = maxChars,
                lpstrInitialDir = string.IsNullOrEmpty(initialDir) ? null : initialDir,
                lpstrTitle = "保存关卡",
                lpstrDefExt = "json",
                Flags = OFN_PATHMUSTEXIST | OFN_OVERWRITEPROMPT | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_HIDEREADONLY | OFN_ENABLESIZING,
            };

            if (!GetSaveFileNameW(ref ofn))
                return false;

            fullPath = Marshal.PtrToStringUni(fileBuffer);
            return !string.IsNullOrEmpty(fullPath);
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }

    const int OFN_FILEMUSTEXIST = 0x1000;

    static bool TryWindowsOpenDialog(string initialDir, out string fullPath)
    {
        fullPath = null;
        const int maxChars = 32768;
        var fileBuffer = Marshal.AllocHGlobal(maxChars * 2);
        try
        {
            for (var i = 0; i < maxChars * 2; i++)
                Marshal.WriteByte(fileBuffer, i, 0);

            var ofn = new OpenFileNameW
            {
                lStructSize = Marshal.SizeOf(typeof(OpenFileNameW)),
                hwndOwner = IntPtr.Zero,
                lpstrFilter = "JSON 关卡\0*.json\0所有文件\0*.*\0\0",
                lpstrFile = fileBuffer,
                nMaxFile = maxChars,
                lpstrInitialDir = string.IsNullOrEmpty(initialDir) ? null : initialDir,
                lpstrTitle = "打开关卡",
                lpstrDefExt = "json",
                Flags = OFN_PATHMUSTEXIST | OFN_FILEMUSTEXIST | OFN_NOCHANGEDIR | OFN_EXPLORER | OFN_HIDEREADONLY | OFN_ENABLESIZING,
            };

            if (!GetOpenFileNameW(ref ofn))
                return false;

            fullPath = Marshal.PtrToStringUni(fileBuffer);
            return !string.IsNullOrEmpty(fullPath);
        }
        finally
        {
            Marshal.FreeHGlobal(fileBuffer);
        }
    }
#endif
}
