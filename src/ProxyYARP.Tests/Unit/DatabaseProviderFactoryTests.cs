using FluentAssertions;
using ProxyYARP.Data.Db;

namespace ProxyYARP.Tests.Unit;

/// <summary>DatabaseProviderFactory 单元测试</summary>
public class DatabaseProviderFactoryTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("sqlite")]
    [InlineData("SQLite")]
    [InlineData(" SQLITE ")]
    public void Create_Should_Return_Sqlite_By_Default(string? provider)
    {
        var p = DatabaseProviderFactory.Create(provider, null);
        p.Name.Should().Be("sqlite");
    }

    [Fact]
    public void Create_Sqlite_Empty_ConnectionString_Should_Use_Default_Path()
    {
        var p = DatabaseProviderFactory.Create("sqlite", null);
        p.DisplayInfo.Should().Contain("proxy.db");
    }

    [Fact]
    public void Create_Sqlite_Should_Pass_Through_ConnectionString()
    {
        var p = DatabaseProviderFactory.Create("sqlite", "Data Source=/tmp/x.db;Cache=Shared;");
        p.DisplayInfo.Should().Be("Data Source=/tmp/x.db;Cache=Shared;");
    }

    [Fact]
    public void Create_Unknown_Provider_Should_Throw()
    {
        var act = () => DatabaseProviderFactory.Create("oracle", null);
        act.Should().Throw<ArgumentException>().WithMessage("*oracle*");
    }
}
