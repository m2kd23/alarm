using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Management;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows.Automation;
using InternalAlarmApp.Utils;

namespace InternalAlarmApp.Services;

public sealed class ActiveContextService
{
    public ActiveSource CaptureSource()
    {
        var handle = Native.GetForegroundWindow();
        Native.GetWindowThreadProcessId(handle, out var processId);
        var processName = "";
        try { processName = Process.GetProcessById((int)processId).ProcessName; } catch { }
        return new ActiveSource(handle, (int)processId, processName, GetWindowTitle(handle));
    }

    public ActiveTarget ResolveTarget(ActiveSource source)
    {
        var process = source.ProcessName.ToLowerInvariant();
        string target = "";

        if (process.Contains("hwp"))
            target = TryGetHwpPath() is { Length: > 0 } hwpPath ? hwpPath : TryGetProcessCommandLinePath(source.ProcessId);
        else if (process == "winword")
            target = TryGetOfficePath("Word.Application", ["ActiveDocument", "FullName"])
                ?? TryGetOfficePath("Word.Application", ["ActiveProtectedViewWindow", "Document", "FullName"])
                ?? "";
        else if (process == "excel")
            target = TryGetOfficePath("Excel.Application", ["ActiveWorkbook", "FullName"])
                ?? TryGetOfficePath("Excel.Application", ["ActiveProtectedViewWindow", "Workbook", "FullName"])
                ?? "";
        else if (process == "powerpnt")
            target = TryGetOfficePath("PowerPoint.Application", ["ActivePresentation", "FullName"]) ?? "";
        else if (process == "explorer")
            target = TryGetExplorerPath(source.WindowHandle);

        if (string.IsNullOrWhiteSpace(target) && IsBrowser(process))
            target = TryGetBrowserAddress(source.WindowHandle);
        if (string.IsNullOrWhiteSpace(target))
            target = TryGetFullPathFromTitle(source.WindowTitle);

        target = NormalizeTarget(target);
        return new ActiveTarget(target, CreateSuggestedTitle(source.WindowTitle, target));
    }

