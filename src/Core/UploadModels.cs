namespace SteamworkUploader.Models;

public enum UploadMode
{
    Create,
    Update
}

public enum WorkshopVisibility
{
    Private,
    FriendsOnly,
    Public
}

public sealed class ModMetadata
{
    public ModMetadata(string folderPath, string? previewPath, string title, IReadOnlyList<string> tags, string description)
    {
        FolderPath = folderPath;
        PreviewPath = previewPath;
        Title = title;
        Tags = tags;
        Description = description;
    }

    public string FolderPath { get; }
    public string? PreviewPath { get; }
    public string Title { get; }
    public IReadOnlyList<string> Tags { get; }
    public string Description { get; }
}

public sealed class UploadRequest
{
    public UploadRequest(
        UploadMode mode,
        ulong workshopItemId,
        string folderPath,
        string previewPath,
        string title,
        IReadOnlyList<string> tags,
        string description,
        string changeLog,
        WorkshopVisibility visibility)
    {
        Mode = mode;
        WorkshopItemId = workshopItemId;
        FolderPath = folderPath;
        PreviewPath = previewPath;
        Title = title;
        Tags = tags;
        Description = description;
        ChangeLog = changeLog;
        Visibility = visibility;
    }

    public UploadMode Mode { get; }
    public ulong WorkshopItemId { get; }
    public string FolderPath { get; }
    public string PreviewPath { get; }
    public string Title { get; }
    public IReadOnlyList<string> Tags { get; }
    public string Description { get; }
    public string ChangeLog { get; }
    public WorkshopVisibility Visibility { get; }
}

public sealed class UploadOutcome
{
    public UploadOutcome(bool success, string result, ulong fileId, bool needsWorkshopAgreement)
    {
        Success = success;
        Result = result;
        FileId = fileId;
        NeedsWorkshopAgreement = needsWorkshopAgreement;
    }

    public bool Success { get; }
    public string Result { get; }
    public ulong FileId { get; }
    public bool NeedsWorkshopAgreement { get; }
}

public sealed class WorkshopItemMetadata
{
    public WorkshopItemMetadata(
        ulong itemId,
        string url,
        string title,
        IReadOnlyList<string> tags,
        string description,
        WorkshopVisibility visibility)
    {
        ItemId = itemId;
        Url = url;
        Title = title;
        Tags = tags;
        Description = description;
        Visibility = visibility;
    }

    public ulong ItemId { get; }
    public string Url { get; }
    public string Title { get; }
    public IReadOnlyList<string> Tags { get; }
    public string Description { get; }
    public WorkshopVisibility Visibility { get; }
}

public sealed class AppSettings
{
    public string Language { get; set; } = "zh-CN";
}

public enum UploadPhase
{
    PreparingConfiguration,
    ProcessingAndUploading,
    Committing
}

public sealed class UploadProgressInfo
{
    public UploadProgressInfo(UploadPhase phase, float nativeProgress, TimeSpan elapsed)
    {
        Phase = phase;
        NativeProgress = nativeProgress;
        Elapsed = elapsed;
    }

    public UploadPhase Phase { get; }
    public float NativeProgress { get; }
    public TimeSpan Elapsed { get; }
}
