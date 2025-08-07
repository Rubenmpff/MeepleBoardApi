using AspNetCoreRateLimit;
using FluentValidation;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using MeepleBoard.Infra.Data.Repositories;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Validator;
using MeepleBoardApi.Services.Mapping.AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeepleBoard.CrossCutting.IoC
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Centraliza toda a injeção de dependência da aplicação.
        /// </summary>
        public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
        {
            return services
                .ConfigureDatabase(configuration)
                .RegisterRepositories()
                .RegisterServices()
                .ConfigureAutoMapper()
                .ConfigureValidators()
                .ConfigureRateLimiting(configuration);
        }

        /// <summary>
        /// Configura o banco de dados com EF Core e SQL Server.
        /// </summary>
        private static IServiceCollection ConfigureDatabase(this IServiceCollection services, IConfiguration configuration)
        {
            services.AddDbContext<MeepleBoardDbContext>(options =>
                options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

            return services;
        }

        /// <summary>
        /// Registra todos os repositórios do domínio.
        /// </summary>
        private static IServiceCollection RegisterRepositories(this IServiceCollection services)
        {
            services.AddScoped<IUserRepository, UserRepository>();
            services.AddScoped<IGameRepository, GameRepository>();
            services.AddScoped<IGameSessionRepository, GameSessionRepository>();
            services.AddScoped<IGameSessionPlayerRepository, GameSessionPlayerRepository>();
            services.AddScoped<IMatchRepository, MatchRepository>();
            services.AddScoped<IMatchPlayerRepository, MatchPlayerRepository>();
            services.AddScoped<IUserGameLibraryRepository, UserGameLibraryRepository>();
            services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>(); // 🔐 Suporte para Refresh Tokens
            services.AddScoped<IEmailResendLogRepository, EmailResendLogRepository>();

            return services;
        }

        /// <summary>
        /// Registra todos os serviços de aplicação (camada de negócio).
        /// </summary>
        private static IServiceCollection RegisterServices(this IServiceCollection services)
        {
            services.AddScoped<IUserService, UserService>();
            services.AddScoped<IGameService, GameService>();
            services.AddScoped<IGameSessionService, GameSessionService>();
            services.AddScoped<IMatchService, MatchService>();
            services.AddScoped<IMatchPlayerService, MatchPlayerService>();
            services.AddScoped<IUserGameLibraryService, UserGameLibraryService>();
            services.AddScoped<IEmailService, EmailService>();
            services.AddScoped<IAuthService, AuthService>();
            services.AddScoped<ITokenService, TokenService>(); // 🔐 TokenService com suporte a Refresh Token
            services.AddHttpClient<IBGGService, BGGService>(); // Integração com BoardGameGeek

            services.AddScoped<UserCleanupJob>();

            return services;
        }

        /// <summary>
        /// Configura o AutoMapper com o perfil de mapeamento.
        /// </summary>
        private static IServiceCollection ConfigureAutoMapper(this IServiceCollection services)
        {
            services.AddAutoMapper(typeof(AutoMapperConfig));
            return services;
        }

        /// <summary>
        /// Registra validadores do FluentValidation.
        /// </summary>
        private static IServiceCollection ConfigureValidators(this IServiceCollection services)
        {
            services.AddValidatorsFromAssemblyContaining<UserValidator>();
            return services;
        }

        /// <summary>
        /// Configura proteção contra abusos com Rate Limiting baseado em IP.
        /// </summary>
        private static IServiceCollection ConfigureRateLimiting(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<IpRateLimitOptions>(configuration.GetSection("RateLimiting"));
            services.Configure<IpRateLimitPolicies>(configuration.GetSection("RateLimiting"));

            services.AddMemoryCache(); // Necessário para armazenar contadores de rate limit
            services.AddInMemoryRateLimiting();

            services.AddSingleton<IIpPolicyStore, MemoryCacheIpPolicyStore>();
            services.AddSingleton<IRateLimitCounterStore, MemoryCacheRateLimitCounterStore>();
            services.AddSingleton<IProcessingStrategy, AsyncKeyLockProcessingStrategy>();
            services.AddSingleton<IRateLimitConfiguration, RateLimitConfiguration>();

            return services;
        }
    }
}