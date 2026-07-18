using FluentAssertions;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Tests.Unit;

/// <summary>PostgreSqlDbProvider 单元测试（不连真实数据库）</summary>
public class PostgreSqlDbProviderTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Ctor_Empty_ConnectionString_Should_Throw(string? connStr)
    {
        var act = () => new PostgreSqlDbProvider(connStr);
        act.Should().Throw<ArgumentException>().WithMessage("*连接字符串*");
    }

    [Fact]
    public void DisplayInfo_Should_Not_Leak_Password()
    {
        var p = new PostgreSqlDbProvider(
            "Host=pg.example.com;Port=5433;Database=proxy;Username=admin;Password=s3cret");
        p.DisplayInfo.Should().Contain("pg.example.com").And.Contain("5433");
        p.DisplayInfo.Should().NotContain("s3cret");
    }

    [Theory]
    [InlineData("pgsql")]
    [InlineData("postgres")]
    [InlineData("PostgreSQL")]
    public void Factory_Should_Create_Pgsql_Provider(string name)
    {
        var p = DatabaseProviderFactory.Create(name, "Host=x;Database=d;Username=u;Password=p");
        p.Name.Should().Be("pgsql");
    }
}
