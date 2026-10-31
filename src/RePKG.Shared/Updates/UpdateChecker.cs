// 基于 notscuffed/RePKG (MIT) 二次修改：本文件为 RePKG-Enhanced 新增，不来自原版。

using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace RePKG.Shared.Updates;

/// <summary>一次更新检查的结果。</summary>
public sealed record UpdateCheckResult(
    string CurrentVersion,
    string? LatestVersion,
    bool IsNewer,
    string? ReleaseUrl,
    DateTimeOffset? PublishedAt,
    string? ReleaseNotes,
    string? Error)
{
    /// <summary>是否成功拿到了远端版本号。</summary>
    public bool Succeeded => Error == null && !string.IsNullOrEmpty(LatestVersion);

    /// <summary>检查失败的结果。</summary>
    public static UpdateCheckResult Failed(string currentVersion, string error)
        => new(currentVersion, null, false, null, null, null, error);
}

/// <summary>
/// 手动更新检查：只读 GitHub 的 latest release，不做任何自动联网。
/// 通过注入 <see cref="HttpMessageHandler"/> 可以在测试里完全离线地验证。
/// </summary>
public sealed class UpdateChecker : IDisposable
{
    private readonly HttpClient _http;
    private readonly bool _ownsClient;
    private readonly string _apiUrl;

    /// <summary>创建实例。</summary>
    /// <param name="apiUrl">release API 地址；为空则用 <see cref="AppInfo.LatestReleaseApiUrl"/>。</param>
    /// <param name="httpClient">外部提供的 HttpClient；为空则内部新建。</param>
    /// <param name="timeout">超时；默认 8 秒。</param>
    public UpdateChecker(string? apiUrl = null, HttpClient? httpClient = null, TimeSpan? timeout = null)
    {
        _apiUrl = string.IsNullOrWhiteSpace(apiUrl) ? AppInfo.LatestReleaseApiUrl : apiUrl;
        _ownsClient = httpClient == null;
        _http = httpClient ?? new HttpClient();
        _http.Timeout = timeout ?? TimeSpan.FromSeconds(8);
    }

    /// <summary>用自定义 handler 创建（测试用，不需要真实网络）。</summary>
    public static UpdateChecker CreateForTesting(HttpMessageHandler handler, string? apiUrl = null, TimeSpan? timeout = null)
        => new(apiUrl, new HttpClient(handler, disposeHandler: true), timeout ?? TimeSpan.FromSeconds(8));

    /// <summary>检查最新版本。</summary>
    public async Task<UpdateCheckResult> CheckAsync(string currentVersion, CancellationToken cancellationToken = default)
    {
        var current = AppInfo.ParseVersion(currentVersion);

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, _apiUrl);
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
            request.Headers.UserAgent.ParseAdd($"{AppInfo.ProductName}/{current}");

            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return UpdateCheckResult.Failed(current, $"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            return Parse(json, current);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            return UpdateCheckResult.Failed(current, ex.Message);
        }
    }

    /// <summary>解析 GitHub release JSON（独立出来便于单测）。</summary>
    public static UpdateCheckResult Parse(string json, string currentVersion)
    {
        var current = AppInfo.ParseVersion(currentVersion);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;

            var tag = root.TryGetProperty("tag_name", out var tagElement) ? tagElement.GetString() : null;
            var latest = AppInfo.ParseVersion(tag);

            if (string.IsNullOrEmpty(latest) || latest == "0.0.0")
            {
                return UpdateCheckResult.Failed(current, "release JSON has no tag_name");
            }

            string? url = root.TryGetProperty("html_url", out var urlElement) ? urlElement.GetString() : null;
            string? notes = root.TryGetProperty("body", out var bodyElement) ? bodyElement.GetString() : null;
            DateTimeOffset? published = null;

            if (root.TryGetProperty("published_at", out var publishedElement)
                && publishedElement.ValueKind == JsonValueKind.String
                && DateTimeOffset.TryParse(publishedElement.GetString(), out var parsed))
            {
                published = parsed;
            }

            return new UpdateCheckResult(
                current,
                latest,
                SemanticVersion.IsNewer(latest, current),
                url,
                published,
                notes,
                null);
        }
        catch (JsonException ex)
        {
            return UpdateCheckResult.Failed(current, ex.Message);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_ownsClient)
        {
            _http.Dispose();
        }
    }
}

/// <summary>够用的语义化版本比较（只比较数字段，忽略预发布后缀）。</summary>
public static class SemanticVersion
{
    /// <summary>把 <c>1.2.3</c> 解析成数字段；无法解析时返回空数组。</summary>
    public static int[] Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<int>();
        }

        var value = text.Trim();

        if (value.Length > 0 && (value[0] == 'v' || value[0] == 'V'))
        {
            value = value.Substring(1);
        }

        var cut = value.IndexOfAny(new[] { '-', '+' });
        if (cut >= 0)
        {
            value = value.Substring(0, cut);
        }

        var parts = value.Split('.');
        var numbers = new int[parts.Length];

        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], out var number) || number < 0)
            {
                return Array.Empty<int>();
            }

            numbers[i] = number;
        }

        return numbers;
    }

    /// <summary><paramref name="candidate"/> 是否比 <paramref name="current"/> 新。</summary>
    public static bool IsNewer(string? candidate, string? current)
    {
        var left = Parse(candidate);
        var right = Parse(current);

        if (left.Length == 0 || right.Length == 0)
        {
            return false;
        }

        var length = Math.Max(left.Length, right.Length);

        for (var i = 0; i < length; i++)
        {
            var a = i < left.Length ? left[i] : 0;
            var b = i < right.Length ? right[i] : 0;

            if (a != b)
            {
                return a > b;
            }
        }

        return false;
    }
}
