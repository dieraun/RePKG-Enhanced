// 基于 notscuffed/RePKG (MIT) 二次修改 —— 新增文件（RePKG-Enhanced）
// 更新检查测试：解析 GitHub release JSON、语义化版本比较、HTTP 失败降级。
// 全部离线：用自定义 HttpMessageHandler 冒充 GitHub，绝不真的联网。

using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using RePKG.Shared;
using RePKG.Shared.Updates;
using Xunit;

namespace RePKG.Tests.Shared.Updates
{
    public sealed class UpdateCheckerTests
    {
        private const string ReleaseJson = """
            {
              "tag_name": "v2.1.0",
              "html_url": "https://github.com/dieraun/RePKG-Enhanced/releases/tag/v2.1.0",
              "body": "## Changes\n- 双语界面",
              "published_at": "2026-11-02T10:00:00Z"
            }
            """;

        private sealed class StubHandler : HttpMessageHandler
        {
            private readonly HttpStatusCode _status;
            private readonly string _body;

            public StubHandler(HttpStatusCode status, string body)
            {
                _status = status;
                _body = body;
            }

            public int Calls { get; private set; }

            public Uri? LastRequestUri { get; private set; }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                Calls++;
                LastRequestUri = request.RequestUri;

                return Task.FromResult(new HttpResponseMessage(_status)
                {
                    Content = new StringContent(_body)
                });
            }
        }

        [Fact]
        public void Parse_ReadsTagUrlNotesAndDate()
        {
            var result = UpdateChecker.Parse(ReleaseJson, "2.0.0");

            Assert.True(result.Succeeded);
            Assert.Equal("2.0.0", result.CurrentVersion);
            Assert.Equal("2.1.0", result.LatestVersion);
            Assert.True(result.IsNewer);
            Assert.Equal("https://github.com/dieraun/RePKG-Enhanced/releases/tag/v2.1.0", result.ReleaseUrl);
            Assert.Contains("双语界面", result.ReleaseNotes);
            Assert.NotNull(result.PublishedAt);
        }

        [Fact]
        public void Parse_ReportsNoUpdateWhenTagIsNotNewer()
        {
            var json = """{ "tag_name": "v2.0.0", "html_url": "https://example.com" }""";

            var result = UpdateChecker.Parse(json, "2.0.0");

            Assert.True(result.Succeeded);
            Assert.False(result.IsNewer);
        }

        [Fact]
        public void Parse_FailsGracefullyOnBrokenJson()
        {
            var result = UpdateChecker.Parse("{ not json", "2.0.0");

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void Parse_FailsWhenTagMissing()
        {
            var result = UpdateChecker.Parse("""{ "html_url": "https://example.com" }""", "2.0.0");

            Assert.False(result.Succeeded);
        }

        [Theory]
        [InlineData("2.1.0", "2.0.0", true)]
        [InlineData("2.0.0", "2.0.0", false)]
        [InlineData("1.9.9", "2.0.0", false)]
        [InlineData("2.10.0", "2.9.0", true)]
        [InlineData("v3", "2.9.9", true)]
        [InlineData("2.0", "2.0.0", false)]
        [InlineData("2.0.1-beta", "2.0.0", true)]
        [InlineData("garbage", "2.0.0", false)]
        public void SemanticVersion_ComparesNumericSegments(string candidate, string current, bool expected)
        {
            Assert.Equal(expected, SemanticVersion.IsNewer(candidate, current));
        }

        [Fact]
        public async Task CheckAsync_RequestsGitHubAndParsesResponse()
        {
            var handler = new StubHandler(HttpStatusCode.OK, ReleaseJson);
            using var checker = UpdateChecker.CreateForTesting(handler);

            var result = await checker.CheckAsync("2.0.0", CancellationToken.None);

            Assert.True(result.Succeeded);
            Assert.True(result.IsNewer);
            Assert.Equal(1, handler.Calls);
            Assert.Equal(AppInfo.LatestReleaseApiUrl, handler.LastRequestUri!.ToString());
        }

        [Fact]
        public async Task CheckAsync_ReturnsErrorOnHttpFailure()
        {
            var handler = new StubHandler(HttpStatusCode.Forbidden, "rate limited");
            using var checker = UpdateChecker.CreateForTesting(handler);

            var result = await checker.CheckAsync("2.0.0", CancellationToken.None);

            Assert.False(result.Succeeded);
            Assert.Contains("403", result.Error);
        }
    }
}
