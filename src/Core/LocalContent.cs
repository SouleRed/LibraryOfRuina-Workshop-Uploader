using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using SteamworkUploader.Models;

namespace SteamworkUploader.Services;

public static class WorkshopItemIdParser
{
    private static readonly Regex WorkshopIdRegex = new(
        @"(?:^|[?&])id=(\d+)(?:&|$)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool TryParse(string? value, out ulong itemId)
    {
        itemId = 0;
        string input = value?.Trim() ?? string.Empty;
        if (ulong.TryParse(input, out itemId) && itemId > 0)
            return true;

        if (Uri.TryCreate(input, UriKind.Absolute, out Uri? uri))
        {
            foreach (string pair in uri.Query.TrimStart('?').Split(new[] { '&' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = pair.Split(new[] { '=' }, 2);
                if (parts.Length != 2 || !parts[0].Equals("id", StringComparison.OrdinalIgnoreCase))
                    continue;
                if (ulong.TryParse(Uri.UnescapeDataString(parts[1]), out itemId) && itemId > 0)
                    return true;
            }
        }

        Match match = WorkshopIdRegex.Match(input);
        return match.Success && ulong.TryParse(match.Groups[1].Value, out itemId) && itemId > 0;
    }

    public static string BuildUrl(ulong itemId)
    {
        if (itemId == 0)
            throw new ArgumentOutOfRangeException(nameof(itemId));
        return $"https://steamcommunity.com/sharedfiles/filedetails/?id={itemId}";
    }
}

public static class TagParser
{
    private static readonly char[] Separators = ['|', ',', ';', '，', '；'];

    public static IReadOnlyList<string> Parse(string? value) =>
        (value ?? string.Empty)
        .Split(Separators, StringSplitOptions.RemoveEmptyEntries)
        .Select(tag => tag.Trim())
        .Where(tag => tag.Length > 0)
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();
}

public sealed class ModMetadataReader
{
    public ModMetadata Read(string folderPath)
    {
        if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            throw new DirectoryNotFoundException(folderPath);

        string fullPath = Path.GetFullPath(folderPath);
        string? fallbackPreview = FindTopLevelFile(fullPath, "preview.jpg");
        string? modInfoPath = FindTopLevelFile(fullPath, "StageModInfo.xml");
        if (modInfoPath is null)
            return new ModMetadata(fullPath, fallbackPreview, string.Empty, [], string.Empty);

        XDocument document = XDocument.Load(modInfoPath, LoadOptions.None);
        XElement? workshop = document.Descendants().FirstOrDefault(element => element.Name.LocalName == "Workshop");
        if (workshop is null)
            return new ModMetadata(fullPath, fallbackPreview, string.Empty, [], string.Empty);

        string title = GetElementValue(workshop, "Title");
        string description = GetElementValue(workshop, "Description");
        string tags = GetElementValue(workshop, "Tag");
        string previewValue = GetElementValue(workshop, "PreviewImage");
        string? previewPath = ResolvePreviewPath(fullPath, previewValue) ?? fallbackPreview;
        return new ModMetadata(fullPath, previewPath, title, TagParser.Parse(tags), description);
    }

    private static string GetElementValue(XElement parent, string name) =>
        parent.Elements().FirstOrDefault(element => element.Name.LocalName == name)?.Value.Trim() ?? string.Empty;

    private static string? ResolvePreviewPath(string folderPath, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        string candidate = value.Trim().Trim('"');
        if (!Path.IsPathRooted(candidate))
            candidate = Path.Combine(folderPath, candidate.TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return File.Exists(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private static string? FindTopLevelFile(string folderPath, string fileName) =>
        Directory.EnumerateFiles(folderPath, "*", SearchOption.TopDirectoryOnly)
            .FirstOrDefault(path => Path.GetFileName(path).Equals(fileName, StringComparison.OrdinalIgnoreCase));

}

public static class UploadRequestValidator
{
    public static IReadOnlyList<string> Validate(UploadRequest request)
    {
        List<string> failures = [];
        if (!Directory.Exists(request.FolderPath))
            failures.Add("ValidationFolder");
        if (string.IsNullOrWhiteSpace(request.Title))
            failures.Add("ValidationTitle");
        if (!File.Exists(request.PreviewPath))
            failures.Add("ValidationPreview");
        if (request.Mode == UploadMode.Update && request.WorkshopItemId == 0)
            failures.Add("ValidationWorkshopId");
        return failures;
    }
}
