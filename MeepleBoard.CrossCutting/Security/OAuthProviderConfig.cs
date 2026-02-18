using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeepleBoard.CrossCutting.Security
{
    public static class OAuthProviderConfig
    {
        public static IServiceCollection AddOAuthProviders(this IServiceCollection services, IConfiguration configuration)
        {
            var auth = services.AddAuthentication();

            // ✅ Google: só configura se tiver credenciais
            var googleClientId = configuration["Authentication:Google:ClientId"];
            var googleClientSecret = configuration["Authentication:Google:ClientSecret"];

            if (!string.IsNullOrWhiteSpace(googleClientId) && !string.IsNullOrWhiteSpace(googleClientSecret))
            {
                auth.AddGoogle(options =>
                {
                    options.ClientId = googleClientId;
                    options.ClientSecret = googleClientSecret;
                });
            }

            // ✅ Apple: só configura se tiver credenciais
            var appleClientId = configuration["Authentication:Apple:ClientId"];
            var appleKeyId = configuration["Authentication:Apple:KeyId"];
            var appleTeamId = configuration["Authentication:Apple:TeamId"];
            var appleClientSecret = configuration["Authentication:Apple:ClientSecret"];

            if (!string.IsNullOrWhiteSpace(appleClientId) &&
                !string.IsNullOrWhiteSpace(appleKeyId) &&
                !string.IsNullOrWhiteSpace(appleTeamId) &&
                !string.IsNullOrWhiteSpace(appleClientSecret))
            {
                auth.AddApple(options =>
                {
                    options.ClientId = appleClientId;
                    options.KeyId = appleKeyId;
                    options.TeamId = appleTeamId;
                    options.ClientSecret = appleClientSecret;
                });
            }

            return services;
        }
    }
}
