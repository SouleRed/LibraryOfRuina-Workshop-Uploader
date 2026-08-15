using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using SteamworkUploader.Models;
using SteamworkUploader.Services;
using Forms = System.Windows.Forms;

namespace SteamworkUploader;

public partial class MainWindow
{
    private void LoadFolder(string folderPath)
    {
        try
        {
            ModMetadata metadata = _metadataReader.Read(folderPath);
            ResetUploadProgress();
            _selectedFolder = metadata.FolderPath;
            _previewPath = metadata.PreviewPath ?? string.Empty;
            SetFolderPathText(metadata.FolderPath);
            FolderHintText.Visibility = Visibility.Collapsed;
            TitleTextBox.Text = metadata.Title;
            TagsTextBox.Text = string.Join(" | ", metadata.Tags);
            DescriptionTextBox.Text = metadata.Description;
            LoadPreview(metadata.PreviewPath);
            SetStatus("MetadataLoaded", false);
            AddLog(LocalizationService.Get("MetadataLoaded"));
        }
        catch (Exception ex)
        {
            string message = LocalizationService.Format("FolderLoadFailed", ex.Message);
            SetStatusText(message, true);
            AddLog(message);
        }
    }

    private void SetPreviewPath(string path)
    {
        if (!File.Exists(path))
            return;

        _previewPath = Path.GetFullPath(path);
        ResetUploadProgress();
        LoadPreview(_previewPath);
        SetStatus("PreviewSelectedStatus", false);
        AddLog(LocalizationService.Format("PreviewSelected", Path.GetFileName(_previewPath)));
    }

    private void LoadPreview(string? path)
    {
        PreviewImage.Source = null;
        PreviewPlaceholderText.Visibility = Visibility.Visible;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return;

        try
        {
            using FileStream stream = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            BitmapImage bitmap = new();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            PreviewImage.Source = bitmap;
            PreviewPlaceholderText.Visibility = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            AddLog(LocalizationService.Format("PreviewLoadFailed", ex.Message));
        }
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        using Forms.FolderBrowserDialog dialog = new()
        {
            Description = LocalizationService.Get("FolderLabel"),
            SelectedPath = Directory.Exists(_selectedFolder) ? _selectedFolder : string.Empty,
            ShowNewFolderButton = false
        };
        if (dialog.ShowDialog() == Forms.DialogResult.OK)
            LoadFolder(dialog.SelectedPath);
    }

    private void FolderPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (_isUpdatingFolderText || FolderHintText is null)
            return;

        if (!IsBusy)
            ResetUploadProgress();
        RefreshFolderHint();
        string raw = FolderPathTextBox.Text.Trim();
        string normalized = NormalizeFolderPathInput(raw);
        if (!string.Equals(normalized, _selectedFolder, StringComparison.OrdinalIgnoreCase))
            _selectedFolder = string.Empty;
        if (HasWrappingQuotes(raw))
            TryLoadFolderFromText(true);
    }

    private void FolderPathTextBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        TryLoadFolderFromText(true);
        e.Handled = true;
    }

    private void FolderPathTextBox_GotKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) => RefreshFolderHint();

    private void FolderPathTextBox_LostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        TryLoadFolderFromText(false);
        RefreshFolderHint();
    }

    private void FolderPathTextBox_PreviewDragEnter(object sender, DragEventArgs e) => UpdateFolderDropState(e);

    private void FolderPathTextBox_PreviewDragOver(object sender, DragEventArgs e) => UpdateFolderDropState(e);

    private void FolderPathTextBox_PreviewDragLeave(object sender, DragEventArgs e)
    {
        FolderDropOverlay.Visibility = Visibility.Collapsed;
        RefreshFolderHint();
        e.Handled = true;
    }

    private void FolderPathTextBox_PreviewDrop(object sender, DragEventArgs e)
    {
        FolderDropOverlay.Visibility = Visibility.Collapsed;
        string? folder = IsBusy ? null : GetDroppedFolder(e.Data);
        if (folder is not null)
            LoadFolder(folder);
        RefreshFolderHint();
        e.Handled = true;
    }

    private void UpdateFolderDropState(DragEventArgs e)
    {
        bool acceptsFolder = !IsBusy && GetDroppedFolder(e.Data) is not null;
        e.Effects = acceptsFolder ? DragDropEffects.Copy : DragDropEffects.None;
        FolderDropOverlay.Visibility = acceptsFolder ? Visibility.Visible : Visibility.Collapsed;
        FolderHintText.Visibility = Visibility.Collapsed;
        e.Handled = true;
    }

    private static string? GetDroppedFolder(IDataObject data) =>
        data.GetData(DataFormats.FileDrop) is string[] paths ? paths.FirstOrDefault(Directory.Exists) : null;

    private void TryLoadFolderFromText(bool reportInvalidPath)
    {
        if (IsBusy)
            return;

        string normalized = NormalizeFolderPathInput(FolderPathTextBox.Text);
        if (!string.Equals(normalized, FolderPathTextBox.Text, StringComparison.Ordinal))
            SetFolderPathText(normalized);

        if (Directory.Exists(normalized))
        {
            if (!string.Equals(Path.GetFullPath(normalized), _selectedFolder, StringComparison.OrdinalIgnoreCase))
                LoadFolder(normalized);
        }
        else if (reportInvalidPath && normalized.Length > 0)
        {
            SetStatus("ValidationFolder", true);
        }
    }

    private void SetFolderPathText(string value)
    {
        _isUpdatingFolderText = true;
        try
        {
            FolderPathTextBox.Text = value;
            FolderPathTextBox.CaretIndex = value.Length;
            RefreshFolderHint();
        }
        finally
        {
            _isUpdatingFolderText = false;
        }
    }

    private static string NormalizeFolderPathInput(string? value)
    {
        string normalized = (value ?? string.Empty).Trim();
        while (HasWrappingQuotes(normalized))
            normalized = normalized.Substring(1, normalized.Length - 2).Trim();
        return normalized;
    }

    private static bool HasWrappingQuotes(string value) => value.Length >= 2 && value[0] == '"' && value[value.Length - 1] == '"';

    private void RefreshFolderHint()
    {
        if (FolderHintText is null || FolderDropOverlay is null)
            return;

        FolderHintText.Visibility = FolderDropOverlay.Visibility != Visibility.Visible
            && !FolderPathTextBox.IsKeyboardFocusWithin
            && string.IsNullOrWhiteSpace(FolderPathTextBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
    }

    private void DropZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (IsBusy)
            return;

        OpenFileDialog dialog = new()
        {
            Title = LocalizationService.Get("ChoosePreview"),
            Filter = LocalizationService.Get("PreviewFileFilter"),
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) == true)
            SetPreviewPath(dialog.FileName);
    }

    private void DropZone_Drop(object sender, DragEventArgs e)
    {
        if (!IsBusy && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            string? preview = paths.FirstOrDefault(path => File.Exists(path) && IsPreviewFile(path));
            if (preview is not null)
                SetPreviewPath(preview);
        }
        e.Handled = true;
    }

    private void DropZone_DragEnter(object sender, DragEventArgs e)
    {
        e.Effects = !IsBusy
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths
            && paths.Any(path => File.Exists(path) && IsPreviewFile(path))
                ? DragDropEffects.Copy
                : DragDropEffects.None;
        e.Handled = true;
    }

    private static bool IsPreviewFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension.Equals(".jpg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".jpeg", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".png", StringComparison.OrdinalIgnoreCase);
    }
}
