using Mawrid.Infrastructure.Identity;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Mawrid.Api.Common.Security;

public static class AuthenticationSetup
{
    public static IServiceCollection AddApiAuthentication(this IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();

        // Configure the bearer handler from the validated JwtOptions (section "Jwt").
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<JwtOptions>>((bearer, jwt) =>
            {
                bearer.MapInboundClaims = false;
                bearer.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Value.Issuer,
                    ValidAudience = jwt.Value.Audience,
                    IssuerSigningKey = JwtTokenService.SigningKey(jwt.Value),
                    NameClaimType = MawridClaims.Name,
                    RoleClaimType = MawridClaims.Role,
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        services.AddAuthorization(Policies.Configure);
        return services;
    }
}
