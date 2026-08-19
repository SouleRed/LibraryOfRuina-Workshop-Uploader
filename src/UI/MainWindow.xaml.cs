using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using SteamworkUploader.Models;
using SteamworkUploader.Services;
using Forms = System.Windows.Forms;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace SteamworkUploader;

public partial class MainWindow : Window
{
    private const uint SteamAppId = 1256670;
    private const int UploadProgressSegmentCount = 24;
    private const double IdealWindowWidth = 940;
    private const double IdealWindowHeight = 850;
    private const double WorkAreaMargin = 32;
    private const uint SwpNoZOrder = 0x0004;
    private const uint SwpNoActivate = 0x0010;

    private readonly SteamSession _steamSession = new();
    private readonly WorkshopUploadService _uploadService = new();
    private readonly WorkshopMetadataService _workshopMetadataService = new();
    private readonly ModMetadataReader _metadataReader = new();
    private readonly SettingsService _settingsService = new();
    private readonly AppSettings _settings;

    private UploadMode _mode = UploadMode.Update;
    private WorkshopVisibility _visibility = WorkshopVisibility.Public;
    private string _selectedFolder = string.Empty;
    private string _previewPath = string.Empty;
    private ulong _createdItemId;
    private bool _isUploading;
    private bool _isFetchingWorkshop;
    private bool _isUpdatingFolderText;
    private string? _statusResourceKey = "Ready";
    private bool _statusIsError;
    private int _avatarLoadVersion;
    private UploadPhase? _lastLoggedUploadPhase;
    private DateTime _lastUploadProgressLog;
    private int _lastLoggedTransferBucket = -1;
    private bool _uploadProgressStarted;
    private bool _uploadProgressCompleted;
    private double _uploadContentProgress;
    private readonly List<Rectangle> _uploadProgressSegments = [];

    public MainWindow()
    {
        InitializeComponent();
        InitializeScaledContent();
        WindowStartupLocation = WindowStartupLocation.Manual;
        _settings = _settingsService.Load();

        LocalizationService.LanguageChanged += LocalizationService_LanguageChanged;
        LocalizationService.SetLanguage(_settings.Language);
        _settings.Language = LocalizationService.CurrentLanguage;
        UpdateLanguageButtonStyles(_settings.Language);
        UpdateVisibilityButtonStyles(_visibility);
        SetMode(UploadMode.Update, false);
        InitializeUploadProgressSegments();
        ResetUploadProgress();
        TitleTextBox.TextChanged += UploadInput_TextChanged;
        TagsTextBox.TextChanged += UploadInput_TextChanged;
        DescriptionTextBox.TextChanged += UploadInput_TextChanged;
        ChangeLogTextBox.TextChanged += UploadInput_TextChanged;

        SourceInitialized += MainWindow_SourceInitialized;
        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
        _steamSession.DiagnosticReceived += SteamSession_DiagnosticReceived;
    }

    private bool IsBusy => _isUploading || _isFetchingWorkshop;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SetWindowPos(
        IntPtr hWnd,
        IntPtr hWndInsertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    private void InitializeScaledContent()
    {
        if (Content is not FrameworkElement designRoot)
            return;

        Content = null;
        designRoot.Width = IdealWindowWidth;
        designRoot.Height = IdealWindowHeight;
        designRoot.HorizontalAlignment = HorizontalAlignment.Center;
        designRoot.VerticalAlignment = VerticalAlignment.Center;

        Content = new Viewbox
        {
            Stretch = Stretch.Uniform,
            StretchDirection = StretchDirection.DownOnly,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            Child = designRoot
        };
    }

    private void MainWindow_SourceInitialized(object? sender, EventArgs e) => ApplyScaledWindowBounds();

    private void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        InitializeSteam();
    }

