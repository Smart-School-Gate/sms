using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace SmartSchool.Api.Auth;

public sealed class AuthOptions
{
    public const string SectionName = "Auth";
    public string Issuer { get; set; } = "smart-school-api";
    public string Audience { get; set; } = "smart-school";
    public string SigningKey { get; set; } = "";
    public int AccessTokenMinutes { get; set; } = 15;
    public int RefreshTokenDays { get; set; } = 14;
    public int LoginPermitLimit { get; set; } = 10;
    public int LoginWindowSeconds { get; set; } = 60;
}

public sealed class TokenService(IOptions<AuthOptions> options, TimeProvider time)
{
    private readonly AuthOptions _options = options.Value;

    public int ExpiresInSeconds => _options.AccessTokenMinutes * 60;

    public string CreateAccessToken(Guid userId, int tokenVersion, Guid? contextId)
    {
        var claims = new List<Claim>
        {
            new("sub", userId.ToString("D")),
            new("ver", tokenVersion.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };
        if (contextId is Guid selected)
            claims.Add(new Claim("ctx", selected.ToString("D")));

        var now = time.GetUtcNow().UtcDateTime;
        var token = new JwtSecurityToken(
            issuer: _options.Issuer,
            audience: _options.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddMinutes(_options.AccessTokenMinutes),
            signingCredentials: new SigningCredentials(SigningKey(), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public DateTime RefreshExpiresAtUtc() => time.GetUtcNow().UtcDateTime.AddDays(_options.RefreshTokenDays);

    public static string NewRefreshToken()
    {
        return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    public static string Hash(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }

    public SecurityKey SigningKey() => new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SigningKey));
}
