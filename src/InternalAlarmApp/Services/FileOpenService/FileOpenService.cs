using System.Diagnostics;
using InternalAlarmApp.Services;

namespace InternalAlarmApp.Services.FileOpenService;

public sealed class FileOpenService
{
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".hwp", ".hwpx", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx",
        ".pdf", ".txt", ".rtf", ".csv", ".tsv", ".htm", ".html", ".xml",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp"
    };

    public static bool IsSupportedTarget(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        if (ActiveContextService.IsSupportedWebAddress(path)) return true;
        if (Directory.Exists(path)) return true;
        return File.Exists(path) && AllowedExtensions.Contains(Path.GetExtension(path));
    }

    public bool Open(string? path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path)) { error = "연결된 파일, 폴더 또는 웹 주소가 없습니다."; return false; }
        if (!IsSupportedTarget(path)) { error = "지원되는 문서·이미지·HTML 파일, 폴더 또는 http/https 웹 주소가 아닙니다."; return false; }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { error = $"파일 또는 폴더를 열 수 없습니다: {ex.Message}"; return false; }
    }
}
