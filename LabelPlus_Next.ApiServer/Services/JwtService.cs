using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace LabelPlus_Next.ApiServer.Services;

public sealed class JwtOptions
{
    public string Key { get; set; } = "dev-secret-key-change";
    public string Issuer { get; set; } = "LabelPlus";
    public string Audience { get; set; } = "LabelPlusClients";
    public int ExpireHours { get; set; } = 24;
}

public interface IJwtService
{
    string IssueToken(long uid, string username, string? ssoId = null);
    ClaimsPrincipal? ValidateToken(string token);
}

public sealed class JwtService : IJwtService
{
    private readonly JwtOptions _opt;
    private readonly SymmetricSecurityKey _key;
    private readonly SigningCredentials _cred;
    private readonly TokenValidationParameters _val;

    public JwtService(IOptions<JwtOptions> options)
    {
        _opt = options.Value;
        _key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_opt.Key));
        _cred = new SigningCredentials(_key, SecurityAlgorithms.HmacSha256);
        _val = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _opt.Issuer,
            ValidAudience = _opt.Audience,
            IssuerSigningKey = _key,
            ClockSkew = TimeSpan.FromMinutes(2)
        };
    }

    public string IssueToken(long uid, string username, string? ssoId = null)
    {
        var handler = new JwtSecurityTokenHandler();
        var now = DateTime.UtcNow;
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, uid.ToString()),
            new(ClaimTypes.Name, username)
        };
        if (!string.IsNullOrWhiteSpace(ssoId))
            claims.Add(new("sso_id", ssoId));
        var token = new JwtSecurityToken(
            issuer: _opt.Issuer,
            audience: _opt.Audience,
            claims: claims,
            notBefore: now,
            expires: now.AddHours(_opt.ExpireHours),
            signingCredentials: _cred);
        return handler.WriteToken(token);
    }

    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var handler = new JwtSecurityTokenHandler();
            return handler.ValidateToken(token, _val, out _);
        }
        catch
        {
            return null;
        }
    }
}

