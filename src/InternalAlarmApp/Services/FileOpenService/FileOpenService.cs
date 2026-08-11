using System.Diagnostics;

namespace InternalAlarmApp.Services.FileOpenService;

public sealed class FileOpenService
{
    public bool Open(string? path, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path)) { error = "연결된 파일 또는 폴더가 없습니다."; return false; }
        if (!File.Exists(path) && !Directory.Exists(path)) { error = "연결된 파일 또는 폴더를 찾을 수 없습니다."; return false; }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            return true;
        }
        catch (Exception ex) { error = $"파일 또는 폴더를 열 수 없습니다: {ex.Message}"; return false; }
    }
}
