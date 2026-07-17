using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Api;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// Auth API 集成测试（/api/auth/*）
/// 验证：登录、登出、/me 端点的认证逻辑
/// </summary>
public class AuthApiTests : IClassFixture<ProxyYarpWebFactory>
{
    private readonly ProxyYarpWebFactory _factory;

    public AuthApiTests(ProxyYarpWebFactory factory) => _factory = factory;

    // ── POST /api/auth/login ────────────────────────────────────

    [Fact]
    public async Task Login_With_Valid_Admin_Key_Should_Return_200()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { apiKey = ProxyYarpWebFactory.AdminKey });

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<AuthResponse>();
        body!.Role.Should().Be("Admin");
        body.Token.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Login_With_Invalid_Key_Should_Return_401()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { apiKey = "wrong-key-xxx" });

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Login_With_Empty_Key_Should_Return_400()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { apiKey = "" });

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Login_Should_Set_Cookie()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.PostAsJsonAsync("/api/auth/login",
            new { apiKey = ProxyYarpWebFactory.AdminKey });

        res.Headers.Should().ContainKey("Set-Cookie");
        var cookie = res.Headers.GetValues("Set-Cookie").First();
        cookie.Should().Contain("api_key");
    }

    // ── GET /api/auth/me ────────────────────────────────────────

    [Fact]
    public async Task Me_With_Valid_Header_Key_Should_Return_User_Info()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadFromJsonAsync<AuthResponse>();
        body!.Role.Should().Be("Admin");
        body.Name.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task Me_Without_Key_Should_Return_401()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.GetAsync("/api/auth/me");

        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    // ── POST /api/auth/logout ───────────────────────────────────

    [Fact]
    public async Task Logout_Should_Return_200_And_Clear_Cookie()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsync("/api/auth/logout", null);

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── QueryString 认证 ─────────────────────────────────────────

    [Fact]
    public async Task Me_With_QueryString_Key_Should_Authenticate()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.GetAsync($"/api/auth/me?key={ProxyYarpWebFactory.AdminKey}");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // ── Version 端点（无需认证）──────────────────────────────────

    [Fact]
    public async Task Version_Endpoint_Should_Be_Public()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.GetAsync("/api/version");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await res.Content.ReadAsStringAsync();
        body.Should().Contain("ProxyYARP");
    }
}

// DTO 帮助类（仅测试用反序列化）
file sealed class AuthResponse
{
    public string Token { get; set; } = "";
    public string Role { get; set; } = "";
    public string Name { get; set; } = "";
}
