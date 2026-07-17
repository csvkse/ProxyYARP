using System.Security.Cryptography;
using ProxyYARP.Data.Models;
using ProxyYARP.Data.Repositories;

namespace ProxyYARP.Data.Services;

/// <summary>API Key 业务服务层</summary>
public class ApiKeyService
{
    private readonly ApiKeyRepository _repo;

    public ApiKeyService(ApiKeyRepository repo) => _repo = repo;

    /// <summary>验证 Key，返回实体（验证失败返回 null）</summary>
    public ApiKeyEntity? Validate(string key)
    {
        if (string.IsNullOrWhiteSpace(key)) return null;
        var entity = _repo.GetByKeyValue(key.Trim());
        if (entity == null) return null;

        // 更新最后使用时间
        _repo.UpdateLastUsed(key);
        return entity;
    }

    public List<ApiKeyEntity> GetAll() => _repo.GetAll();

    public ApiKeyEntity? GetById(string id) => _repo.GetById(id);

    public ApiKeyEntity Create(string name, string role)
    {
        var entity = new ApiKeyEntity
        {
            Id = Guid.NewGuid().ToString(),
            KeyValue = GenerateKey(),
            Name = name,
            Role = role,
            IsEnabled = 1,
            CreatedAt = DateTime.UtcNow.ToString("o")
        };
        _repo.Insert(entity);
        return entity;
    }

    public bool Update(string id, string name, string role, bool isEnabled)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return false;
        entity.Name = name;
        entity.Role = role;
        entity.IsEnabled = isEnabled ? 1 : 0;
        _repo.Update(entity);
        return true;
    }

    public bool Delete(string id)
    {
        var entity = _repo.GetById(id);
        if (entity == null) return false;
        _repo.Delete(id);
        return true;
    }

    private static string GenerateKey()
    {
        // 生成 32 字节密码学安全随机 Key，Base64Url 编码
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace('+', '-').Replace('/', '_').TrimEnd('=');
    }
}
