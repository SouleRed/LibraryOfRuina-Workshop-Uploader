using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using SteamworkUploader.Models;
using SteamworkUploader.Services;

namespace SteamworkUploader;

public partial class MainWindow
{
    private void InitializeSteam()
    {
        try
        {
            _steamSession.Initialize(SteamAppId);
            UpdateSteamStatus();
            _ = LoadSteamAvatarAsync();
            AddLog(LocalizationService.Format("SteamInitSuccess", SteamAppId));
        }
        catch (Exception ex)
        {
            _avatarLoadVersion++;
            SteamAvatarImage.Source = null;
            UpdateSteamStatus();
            string message = LocalizationService.Format("SteamInitFailed", ex.Message);
            SetStatusText(message, true);
            AddLog(message);
        }
    }

    private void UpdateSteamStatus()
    {
        bool connected = _steamSession.IsInitialized && _steamSession.IsLoggedOn;
        SteamStatusDot.Fill = (Brush)FindResource(connected ? "SuccessBrush" : "DangerBrush");
        SteamStatusText.Text = connected
            ? LocalizationService.Format("ConnectedFormat", _steamSession.UserName)
            : LocalizationService.Get("Disconnected");
    }

    private async Task LoadSteamAvatarAsync()
    {
        int version = ++_avatarLoadVersion;
        SteamAvatarImage.Source = null;
        try
        {
            Steamworks.Data.Image? avatar = await _steamSession.GetAvatarAsync();
            if (version == _avatarLoadVersion && avatar is not null)
                SteamAvatarImage.Source = CreateAvatarBitmap(avatar.Value);
        }
        catch
        {
            if (version == _avatarLoadVersion)
                SteamAvatarImage.Source = null;
        }
    }

