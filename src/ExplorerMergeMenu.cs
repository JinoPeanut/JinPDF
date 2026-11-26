using System;
using System.IO;
using System.Runtime.InteropServices;

static class ExplorerMergeMenu
{
    public const string MergeArgument = "--merge";
    public const string MenuName = "JinPDF로 병합";

    public static string ShortcutPath(string folder)
    {
        return Path.Combine(folder, MenuName + ".lnk");
    }

    public static void Register(string folder, string executable)
    {
        Directory.CreateDirectory(folder);
        var shellType = Type.GetTypeFromProgID("WScript.Shell", true);
        dynamic shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic shortcut = shell.CreateShortcut(ShortcutPath(folder));
            try
            {
                shortcut.TargetPath = executable;
                shortcut.Arguments = MergeArgument;
                shortcut.WorkingDirectory = Path.GetDirectoryName(executable);
                shortcut.Save();
            }
            finally { Marshal.ReleaseComObject(shortcut); }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }

    public static void Unregister(string folder)
    {
        File.Delete(ShortcutPath(folder));
    }

    public static bool IsRegisteredFor(string folder, string executable)
    {
        var shellType = Type.GetTypeFromProgID("WScript.Shell", true);
        dynamic shell = Activator.CreateInstance(shellType);
        try
        {
            dynamic shortcut = shell.CreateShortcut(ShortcutPath(folder));
            try
            {
                return string.Equals((string)shortcut.TargetPath, executable, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals((string)shortcut.Arguments, MergeArgument, StringComparison.Ordinal);
            }
            finally { Marshal.ReleaseComObject(shortcut); }
        }
        finally { Marshal.ReleaseComObject(shell); }
    }
}
