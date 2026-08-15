using System.Globalization;
using System.IO;
using System.Runtime.Serialization.Json;
using System.Windows;
using SteamworkUploader.Models;

namespace SteamworkUploader.Services;

public static class LocalizationService
{
    private static readonly HashSet<string> SupportedLanguages = new(StringComparer.OrdinalIgnoreCase) { "zh-CN", "en-US", "ja-JP", "ko-KR" };

    public static string CurrentLanguage { get; private set; } = "zh-CN";

    public static event EventHandler? LanguageChanged;

    public static void SetLanguage(string? language)
    {
        string selected = language is not null && SupportedLanguages.Contains(language) ? language : "zh-CN";
        ResourceDictionary resources = Application.Current.Resources;
        ResourceDictionary? existing = resources.MergedDictionaries.FirstOrDefault(dictionary => dictionary.Contains("LanguageCode"));
        ResourceDictionary replacement = new() { Source = new Uri($"Localization/Strings.{selected}.xaml", UriKind.Relative) };

        if (existing is null)
            resources.MergedDictionaries.Insert(0, replacement);
        else
            resources.MergedDictionaries[resources.MergedDictionaries.IndexOf(existing)] = replacement;

        CurrentLanguage = selected;
        CultureInfo culture = CultureInfo.GetCultureInfo(selected);
        CultureInfo.CurrentCulture = culture;
        CultureInfo.CurrentUICulture = culture;
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    public static string Get(string key) => Application.Current.TryFindResource(key) as string ?? key;

    public static string Format(string key, params object[] arguments) => string.Format(CultureInfo.CurrentCulture, Get(key), arguments);
}

public sealed class SettingsService
{
    private readonly string _settingsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "SteamworkUploader",
        "settings.json");

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
                return new AppSettings();
            using FileStream stream = File.OpenRead(_settingsPath);
            return CreateSerializer().ReadObject(stream) as AppSettings ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        string? directory = Path.GetDirectoryName(_settingsPath);
        if (directory is null)
            return;

        Directory.CreateDirectory(directory);
        string temporaryPath = _settingsPath + ".tmp";
        using (FileStream stream = File.Create(temporaryPath))
            CreateSerializer().WriteObject(stream, settings);

        if (File.Exists(_settingsPath))
            File.Replace(temporaryPath, _settingsPath, null);
        else
            File.Move(temporaryPath, _settingsPath);
    }

    private static DataContractJsonSerializer CreateSerializer() =>
        new(typeof(AppSettings), new DataContractJsonSerializerSettings { UseSimpleDictionaryFormat = true });
}