    private static BitmapSource CreateAvatarBitmap(Steamworks.Data.Image image)
    {
        int width = checked((int)image.Width);
        int height = checked((int)image.Height);
        int stride = checked(width * 4);
        int requiredLength = checked(stride * height);
        if (width == 0 || height == 0 || image.Data is null || image.Data.Length < requiredLength)
            throw new InvalidDataException("Steam returned invalid avatar data.");

        byte[] pixels = new byte[requiredLength];
        Buffer.BlockCopy(image.Data, 0, pixels, 0, requiredLength);
        for (int index = 0; index < pixels.Length; index += 4)
            (pixels[index], pixels[index + 2]) = (pixels[index + 2], pixels[index]);

        BitmapSource bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, stride);
        bitmap.Freeze();
        return bitmap;
    }

    private bool EnsureSteamConnected()
    {
        if (_steamSession.IsInitialized && _steamSession.IsLoggedOn)
            return true;
        SetStatus("SteamLoginRequired", true);
        return false;
    }

    private void ReconnectButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        ReconnectButton.IsEnabled = false;
        try
        {
            InitializeSteam();
        }
        finally
        {
            ReconnectButton.IsEnabled = true;
        }
    }

    private bool TryBuildRequest(out UploadRequest request)
    {
        ulong itemId = 0;
        if (_mode == UploadMode.Update)
            WorkshopItemIdParser.TryParse(WorkshopIdTextBox.Text, out itemId);

        request = new UploadRequest(
            _mode,
            itemId,
            _selectedFolder,
            _previewPath,
            TitleTextBox.Text.Trim(),
            TagParser.Parse(TagsTextBox.Text),
            DescriptionTextBox.Text,
            ChangeLogTextBox.Text,
            _visibility);

        IReadOnlyList<string> failures = UploadRequestValidator.Validate(request);
        if (failures.Count == 0)
            return true;

        SetStatusText(string.Join("  ·  ", failures.Select(LocalizationService.Get)), true);
        return false;
    }

    private async void ActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        ResetUploadProgress();
        if (!TryBuildRequest(out UploadRequest request))
            return;

        LogUploadSummary(request);
        if (!EnsureSteamConnected())
            return;

        _isUploading = true;
        RefreshInteractiveState();
        ActionButton.Content = LocalizationService.Get("Uploading");
        _lastLoggedUploadPhase = null;
        _lastUploadProgressLog = DateTime.MinValue;
        _lastLoggedTransferBucket = -1;
        BeginUploadProgress();
        SetStatus("Uploading", false);
        AddLog(LocalizationService.Get("Uploading"));

        Progress<UploadProgressInfo> progress = new(HandleUploadProgress);
        try
        {
            UploadOutcome outcome = await _uploadService.SubmitAsync(request, progress);
            ulong fileId = outcome.FileId != 0 ? outcome.FileId : request.WorkshopItemId;
            if (!outcome.Success)
            {
                string failed = LocalizationService.Format("UploadFailed", outcome.Result);
                SetStatusText(failed, true);
                AddLog(failed);
                return;
            }

            string successKey = request.Mode == UploadMode.Create ? "UploadSucceeded" : "UpdateSucceeded";
            string succeeded = LocalizationService.Format(successKey, fileId);
            SetStatusText(succeeded, false);
            AddLog(succeeded);
            CompleteUploadProgress();

            if (fileId != 0)
            {
                WorkshopIdTextBox.Text = WorkshopItemIdParser.BuildUrl(fileId);
                if (request.Mode == UploadMode.Create)
                    _createdItemId = fileId;
                RefreshModeControls();
            }

            if (outcome.NeedsWorkshopAgreement)
            {
                MessageBoxResult agreement = MessageBox.Show(LocalizationService.Get("AgreementRequired"), LocalizationService.Get("NoticeTitle"), MessageBoxButton.YesNo, MessageBoxImage.Information);
                if (agreement == MessageBoxResult.Yes)
                    OpenUrl("https://steamcommunity.com/sharedfiles/workshoplegalagreement");
            }
        }
        catch (Exception ex)
        {
            string message = LocalizationService.Format("UploadFailed", ex.Message);
            SetStatusText(message, true);
            AddLog(message);
        }
        finally
        {
            _isUploading = false;
            RefreshInteractiveState();
        }
    }

    private void HandleUploadProgress(UploadProgressInfo progress)
    {
        double? transferProgress = progress.Phase == UploadPhase.ProcessingAndUploading && progress.NativeProgress > 0.2f
            ? Math.Max(0, Math.Min(1, (progress.NativeProgress - 0.2f) / 0.7f))
            : null;
        if (progress.Phase == UploadPhase.Committing)
            UpdateUploadProgress(1);
        else if (transferProgress.HasValue)
            UpdateUploadProgress(transferProgress.Value);

        int transferBucket = transferProgress.HasValue ? (int)(transferProgress.Value * 10) : -1;
        bool phaseChanged = _lastLoggedUploadPhase != progress.Phase;
        bool transferAdvanced = transferBucket > _lastLoggedTransferBucket;
        bool heartbeatDue = DateTime.Now - _lastUploadProgressLog >= TimeSpan.FromSeconds(15);
        if (!phaseChanged && !transferAdvanced && !heartbeatDue)
            return;

        _lastLoggedUploadPhase = progress.Phase;
        _lastLoggedTransferBucket = Math.Max(_lastLoggedTransferBucket, transferBucket);
        _lastUploadProgressLog = DateTime.Now;
        string log = transferProgress.HasValue
            ? LocalizationService.Format("UploadTransferLog", GetUploadLogPhaseText(progress.Phase), progress.Elapsed.TotalSeconds, transferProgress.Value)
            : LocalizationService.Format("UploadStageLog", GetUploadLogPhaseText(progress.Phase), progress.Elapsed.TotalSeconds);
        AddLog(log);
    }

    private void LogUploadSummary(UploadRequest request)
    {
        try
        {
            FileInfo[] files = new DirectoryInfo(request.FolderPath).EnumerateFiles("*", SearchOption.AllDirectories).ToArray();
            long contentBytes = files.Sum(file => file.Length);
            long previewBytes = new FileInfo(request.PreviewPath).Length;
            string target = request.Mode == UploadMode.Update ? request.WorkshopItemId.ToString() : LocalizationService.Get("NewWorkshopItem");
            AddLog(LocalizationService.Format("UploadSummary", target, files.Length, FormatBytes(contentBytes), FormatBytes(previewBytes)));
        }
        catch (Exception ex)
        {
            AddLog(LocalizationService.Format("UploadSummaryFailed", ex.Message));
        }
    }

    private static string FormatBytes(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }
        return $"{value:0.##} {units[unit]}";
    }

    private async void WorkshopActionButton_Click(object sender, RoutedEventArgs e)
    {
        if (IsBusy)
            return;

        if (_mode == UploadMode.Create)
        {
            if (_createdItemId != 0)
                OpenWorkshopInSteam(_createdItemId);
            return;
        }

        if (!WorkshopItemIdParser.TryParse(WorkshopIdTextBox.Text, out ulong itemId))
        {
            SetStatus("ValidationWorkshopId", true);
            return;
        }

        if (!EnsureSteamConnected())
            return;

        _isFetchingWorkshop = true;
        RefreshInteractiveState();
        SetStatus("FetchingWorkshopInfo", false);
        AddLog(LocalizationService.Get("FetchingWorkshopInfo"));
        try
        {
            WorkshopItemMetadata metadata = await _workshopMetadataService.GetAsync(itemId);
            WorkshopIdTextBox.Text = metadata.Url;
            TitleTextBox.Text = metadata.Title;
            TagsTextBox.Text = string.Join(" | ", metadata.Tags);
            DescriptionTextBox.Text = metadata.Description;
            SelectVisibility(metadata.Visibility, false);
            ResetUploadProgress();
            SetStatus("WorkshopInfoFetchedStatus", false);
            AddLog(LocalizationService.Format("WorkshopInfoFetched", metadata.Title));
        }
        catch (Exception ex)
        {
            string message = LocalizationService.Format("WorkshopInfoFetchFailed", ex.Message);
            SetStatusText(message, true);
            AddLog(message);
        }
        finally
        {
            _isFetchingWorkshop = false;
            RefreshInteractiveState();
        }
    }
}
