using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;
using Xunit;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// 网站代理集成测试（/api/websites）
/// 验证：CRUD、鉴权、重复/非法输入拦截，以及最关键的白名单安全门（未登记 → 403）
/// </summary>
public class WebsitesApiTests : IClassFixture<ProxyYarpWebFactory>
{
    private readonly ProxyYarpWebFactory _factory;

    public WebsitesApiTests(ProxyYarpWebFactory factory) => _factory = factory;

    // ── 基础 CRUD ────────────────────────────────────────────────

    [Fact]
    public async Task GetAll_With_Admin_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.GetAsync("/api/websites");
        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Create_With_Admin_Should_Return_201_And_Access_Url()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/websites", new
        {
            name = "Test Site",
            url = "https://valid-internal-site.example",
            rewriteBody = true,
            rewriteCookies = true
        });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<WebsiteDto>();
        body.Should().NotBeNull();
        body!.Url.Should().Be("https://valid-internal-site.example");
        body.AccessUrl.Should().Be("/https://valid-internal-site.example/");
        body.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Without_Auth_Should_Return_401()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.PostAsJsonAsync("/api/websites", new { name = "x", url = "https://a.example" });
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Create_With_ReadOnly_Should_Return_403()
    {
        var client = _factory.CreateReadOnlyClient();
        var res = await client.PostAsJsonAsync("/api/websites", new { name = "x", url = "https://a.example" });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── 输入校验（防止脏数据进入白名单） ──────────────────────────

    [Fact]
    public async Task Create_With_Invalid_Url_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/websites", new { name = "bad", url = "ftp://nope.example" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Duplicate_Url_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var url = $"https://dup-{Guid.NewGuid():N}.example";

        var first = await client.PostAsJsonAsync("/api/websites", new { name = "first", url });
        first.StatusCode.Should().Be(HttpStatusCode.Created);

        // 尾部斜杠 + 大小写差异也应判为同一 authority
        var second = await client.PostAsJsonAsync("/api/websites",
            new { name = "second", url = url.ToUpperInvariant() + "/" });
        second.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    // ── 安全门：未登记的 authority 必须被拒绝 ─────────────────────

    /// <summary>
    /// 安全门的完整生命周期，放在一个测试里以保证自洽（不依赖其他测试的副作用）：
    ///  1. 有站点 → 路由加载 → 未登记地址一律 403（含云元数据地址，防 SSRF）
    ///  2. 站点全部删除 → 路由撤销 → 连入口都不暴露，返回 404
    /// </summary>
    [Fact]
    public async Task Security_Gate_Unlisted_403_And_No_Config_No_Exposure()
    {
        var anon = _factory.CreateAnonymousClient();
        var admin = _factory.CreateAdminClient();

        // ── 1. 登记一个站点，等路由注入 ──
        var seeded = await admin.PostAsJsonAsync("/api/websites", new
        {
            name = "gate-seed",
            url = "https://gate-seed.example"
        });
        seeded.StatusCode.Should().Be(HttpStatusCode.Created);

        await WaitForRouteAsync(() => anon.GetAsync("/http://route-probe.invalid/"),
            expectLoaded: true);

        // 云元数据地址（SSRF 的典型目标）：未被登记 → 必须 403
        var metadata = await anon.GetAsync("/http://169.254.169.254/latest/meta-data/");
        metadata.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 其他任意未登记站点同样 403
        var other = await anon.GetAsync("/http://evil.example.com/");
        other.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 畸形输入 → 400（不是 403，表示连解析都没通过）
        var malformed = await anon.GetAsync("/not-a-url");
        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // ── 2. 清空所有站点，等路由撤销 ──
        var existing = await admin.GetFromJsonAsync<System.Collections.Generic.List<WebsiteDto>>("/api/websites");
        if (existing is not null)
        {
            foreach (var w in existing)
                (await admin.DeleteAsync($"/api/websites/{w.Id}")).StatusCode
                    .Should().Be(HttpStatusCode.OK);
        }

        await WaitForRouteAsync(() => anon.GetAsync("/http://route-probe.invalid/"), expectLoaded: false);

        // "无配置即无暴露"：连入口都给不出去，避免网关变成可探测的开放代理
        var afterClear = await anon.GetAsync("/http://169.254.169.254/latest/meta-data/");
        afterClear.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    /// <summary>轮询等待网站代理路由注入/撤销（最多 ~15s，热重载约 3s 一次）</summary>
    private static async Task WaitForRouteAsync(
        Func<Task<HttpResponseMessage>> probe,
        bool expectLoaded)
    {
        for (var i = 0; i < 30; i++)
        {
            var res = await probe();
            var loaded = res.StatusCode != HttpStatusCode.NotFound;
            if (loaded == expectLoaded) return;
            await Task.Delay(500);
        }
        throw new InvalidOperationException(
            $"网站代理路由未在预期时间内{(expectLoaded ? "生效" : "撤销")}");
    }

    [Fact]
    public async Task Registered_Url_Object_Should_Be_Deletable()
    {
        var client = _factory.CreateAdminClient();
        var created = await client.PostAsJsonAsync("/api/websites", new
        {
            name = "ToDelete",
            url = $"https://del-{Guid.NewGuid():N}.example"
        });
        created.StatusCode.Should().Be(HttpStatusCode.Created);
        var dto = await created.Content.ReadFromJsonAsync<WebsiteDto>();

        var deleted = await client.DeleteAsync($"/api/websites/{dto!.Id}");
        deleted.StatusCode.Should().Be(HttpStatusCode.OK);

        var again = await client.DeleteAsync($"/api/websites/{dto.Id}");
        again.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── URL 预览接口 ─────────────────────────────────────────────

    [Fact]
    public async Task TestUrl_Should_Normalize_Missing_Scheme()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/websites/test-url", new { url = "example.com/api" });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<UrlPreview>();
        body.Should().NotBeNull();
        body!.TargetUrl.Should().Be("https://example.com");
        body.Authority.Should().Be("example.com");
        body.AccessUrlSuffix.Should().Be("/https://example.com/");
    }

    [Fact]
    public async Task TestUrl_With_Invalid_Url_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/websites/test-url", new { url = "ftp://bad.example" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }
}

// 与 ProxyYARP.Api 中的 DTO 对应的测试用形状
public sealed class WebsiteDto
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Url { get; set; } = "";
    public string AccessUrl { get; set; } = "";
    public bool RewriteBody { get; set; }
    public bool RewriteCookies { get; set; }
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string UpdatedAt { get; set; } = "";
}

public sealed class UrlPreview
{
    public string TargetUrl { get; set; } = "";
    public string Authority { get; set; } = "";
    public string AccessUrlSuffix { get; set; } = "";
}
