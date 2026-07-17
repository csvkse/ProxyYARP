using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Integration;

/// <summary>
/// Keys API 集成测试（/api/keys）
/// 验证：CRUD 操作、Admin 权限守卫、ReadOnly 权限限制
/// </summary>
public class KeysApiTests : IClassFixture<ProxyYarpWebFactory>
{
    private readonly ProxyYarpWebFactory _factory;

    public KeysApiTests(ProxyYarpWebFactory factory) => _factory = factory;

    // ── GET /api/keys ────────────────────────────────────────────

    [Fact]
    public async Task GetAll_With_Admin_Key_Should_Return_200_With_List()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.GetAsync("/api/keys");

        res.StatusCode.Should().Be(HttpStatusCode.OK);
        var keys = await res.Content.ReadFromJsonAsync<List<KeyDto>>();
        keys.Should().NotBeNull();
        keys!.Should().NotBeEmpty("应至少包含初始 Admin Key");
    }

    [Fact]
    public async Task GetAll_Without_Key_Should_Return_401()
    {
        var client = _factory.CreateAnonymousClient();
        var res = await client.GetAsync("/api/keys");
        res.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task GetAll_With_ReadOnly_Key_Should_Return_403()
    {
        var client = _factory.CreateReadOnlyClient();
        var res = await client.GetAsync("/api/keys");
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── POST /api/keys ───────────────────────────────────────────

    [Fact]
    public async Task Create_Key_With_Admin_Should_Return_201_With_KeyValue()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/keys",
            new { name = "Integration Test Key", role = "ReadOnly" });

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await res.Content.ReadFromJsonAsync<KeyDto>();
        body!.KeyValue.Should().NotBeNullOrWhiteSpace("创建后应返回明文 Key");
        body.Name.Should().Be("Integration Test Key");
        body.Role.Should().Be("ReadOnly");
        body.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Create_Key_Without_Name_Should_Return_400()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PostAsJsonAsync("/api/keys",
            new { name = "", role = "ReadOnly" });
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Create_Key_With_ReadOnly_Should_Return_403()
    {
        var client = _factory.CreateReadOnlyClient();
        var res = await client.PostAsJsonAsync("/api/keys",
            new { name = "Attempt", role = "Admin" });
        res.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── PUT /api/keys/{id} ───────────────────────────────────────

    [Fact]
    public async Task Update_Key_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();

        // 先创建
        var createRes = await client.PostAsJsonAsync("/api/keys",
            new { name = "Before Update", role = "ReadOnly" });
        var created = await createRes.Content.ReadFromJsonAsync<KeyDto>();

        // 更新
        var updateRes = await client.PutAsJsonAsync($"/api/keys/{created!.Id}",
            new { name = "After Update", role = "Admin", isEnabled = true });

        updateRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 验证：通过 GetById
        var getRes = await client.GetAsync($"/api/keys/{created.Id}");
        var updated = await getRes.Content.ReadFromJsonAsync<KeyDto>();
        updated!.Name.Should().Be("After Update");
        updated.Role.Should().Be("Admin");
    }

    [Fact]
    public async Task Update_Nonexistent_Key_Should_Return_404()
    {
        var client = _factory.CreateAdminClient();
        var res = await client.PutAsJsonAsync("/api/keys/nonexistent-id",
            new { name = "X", role = "Admin", isEnabled = true });
        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    // ── DELETE /api/keys/{id} ────────────────────────────────────

    [Fact]
    public async Task Delete_Key_Should_Return_200()
    {
        var client = _factory.CreateAdminClient();

        // 先创建一个 Key
        var createRes = await client.PostAsJsonAsync("/api/keys",
            new { name = "To Delete", role = "ReadOnly" });
        var created = await createRes.Content.ReadFromJsonAsync<KeyDto>();

        // 删除
        var delRes = await client.DeleteAsync($"/api/keys/{created!.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 确认已删除（GetById 应返回 404）
        var getRes = await client.GetAsync($"/api/keys/{created.Id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Delete_With_ReadOnly_Should_Return_403()
    {
        // 先用 Admin 创建
        var admin = _factory.CreateAdminClient();
        var createRes = await admin.PostAsJsonAsync("/api/keys",
            new { name = "Protected", role = "ReadOnly" });
        var created = await createRes.Content.ReadFromJsonAsync<KeyDto>();

        // ReadOnly 尝试删除
        var roClient = _factory.CreateReadOnlyClient();
        var delRes = await roClient.DeleteAsync($"/api/keys/{created!.Id}");
        delRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    // ── 新建 Key 后立即认证 ──────────────────────────────────────

    [Fact]
    public async Task New_Key_Should_Be_Usable_For_Authentication_Immediately()
    {
        var adminClient = _factory.CreateAdminClient();

        // 创建新 Key
        var createRes = await adminClient.PostAsJsonAsync("/api/keys",
            new { name = "Fresh Key", role = "Admin" });
        var created = await createRes.Content.ReadFromJsonAsync<KeyDto>();

        // 用新 Key 访问 /api/auth/me
        var newKeyClient = _factory.CreateClient();
        newKeyClient.DefaultRequestHeaders.Add("X-Api-Key", created!.KeyValue);
        var meRes = await newKeyClient.GetAsync("/api/auth/me");

        meRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}

file sealed class KeyDto
{
    public string Id { get; set; } = "";
    public string KeyValue { get; set; } = "";
    public string Name { get; set; } = "";
    public string Role { get; set; } = "";
    public bool IsEnabled { get; set; }
    public string CreatedAt { get; set; } = "";
    public string? LastUsedAt { get; set; }
}
