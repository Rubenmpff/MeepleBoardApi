using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeepleBoard.CrossCutting.Security
{
    public static class OAuthProviderConfig
    {
        public static IServiceCollection AddOAuthProviders(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddAuthentication()
                .AddGoogle(options =>
                {
                    options.ClientId = configuration["Authentication:Google:ClientId"] ?? string.Empty;
                    options.ClientSecret = configuration["Authentication:Google:ClientSecret"] ?? string.Empty;
                })
                .AddApple(options =>
                {
                    options.ClientId = configuration["Authentication:Apple:ClientId"] ?? string.Empty;
                    options.KeyId = configuration["Authentication:Apple:KeyId"] ?? string.Empty;
                    options.TeamId = configuration["Authentication:Apple:TeamId"] ?? string.Empty;

                    // Certifique-se de que o ClientSecret está correto
                    var clientSecret = configuration["Authentication:Apple:ClientSecret"] ?? string.Empty;
                    if (string.IsNullOrEmpty(clientSecret))
                    {
                        throw new InvalidOperationException("Apple ClientSecret não foi encontrado no appsettings.json.");
                    }
                    options.ClientSecret = clientSecret;
                });

            return services;
        }
    }
}