    private void ApplyScaledWindowBounds()
    {
        IntPtr handle = new WindowInteropHelper(this).Handle;
        if (handle == IntPtr.Zero)
            return;

        Forms.Screen screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
        DpiScale dpi = VisualTreeHelper.GetDpi(this);
        double workingWidth = screen.WorkingArea.Width / dpi.DpiScaleX;
        double workingHeight = screen.WorkingArea.Height / dpi.DpiScaleY;
        double availableWidth = Math.Max(1, workingWidth - WorkAreaMargin);
        double availableHeight = Math.Max(1, workingHeight - WorkAreaMargin);
        double scale = Math.Min(1, Math.Min(availableWidth / IdealWindowWidth, availableHeight / IdealWindowHeight));
        double targetWidth = IdealWindowWidth * scale;
        double targetHeight = IdealWindowHeight * scale;

        MinWidth = targetWidth;
        MinHeight = targetHeight;
        Width = targetWidth;
        Height = targetHeight;

        int widthPixels = (int)Math.Round(targetWidth * dpi.DpiScaleX);
        int heightPixels = (int)Math.Round(targetHeight * dpi.DpiScaleY);
        int left = screen.WorkingArea.Left + Math.Max(0, (screen.WorkingArea.Width - widthPixels) / 2);
        int top = screen.WorkingArea.Top + Math.Max(0, (screen.WorkingArea.Height - heightPixels) / 2);

        SetWindowPos(handle, IntPtr.Zero, left, top, widthPixels, heightPixels, SwpNoZOrder | SwpNoActivate);
    }

    private void MainWindow_Closing(object? sender, CancelEventArgs e)
    {
        if (IsBusy)
        {
            e.Cancel = true;
            MessageBox.Show(LocalizationService.Get("CloseDuringUpload"), LocalizationService.Get("NoticeTitle"), MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        LocalizationService.LanguageChanged -= LocalizationService_LanguageChanged;
        _steamSession.DiagnosticReceived -= SteamSession_DiagnosticReceived;
        _avatarLoadVersion++;
        SaveSettings();
        _steamSession.Dispose();
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    private static void OpenUrl(string url) => Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });

    private static void OpenWorkshopInSteam(ulong itemId) =>
        Process.Start(new ProcessStartInfo($"steam://url/CommunityFilePage/{itemId}") { UseShellExecute = true });

    private void AddLog(string message)
    {
        string entry = $"[{DateTime.Now:HH:mm:ss}] {message}";
        LogTextBox.AppendText((LogTextBox.Text.Length == 0 ? string.Empty : Environment.NewLine) + entry);
        LogTextBox.ScrollToEnd();
    }

    private void SetStatus(string resourceKey, bool isError)
    {
        _statusResourceKey = resourceKey;
        _statusIsError = isError;
        ValidationText.Text = LocalizationService.Get(resourceKey);
        ValidationText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "GoldLightBrush");
    }

    private void SetStatusText(string text, bool isError)
    {
        _statusResourceKey = null;
        _statusIsError = isError;
        ValidationText.Text = text;
        ValidationText.Foreground = (Brush)FindResource(isError ? "DangerBrush" : "GoldLightBrush");
    }

    private void ResetUploadProgress()
    {
        _uploadProgressStarted = false;
        _uploadProgressCompleted = false;
        _uploadContentProgress = 0;
        RenderUploadProgress();
    }

    private void InitializeUploadProgressSegments()
    {
        Brush inactive = (Brush)FindResource("ProgressLineInactiveBrush");
        for (int index = 0; index < UploadProgressSegmentCount; index++)
        {
            Rectangle segment = new()
            {
                Height = 3,
                Margin = new Thickness(1, 0, 1, 0),
                Fill = inactive
            };
            _uploadProgressSegments.Add(segment);
            ProgressSegmentsPanel.Children.Add(segment);
        }
    }

    private void BeginUploadProgress()
    {
        _uploadProgressStarted = true;
        _uploadProgressCompleted = false;
        _uploadContentProgress = 0;
        RenderUploadProgress();
    }

    private void UpdateUploadProgress(double progress)
    {
        _uploadProgressStarted = true;
        _uploadContentProgress = Math.Max(_uploadContentProgress, Math.Max(0, Math.Min(1, progress)));
        RenderUploadProgress();
    }

    private void CompleteUploadProgress()
    {
        _uploadProgressStarted = true;
        _uploadProgressCompleted = true;
        _uploadContentProgress = 1;
        RenderUploadProgress();
    }

    private void RenderUploadProgress()
    {
        ProgressStart.Style = (Style)FindResource(_uploadProgressStarted ? "ProgressStepActiveStyle" : "ProgressStepInactiveStyle");
        ProgressComplete.Style = (Style)FindResource(_uploadProgressCompleted ? "ProgressStepActiveStyle" : "ProgressStepInactiveStyle");
        Brush active = (Brush)FindResource("GoldBrush");
        Brush inactive = (Brush)FindResource("ProgressLineInactiveBrush");
        double illuminated = _uploadProgressCompleted ? _uploadProgressSegments.Count : _uploadContentProgress * _uploadProgressSegments.Count;
        for (int index = 0; index < _uploadProgressSegments.Count; index++)
        {
            double segmentProgress = Math.Max(0, Math.Min(1, illuminated - index));
            Rectangle segment = _uploadProgressSegments[index];
            segment.Fill = segmentProgress > 0 ? active : inactive;
            segment.Opacity = segmentProgress > 0 ? 0.3 + segmentProgress * 0.7 : 1;
        }

        ProgressText.Text = !_uploadProgressStarted
            ? LocalizationService.Get("ProgressWaiting")
            : _uploadProgressCompleted
                ? LocalizationService.Get("ProgressComplete")
                : LocalizationService.Format("ProgressFormat", LocalizationService.Get("ProgressTransfer"), _uploadContentProgress);
    }

