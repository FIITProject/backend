using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Fiit.Api.Domain;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Fiit.Api.Auth;

public class TokenService(IOptions<JwtOptions> options, TimeProvider time)
{
    public string Issue(Teacher teacher)
    {
        var o = options.Value;
        var now = time.GetUtcNow();
        var token = new JwtSecurityToken(
            issuer: o.Issuer,
            audience: o.Audience,
            claims: [new Claim(JwtRegisteredClaimNames.Sub, teacher.Id.ToString())],
            notBefore: now.UtcDateTime,
            expires: now.Add(o.Lifetime).UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey(o), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public static SymmetricSecurityKey SigningKey(JwtOptions o) => new(Encoding.UTF8.GetBytes(o.Key));
}
