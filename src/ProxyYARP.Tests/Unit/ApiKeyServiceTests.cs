using FluentAssertions;
using ProxyYARP.Data.Models;
using ProxyYARP.Tests.TestHelpers;

namespace ProxyYARP.Tests.Unit;

/// <summary>
/// ApiKeyService 单元测试
/// 验证：创建、验证、更新、删除、禁用逻辑
/// </summary>
public class ApiKeyServiceTests : IDisposable
{
    private readonly TestDatabase _db;

    public ApiKeyServiceTests() => _db = new TestDatabase();
    public void Dispose() => _db.Dispose();

    // ── Create ──────────────────────────────────────────────────

    [Fact]
    public void Create_Should_Return_Entity_With_Generated_Key()
    {
        var entity = _db.KeyService.Create("Test Key", "Admin");

        entity.Should().NotBeNull();
        entity.Id.Should().NotBeNullOrWhiteSpace();
        entity.KeyValue.Should().NotBeNullOrWhiteSpace();
        entity.Name.Should().Be("Test Key");
        entity.Role.Should().Be("Admin");
        entity.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Create_ReadOnly_Key_Should_Have_Correct_Role()
    {
        var entity = _db.KeyService.Create("RO Key", "ReadOnly");
        entity.Role.Should().Be("ReadOnly");
    }

    [Fact]
    public void Create_Multiple_Keys_Should_Have_Unique_Values()
    {
        var k1 = _db.KeyService.Create("Key1", "Admin");
        var k2 = _db.KeyService.Create("Key2", "Admin");

        k1.KeyValue.Should().NotBe(k2.KeyValue);
        k1.Id.Should().NotBe(k2.Id);
    }

    // ── Validate ────────────────────────────────────────────────

    [Fact]
    public void Validate_Should_Return_Entity_For_Valid_Key()
    {
        var created = _db.KeyService.Create("Auth Key", "Admin");
        var result = _db.KeyService.Validate(created.KeyValue);

        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
        result.Role.Should().Be("Admin");
    }

    [Fact]
    public void Validate_Should_Return_Null_For_Invalid_Key()
    {
        var result = _db.KeyService.Validate("nonexistent-key-xyz");
        result.Should().BeNull();
    }

    [Fact]
    public void Validate_Should_Return_Null_For_Empty_Key()
    {
        _db.KeyService.Validate("").Should().BeNull();
        _db.KeyService.Validate("   ").Should().BeNull();
        _db.KeyService.Validate(null!).Should().BeNull();
    }

    [Fact]
    public void Validate_Should_Return_Null_For_Disabled_Key()
    {
        // 创建 Key 然后禁用
        var created = _db.KeyService.Create("Disabled Key", "Admin");
        _db.KeyService.Update(created.Id, "Disabled Key", "Admin", isEnabled: false);

        var result = _db.KeyService.Validate(created.KeyValue);
        result.Should().BeNull();
    }

    [Fact]
    public void Validate_Should_Work_For_Created_Key_Without_Spaces()
    {
        // 验证正常创建的 Key 可以验证成功
        var created = _db.KeyService.Create("Trim Key", "ReadOnly");

        var result = _db.KeyService.Validate(created.KeyValue);
        result.Should().NotBeNull();
        result!.Id.Should().Be(created.Id);
    }

    // ── GetAll ──────────────────────────────────────────────────

    [Fact]
    public void GetAll_Should_Return_Empty_When_No_Keys()
    {
        _db.KeyService.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void GetAll_Should_Return_All_Created_Keys()
    {
        _db.KeyService.Create("Key A", "Admin");
        _db.KeyService.Create("Key B", "ReadOnly");
        _db.KeyService.Create("Key C", "ReadOnly");

        _db.KeyService.GetAll().Should().HaveCount(3);
    }

    // ── Update ──────────────────────────────────────────────────

    [Fact]
    public void Update_Should_Change_Name_And_Role()
    {
        var created = _db.KeyService.Create("Old Name", "ReadOnly");

        var ok = _db.KeyService.Update(created.Id, "New Name", "Admin", isEnabled: true);

        ok.Should().BeTrue();
        var updated = _db.KeyService.GetById(created.Id);
        updated!.Name.Should().Be("New Name");
        updated.Role.Should().Be("Admin");
        updated.IsEnabled.Should().BeTrue();
    }

    [Fact]
    public void Update_Should_Return_False_For_Nonexistent_Id()
    {
        var ok = _db.KeyService.Update("nonexistent-id", "Name", "Admin", true);
        ok.Should().BeFalse();
    }

    [Fact]
    public void Update_Disable_Then_Validate_Should_Fail()
    {
        var created = _db.KeyService.Create("Active Key", "Admin");

        // 先验证成功
        _db.KeyService.Validate(created.KeyValue).Should().NotBeNull();

        // 禁用
        _db.KeyService.Update(created.Id, "Active Key", "Admin", isEnabled: false);

        // 验证失败
        _db.KeyService.Validate(created.KeyValue).Should().BeNull();
    }

    // ── Delete ──────────────────────────────────────────────────

    [Fact]
    public void Delete_Should_Remove_Key()
    {
        var created = _db.KeyService.Create("To Delete", "ReadOnly");
        _db.KeyService.GetAll().Should().HaveCount(1);

        var ok = _db.KeyService.Delete(created.Id);

        ok.Should().BeTrue();
        _db.KeyService.GetAll().Should().BeEmpty();
    }

    [Fact]
    public void Delete_Should_Return_False_For_Nonexistent_Id()
    {
        var ok = _db.KeyService.Delete("nonexistent-id");
        ok.Should().BeFalse();
    }

    [Fact]
    public void Delete_Then_Validate_Should_Return_Null()
    {
        var created = _db.KeyService.Create("Temp Key", "Admin");
        _db.KeyService.Delete(created.Id);

        _db.KeyService.Validate(created.KeyValue).Should().BeNull();
    }

    // ── IsAdmin ──────────────────────────────────────────────────

    [Fact]
    public void IsAdmin_Should_Return_True_For_Admin_Role()
    {
        var created = _db.KeyService.Create("Admin", "Admin");
        var entity = _db.KeyService.GetById(created.Id);
        entity!.IsAdmin.Should().BeTrue();
    }

    [Fact]
    public void IsAdmin_Should_Return_False_For_ReadOnly_Role()
    {
        var created = _db.KeyService.Create("RO", "ReadOnly");
        var entity = _db.KeyService.GetById(created.Id);
        entity!.IsAdmin.Should().BeFalse();
    }
}
