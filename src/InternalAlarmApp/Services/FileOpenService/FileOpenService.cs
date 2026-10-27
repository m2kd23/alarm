using System.Diagnostics;
using InternalAlarmApp.Models;
using InternalAlarmApp.Services;

namespace InternalAlarmApp.Services.FileOpenService;

public sealed class FileOpenService
{
    public const int MaximumAttachments = 20;

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".hwp", ".hwpx", ".doc", ".docx", ".xls", ".xlsx", ".xlsm", ".ppt", ".pptx",
        ".pdf", ".txt", ".rtf", ".csv", ".tsv", ".htm", ".html", ".xml",
        ".jpg", ".jpeg", ".png", ".gif", ".bmp"
    };

    public static bool IsSupportedTarget(string? target) => TryInferAttachment(target, out var attachment, out _) && IsValidForSaving(attachment, out _);

    public static bool TryInferAttachment(string? target, out AlarmAttachment attachment, out string error)
    {
        attachment = new AlarmAttachment { Target = target?.Trim() ?? "" };
        if (string.IsNullOrWhiteSpace(target)) { error = "관련 자료가 비어 있습니다."; return false; }
        if (ActiveContextService.IsSupportedWebAddress(target)) attachment.Kind = AttachmentKind.Web;
        else attachment.Kind = Directory.Exists(target) ? AttachmentKind.Folder : AttachmentKind.File;
        attachment.DisplayName = GetDefaultDisplayName(attachment);
        error = "";
        return true;
    }

    public static bool IsValidForSaving(AlarmAttachment? attachment, out string error)
    {
        error = "";
        if (attachment == null || string.IsNullOrWhiteSpace(attachment.Target)) { error = "관련 자료가 비어 있습니다."; return false; }
        var target = attachment.Target.Trim();
        switch (attachment.Kind)
        {
            case AttachmentKind.Web:
                if (ActiveContextService.IsSupportedWebAddress(target)) return true;
                error = "웹 주소는 http 또는 https 형식이어야 합니다.";
                return false;
            case AttachmentKind.File:
                if (!IsAbsoluteWindowsPath(target)) { error = @"파일은 드라이브 또는 \\서버\공유 경로로 입력해 주세요."; return false; }
                if (AllowedExtensions.Contains(Path.GetExtension(target))) return true;
                error = "지원되는 문서·이미지·HTML 파일 형식이 아닙니다.";
                return false;
            case AttachmentKind.Folder:
                if (IsAbsoluteWindowsPath(target)) return true;
                error = @"폴더는 드라이브 또는 \\서버\공유 경로로 입력해 주세요.";
                return false;
            default:
                error = "지원되지 않는 관련 자료 유형입니다.";
                return false;
        }
    }

    public bool Open(string? target, out string error)
    {
        if (!TryInferAttachment(target, out var attachment, out error)) return false;
        return Open(attachment, out error);
    }

    public bool Open(AlarmAttachment attachment, out string error)
    {
        if (!IsValidForSaving(attachment, out error)) return false;
        var target = attachment.Target.Trim();
        if (attachment.Kind == AttachmentKind.File && !File.Exists(target)) { error = "연결된 파일을 찾을 수 없습니다."; return false; }
        if (attachment.Kind == AttachmentKind.Folder && !Directory.Exists(target)) { error = "연결된 폴더에 접근할 수 없습니다."; return false; }
        try
        {
            Process.Start(new ProcessStartInfo { FileName = target, UseShellExecute = true });
            error = "";
            return true;
        }
        catch (Exception ex)
        {
            error = $"파일 또는 폴더를 열 수 없습니다: {ex.Message}";
            return false;
        }
    }

    public Task<OpenTargetsResult> OpenAllAsync(IEnumerable<AlarmAttachment> attachments) =>
        Task.Run(() => OpenAll(attachments));

    public OpenTargetsResult OpenAll(IEnumerable<AlarmAttachment> attachments)
    {
        var items = attachments.Take(MaximumAttachments).ToList();
        var opened = new List<AlarmAttachment>();
        var failures = new List<string>();
        foreach (var attachment in items)
        {
            if (Open(attachment, out var error)) opened.Add(attachment);
            else failures.Add($"{attachment.DisplayName}: {error}");
        }
        return new OpenTargetsResult(opened, failures, attachments.Skip(MaximumAttachments).Any());
    }

    public static string GetDefaultDisplayName(AlarmAttachment attachment)
    {
        var target = attachment.Target.Trim();
        if (attachment.Kind == AttachmentKind.Web && Uri.TryCreate(target, UriKind.Absolute, out var uri)) return uri.Host;
        var name = Path.GetFileName(target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(name) ? target : name;
    }

    public static string NormalizedKey(AlarmAttachment attachment)
    {
        var target = attachment.Target.Trim();
        if (attachment.Kind == AttachmentKind.Web && Uri.TryCreate(target, UriKind.Absolute, out var uri)) target = uri.GetComponents(UriComponents.HttpRequestUrl, UriFormat.SafeUnescaped).TrimEnd('/');
        else target = target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return $"{(int)attachment.Kind}:{target}";
    }

    private static bool IsAbsoluteWindowsPath(string value)
    {
        try { return Path.IsPathFullyQualified(value) && !value.StartsWith("\\\\?\\", StringComparison.Ordinal); }
        catch { return false; }
    }
}

public sealed record OpenTargetsResult(IReadOnlyList<AlarmAttachment> Opened, IReadOnlyList<string> Failures, bool LimitReached);
