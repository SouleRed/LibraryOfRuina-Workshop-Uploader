using System.Diagnostics;
using Steamworks;
using Steamworks.Ugc;
using SteamworkUploader.Models;

namespace SteamworkUploader.Services;

public sealed class SteamSession : IDisposable
{
    public event Action<string>? DiagnosticReceived;

    public bool IsInitialized { get; private set; }

    public bool IsLoggedOn => IsInitialized && SteamClient.IsLoggedOn;

    public string UserName => IsInitialized ? SteamClient.Name : string.Empty;

    public Task<Steamworks.Data.Image?> GetAvatarAsync() => IsLoggedOn
        ? SteamFriends.GetMediumAvatarAsync(SteamClient.SteamId)
        : Task.FromResult<Steamworks.Data.Image?>(null);

    public void Initialize(uint appId)
    {
        if (appId == 0)
            throw new ArgumentOutOfRangeException(nameof(appId), "Steam App ID cannot be zero.");

        Shutdown();
        AttachDiagnostics();
        try
        {
            SteamClient.Init(appId, true);
            if (!SteamClient.IsValid)
                throw new InvalidOperationException("Steam client initialization returned an invalid state.");
            IsInitialized = true;
        }
        catch
        {
            IsInitialized = false;
            TryShutdownClient();
            DetachDiagnostics();
            throw;
        }
    }

    public void Shutdown()
    {
        if (!IsInitialized)
            return;
        TryShutdownClient();
        IsInitialized = false;
        DetachDiagnostics();
    }

    public void Dispose() => Shutdown();

    private static void TryShutdownClient()
    {
        try
        {
            SteamClient.Shutdown();
        }
        catch
        {
            // Initialization failure and process shutdown only require best-effort cleanup.
        }
    }

    private void AttachDiagnostics()
    {
        Dispatch.OnException += HandleDispatchException;
        Dispatch.OnDebugCallback += HandleDebugCallback;
    }

    private void DetachDiagnostics()
    {
        Dispatch.OnException -= HandleDispatchException;
        Dispatch.OnDebugCallback -= HandleDebugCallback;
    }

    private void HandleDispatchException(Exception exception) => DiagnosticReceived?.Invoke($"Steam callback exception: {exception}");

    private void HandleDebugCallback(CallbackType type, string message, bool isServer)
    {
        string callback = type.ToString();
        if (callback.Contains("SubmitItemUpdate") || callback.Contains("CreateItem"))
            DiagnosticReceived?.Invoke($"Steam callback [{callback}]: {message}");
    }
}

public sealed class WorkshopUploadService
{
    public async Task<UploadOutcome> SubmitAsync(UploadRequest request, IProgress<UploadProgressInfo>? progress = null)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        Editor editor = request.Mode == UploadMode.Create ? Editor.NewCommunityFile : new Editor(request.WorkshopItemId);
        editor = editor
            .WithTitle(request.Title)
            .WithDescription(request.Description)
            .WithContent(request.FolderPath)
            .WithPreviewFile(request.PreviewPath);

        if (!string.IsNullOrWhiteSpace(request.ChangeLog))
            editor = editor.WithChangeLog(request.ChangeLog);

        editor = request.Visibility switch
        {
            WorkshopVisibility.Public => editor.WithPublicVisibility(),
            WorkshopVisibility.FriendsOnly => editor.WithFriendsOnlyVisibility(),
            _ => editor.WithPrivateVisibility()
        };
        foreach (string tag in request.Tags)
            editor = editor.WithTag(tag);

        Report(progress, UploadPhase.PreparingConfiguration, 0, stopwatch.Elapsed);
        Progress<float> nativeProgress = new(value => ReportNativeProgress(progress, value, stopwatch.Elapsed));
        PublishResult result = await editor.SubmitAsync(nativeProgress);
        Report(progress, UploadPhase.Committing, 1, stopwatch.Elapsed);
        return new UploadOutcome(result.Success, result.Result.ToString(), result.FileId.Value, result.NeedsWorkshopAgreement);
    }

    private static void ReportNativeProgress(IProgress<UploadProgressInfo>? progress, float nativeProgress, TimeSpan elapsed)
    {
        const float epsilon = 0.001f;
        if (nativeProgress <= 0.1f + epsilon)
        {
            Report(progress, UploadPhase.PreparingConfiguration, nativeProgress, elapsed);
            return;
        }

        if (nativeProgress <= 0.2f + epsilon)
        {
            Report(progress, UploadPhase.ProcessingAndUploading, nativeProgress, elapsed);
            return;
        }

        if (nativeProgress < 1f - epsilon)
        {
            Report(progress, UploadPhase.ProcessingAndUploading, nativeProgress, elapsed);
            return;
        }

        Report(progress, UploadPhase.Committing, nativeProgress, elapsed);
    }

    private static void Report(IProgress<UploadProgressInfo>? progress, UploadPhase phase, float native, TimeSpan elapsed) =>
        progress?.Report(new UploadProgressInfo(phase, native, elapsed));
}

public sealed class WorkshopMetadataService
{
    public async Task<WorkshopItemMetadata> GetAsync(ulong itemId)
    {
        Item? result = await Item.GetAsync(itemId);
        if (!result.HasValue)
            throw new InvalidOperationException("Steam Workshop returned no item data.");

        Item item = result.Value;
        WorkshopVisibility visibility = WorkshopVisibility.Private;
        if (item.IsPublic)
            visibility = WorkshopVisibility.Public;
        else if (item.IsFriendsOnly)
            visibility = WorkshopVisibility.FriendsOnly;

        return new WorkshopItemMetadata(
            item.Id.Value,
            WorkshopItemIdParser.BuildUrl(item.Id.Value),
            item.Title ?? string.Empty,
            item.Tags ?? [],
            item.Description ?? string.Empty,
            visibility);
    }
}
