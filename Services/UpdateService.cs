using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace ZCue.Services;

public sealed class UpdateService
{
    // SECTION GitHub Release 查询

    private const string LatestReleaseApiUrl = "https://api.github.com/repos/Zevxn/ZCue/releases/latest";
    private static readonly HttpClient HttpClient = CreateHttpClient();
    private static readonly Version CurrentVersion = GetCurrentVersion();

    public static string CurrentVersionText => CurrentVersion.ToString(3);

    public async Task<UpdateInfo?> CheckForUpdateAsync(CancellationToken cancellationToken = default)
    {
        using var response = await HttpClient.GetAsync(
            LatestReleaseApiUrl,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;
        var tagName = ReadRequiredString(root, "tag_name");
        var releaseUrl = ReadRequiredString(root, "html_url");

        if (!TryParseTagVersion(tagName, out var releaseVersion))
        {
            throw new InvalidDataException($"GitHub Release 标签不是有效版本号：{tagName}");
        }

        if (!Uri.TryCreate(releaseUrl, UriKind.Absolute, out var releaseUri)
            || releaseUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException("GitHub 返回了无效的 Release 页面地址。");
        }

        return releaseVersion > CurrentVersion
            ? new UpdateInfo(tagName, releaseUri)
            : null;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ZCue");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static Version GetCurrentVersion()
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version ?? new Version(1, 0, 0);
        return NormalizeVersion(version);
    }

    private static bool TryParseTagVersion(string tagName, out Version version)
    {
        var normalized = tagName.Trim();
        if (normalized.StartsWith('v') || normalized.StartsWith('V'))
        {
            normalized = normalized[1..];
        }

        var suffixIndex = normalized.IndexOfAny(['-', '+']);
        if (suffixIndex >= 0)
        {
            normalized = normalized[..suffixIndex];
        }

        if (Version.TryParse(normalized, out var parsedVersion))
        {
            version = NormalizeVersion(parsedVersion);
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
    }

    private static Version NormalizeVersion(Version version) =>
        new(version.Major, version.Minor, Math.Max(version.Build, 0));

    private static string ReadRequiredString(JsonElement element, string propertyName)
    {
        if (element.TryGetProperty(propertyName, out var property)
            && property.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(property.GetString()))
        {
            return property.GetString()!;
        }

        throw new InvalidDataException($"GitHub Release 响应缺少 {propertyName} 字段。");
    }

    // !SECTION GitHub Release 查询
}

public sealed record UpdateInfo(string TagName, Uri ReleaseUri);