    public static bool IsSupportedWebAddress(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    public static string NormalizeTarget(string? value)
    {
        value = value?.Trim().Trim('"') ?? "";
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return value;
        if (uri.IsFile) return Uri.UnescapeDataString(uri.LocalPath);
        return uri.Scheme is "http" or "https" ? uri.AbsoluteUri : "";
    }

    private static string TryGetHwpPath()
    {
        foreach (var progId in new[] { "HWPFrame.HwpObject", "HWPFrame.HwpObject.2" })
        {
            var value = TryGetOfficePath(progId, ["XHwpDocuments", "Active_XHwpDocument", "FullName"]);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        return "";
    }

    private static string TryGetProcessCommandLinePath(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId={processId}");
            using var results = searcher.Get();
            foreach (ManagementObject item in results)
            {
                using (item)
                {
                var commandLine = item["CommandLine"]?.ToString() ?? "";
                var matches = Regex.Matches(commandLine, "\\\"(?<path>[A-Za-z]:\\\\[^\\\"]+\\.(?:hwp|hwpx|doc|docx|xls|xlsx|ppt|pptx|pdf|html?))\\\"", RegexOptions.IgnoreCase);
                var paths = matches.Select(x => x.Groups["path"].Value).Where(File.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return paths.Count == 1 ? paths[0] : "";
                }
            }
        }
        catch (Exception ex) { Logger.Error("활성 문서 경로 감지", ex); }
        return "";
    }

    private static string? TryGetOfficePath(string progId, string[] propertyPath)
    {
        object? root = null;
        var accessed = new List<object>();
        try
        {
            var type = Type.GetTypeFromProgID(progId, throwOnError: false);
            if (type is null) return null;
            var classId = type.GUID;
            if (Native.GetActiveObject(ref classId, IntPtr.Zero, out root) != 0 || root is null) return null;
            object? current = root;
            accessed.Add(root);
            foreach (var property in propertyPath)
            {
                if (current is null) return null;
                current = current.GetType().InvokeMember(property, BindingFlags.GetProperty, null, current, null);
                if (current != null && Marshal.IsComObject(current)) accessed.Add(current);
            }
            var path = current?.ToString();
            return !string.IsNullOrWhiteSpace(path) && (File.Exists(path) || Directory.Exists(path)) ? path : null;
        }
        catch { return null; }
        finally
        {
            for (var i = accessed.Count - 1; i >= 0; i--)
                try { Marshal.ReleaseComObject(accessed[i]); } catch { }
        }
    }

    private static string TryGetExplorerPath(IntPtr windowHandle)
    {
        object? shell = null;
        var release = new List<object>();
        try
        {
            var type = Type.GetTypeFromProgID("Shell.Application", throwOnError: false);
            if (type is null) return "";
            shell = Activator.CreateInstance(type);
            if (shell is null) return "";
            release.Add(shell);
            var windows = GetProperty(shell, "Windows");
            if (windows is null) return "";
            release.Add(windows);
            var count = Convert.ToInt32(GetProperty(windows, "Count"));
            for (var i = 0; i < count; i++)
            {
                var window = GetProperty(windows, "Item", i);
                if (window is null) continue;
                release.Add(window);
                if (new IntPtr(Convert.ToInt64(GetProperty(window, "HWND"))) != windowHandle) continue;
                var document = GetProperty(window, "Document");
                if (document is null) return "";
                release.Add(document);
                var focused = GetProperty(document, "FocusedItem");
                var selectedPath = focused is null ? "" : GetProperty(focused, "Path")?.ToString() ?? "";
                if (focused != null) release.Add(focused);
                if (File.Exists(selectedPath) || Directory.Exists(selectedPath)) return selectedPath;
                var folder = GetProperty(document, "Folder");
                var self = folder is null ? null : GetProperty(folder, "Self");
                var folderPath = self is null ? "" : GetProperty(self, "Path")?.ToString() ?? "";
                if (folder != null) release.Add(folder);
                if (self != null) release.Add(self);
                return Directory.Exists(folderPath) ? folderPath : "";
            }
        }
        catch { }
        finally
        {
            for (var i = release.Count - 1; i >= 0; i--)
                try { if (Marshal.IsComObject(release[i])) Marshal.ReleaseComObject(release[i]); } catch { }
        }
        return "";
    }

    private static object? GetProperty(object target, string property, params object[] arguments) =>
        target.GetType().InvokeMember(property, BindingFlags.GetProperty | BindingFlags.InvokeMethod, null, target, arguments.Length == 0 ? null : arguments);

    private static string TryGetBrowserAddress(IntPtr windowHandle)
    {
        try
        {
            var root = AutomationElement.FromHandle(windowHandle);
            var edits = root.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Edit));
            foreach (AutomationElement element in edits)
            {
                var name = element.Current.Name ?? "";
                var automationId = element.Current.AutomationId ?? "";
                var identity = $"{name} {automationId}";
                if (!Regex.IsMatch(identity, "address|주소|search|검색|omnibox", RegexOptions.IgnoreCase)) continue;
                if ((bool)element.GetCurrentPropertyValue(AutomationElement.IsPasswordProperty)) continue;
                if (!element.TryGetCurrentPattern(ValuePattern.Pattern, out var pattern)) continue;
                var value = ((ValuePattern)pattern).Current.Value;
                if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" or "file") return value;
            }
        }
        catch { }
        return "";
    }

    private static string TryGetFullPathFromTitle(string title)
    {
        var match = Regex.Match(title, @"(?<path>[A-Za-z]:\\.*?\.(?:hwp|hwpx|doc|docx|xls|xlsx|ppt|pptx|pdf|html?|txt))(?=\s*(?:[-–—]|$))", RegexOptions.IgnoreCase);
        var path = match.Success ? match.Groups["path"].Value : "";
        return File.Exists(path) ? path : "";
    }

    private static string CreateSuggestedTitle(string windowTitle, string target)
    {
        if (!string.IsNullOrWhiteSpace(target) && !IsSupportedWebAddress(target))
            return Path.GetFileNameWithoutExtension(target);
        var title = Regex.Replace(windowTitle, @"\s+[-–—]\s+(Google Chrome|Microsoft Edge|Mozilla Firefox|한글|Microsoft Word|Excel|PowerPoint).*$", "", RegexOptions.IgnoreCase).Trim();
        return title.Length <= 200 ? title : title[..200];
    }

    private static bool IsBrowser(string process) => process is "chrome" or "msedge" or "firefox" or "iexplore";

    private static string GetWindowTitle(IntPtr handle)
    {
        var length = Native.GetWindowTextLength(handle);
        if (length <= 0) return "";
        var text = new StringBuilder(length + 1);
        Native.GetWindowText(handle, text, text.Capacity);
        return text.ToString();
    }

    private static class Native
    {
        [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] internal static extern int GetWindowText(IntPtr window, StringBuilder text, int count);
        [DllImport("user32.dll")] internal static extern int GetWindowTextLength(IntPtr window);
        [DllImport("oleaut32.dll", PreserveSig = true)] internal static extern int GetActiveObject(ref Guid classId, IntPtr reserved, [MarshalAs(UnmanagedType.IUnknown)] out object? instance);
    }
}

public sealed record ActiveSource(IntPtr WindowHandle, int ProcessId, string ProcessName, string WindowTitle);
public sealed record ActiveTarget(string Target, string SuggestedTitle);