    private void UploadInput_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!IsBusy)
            ResetUploadProgress();
    }

    private static string GetUploadLogPhaseText(UploadPhase phase) => LocalizationService.Get(phase switch
    {
        UploadPhase.PreparingConfiguration => "UploadLogPreparingConfiguration",
        UploadPhase.ProcessingAndUploading => "ProgressTransfer",
        _ => "UploadLogCommitting"
    });

    private void SteamSession_DiagnosticReceived(string message)
    {
        if (Dispatcher.CheckAccess())
            AddLog(message);
        else
            Dispatcher.BeginInvoke(new Action(() => AddLog(message)));
    }

    private void SaveSettings()
    {
        try
        {
            _settingsService.Save(_settings);
        }
        catch (Exception ex)
        {
            AddLog(ex.Message);
        }
    }

    private void SetMode(UploadMode mode, bool writeLog = true)
    {
        if (IsBusy)
            return;

        _mode = mode;
        _createdItemId = 0;
        WorkshopIdTextBox.Clear();
        RefreshModeControls();
        ResetUploadProgress();
        if (writeLog)
            AddLog(LocalizationService.Get(mode == UploadMode.Create ? "ModeCreateSelected" : "ModeUpdateSelected"));
    }

    private void RefreshModeControls()
    {
        bool create = _mode == UploadMode.Create;
        CreateModeButton.Style = (Style)FindResource(create ? "SelectedModeButtonStyle" : "ModeButtonStyle");
        UpdateModeButton.Style = (Style)FindResource(create ? "ModeButtonStyle" : "SelectedModeButtonStyle");
        WorkshopIdLabelText.Text = LocalizationService.Get(create ? "WorkshopIdLabelCreate" : "WorkshopIdLabelUpdate");
        WorkshopHintText.Text = LocalizationService.Get(create ? "WorkshopAutoAssignedHint" : "WorkshopInputHint");
        WorkshopIdTextBox.Foreground = (Brush)FindResource(create ? "GoldDarkBrush" : "TextBrush");
        WorkshopHintText.Foreground = (Brush)FindResource(create ? "GoldDarkBrush" : "TextMutedBrush");
        WorkshopIdTextBox.IsReadOnly = create;
        WorkshopIdTextBox.IsTabStop = !create;
        WorkshopIdTextBox.Focusable = !create;
        WorkshopIdTextBox.IsHitTestVisible = !create;
        WorkshopIdTextBox.IsEnabled = !IsBusy && !create;

        string actionKey = _isFetchingWorkshop
            ? "FetchingWorkshopInfoButton"
            : create
                ? _createdItemId == 0 ? "WorkshopUnbound" : "OpenWorkshop"
                : "FetchWorkshopInfo";
        WorkshopActionButton.Content = LocalizationService.Get(actionKey);
        WorkshopActionButton.IsEnabled = !IsBusy && (create
            ? _createdItemId != 0
            : WorkshopItemIdParser.TryParse(WorkshopIdTextBox.Text, out _));
        ActionButton.Content = LocalizationService.Get(create ? "Upload" : "Update");
        RefreshWorkshopHint();
    }

    private void RefreshInteractiveState()
    {
        bool enabled = !IsBusy;
        ActionButton.IsEnabled = enabled;
        CreateModeButton.IsEnabled = enabled;
        UpdateModeButton.IsEnabled = enabled;
        BrowseButton.IsEnabled = enabled;
        ReconnectButton.IsEnabled = enabled;
        LanguageZhButton.IsEnabled = enabled;
        LanguageEnButton.IsEnabled = enabled;
        LanguageJaButton.IsEnabled = enabled;
        LanguageKoButton.IsEnabled = enabled;
        VisibilityPublicButton.IsEnabled = enabled;
        VisibilityFriendsButton.IsEnabled = enabled;
        VisibilityPrivateButton.IsEnabled = enabled;
        TitleTextBox.IsEnabled = enabled;
        TagsTextBox.IsEnabled = enabled;
        DescriptionTextBox.IsEnabled = enabled;
        ChangeLogTextBox.IsEnabled = enabled;
        FolderPathTextBox.IsEnabled = enabled;
        DropZoneBorder.IsEnabled = enabled;
        RefreshModeControls();
    }

    private void LocalizationService_LanguageChanged(object? sender, EventArgs e)
    {
        UpdateVisibilityButtonStyles(_visibility);
        UpdateLanguageButtonStyles(_settings.Language);
        RefreshModeControls();
        UpdateSteamStatus();
        RenderUploadProgress();
        if (_statusResourceKey is not null)
            SetStatus(_statusResourceKey, _statusIsError);
    }

    private void ChangeLanguage(string language)
    {
        if (IsBusy)
            return;

        LocalizationService.SetLanguage(language);
        _settings.Language = LocalizationService.CurrentLanguage;
        UpdateLanguageButtonStyles(_settings.Language);
        SaveSettings();
    }

    private void CreateModeButton_Click(object sender, RoutedEventArgs e) => SetMode(UploadMode.Create);

    private void UpdateModeButton_Click(object sender, RoutedEventArgs e) => SetMode(UploadMode.Update);

    private void VisibilityPublicButton_Click(object sender, RoutedEventArgs e) => SelectVisibility(WorkshopVisibility.Public);

    private void VisibilityFriendsButton_Click(object sender, RoutedEventArgs e) => SelectVisibility(WorkshopVisibility.FriendsOnly);

    private void VisibilityPrivateButton_Click(object sender, RoutedEventArgs e) => SelectVisibility(WorkshopVisibility.Private);

    private void LanguageZhButton_Click(object sender, RoutedEventArgs e) => ChangeLanguage("zh-CN");

    private void LanguageEnButton_Click(object sender, RoutedEventArgs e) => ChangeLanguage("en-US");

    private void LanguageJaButton_Click(object sender, RoutedEventArgs e) => ChangeLanguage("ja-JP");

    private void LanguageKoButton_Click(object sender, RoutedEventArgs e) => ChangeLanguage("ko-KR");

    private void SelectVisibility(WorkshopVisibility visibility, bool writeLog = true)
    {
        if (IsBusy && writeLog)
            return;

        bool changed = _visibility != visibility;
        _visibility = visibility;
        UpdateVisibilityButtonStyles(visibility);
        if (changed && writeLog)
            ResetUploadProgress();
        if (!writeLog)
            return;

        string visibilityKey = visibility switch
        {
            WorkshopVisibility.Public => "VisibilityPublic",
            WorkshopVisibility.FriendsOnly => "VisibilityFriends",
            _ => "VisibilityPrivate"
        };
        AddLog(LocalizationService.Format("VisibilityChanged", LocalizationService.Get(visibilityKey)));
    }

    private void UpdateVisibilityButtonStyles(WorkshopVisibility selected)
    {
        VisibilityPublicButton.Style = (Style)FindResource(selected == WorkshopVisibility.Public ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
        VisibilityFriendsButton.Style = (Style)FindResource(selected == WorkshopVisibility.FriendsOnly ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
        VisibilityPrivateButton.Style = (Style)FindResource(selected == WorkshopVisibility.Private ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
    }

    private void UpdateLanguageButtonStyles(string language)
    {
        LanguageZhButton.Style = (Style)FindResource(language == "zh-CN" ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
        LanguageEnButton.Style = (Style)FindResource(language == "en-US" ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
        LanguageJaButton.Style = (Style)FindResource(language == "ja-JP" ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
        LanguageKoButton.Style = (Style)FindResource(language == "ko-KR" ? "RailOptionSelectedButtonStyle" : "RailOptionButtonStyle");
    }

    private void WorkshopIdTextBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (WorkshopHintText is not null && WorkshopActionButton is not null)
            RefreshModeControls();
        if (!IsBusy)
            ResetUploadProgress();
    }

    private void WorkshopIdTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshWorkshopHint();

    private void WorkshopIdTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshWorkshopHint();

    private void RefreshWorkshopHint()
    {
        if (WorkshopHintText is null)
            return;

        WorkshopHintText.Visibility = !WorkshopIdTextBox.IsKeyboardFocusWithin && string.IsNullOrWhiteSpace(WorkshopIdTextBox.Text)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }
}
