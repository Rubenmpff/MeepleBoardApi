using MeepleBoard.Domain.Entities;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace MeepleBoard.CrossCutting.Security
{
    public static class IdentityConfig
    {
        public static IServiceCollection AddIdentityConfiguration(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddIdentity<User, IdentityRole<Guid>>(options =>
            {
                options.Password.RequireDigit = true;
                options.Password.RequireLowercase = true;
                options.Password.RequireUppercase = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
            })
            .AddEntityFrameworkStores<MeepleBoardDbContext>()
            .AddDefaultTokenProviders();

            var jwtKey = configuration["JWT_KEY"] ?? configuration["Jwt:Key"];


            if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
            {
                throw new InvalidOperationException("❌ A chave JWT_KEY não foi encontrada ou é muito curta.");
            }

            services.AddAuthentication(options =>
            {
                options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
                options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            })
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,

                    ValidIssuer = configuration["Jwt:Issuer"],
                    ValidAudience = configuration["Jwt:Audience"],
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
                    ClockSkew = TimeSpan.Zero
                };
                options.Events = new JwtBearerEvents
                {
                    // A token issued before the account was deleted (anonymised) gives no access: checked on every
                    // authenticated request, one lookup by primary key. Its refresh tokens were removed with the account.
                    OnTokenValidated = async context =>
                    {
                        var id = context.Principal?.FindFirst("sub")?.Value ?? context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                        var users = context.HttpContext.RequestServices.GetRequiredService<IUserRepository>();
                        if (!Guid.TryParse(id, out var userId) || !await users.IsActiveAsync(userId, context.HttpContext.RequestAborted))
                            context.Fail("A conta não existe ou foi eliminada.");
                    }
                };
            });

            return services;
        }
    }
}