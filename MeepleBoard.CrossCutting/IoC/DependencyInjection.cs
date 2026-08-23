using AspNetCoreRateLimit;
using FluentValidation;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using MeepleBoard.Infra.Data.Repositories;
using MeepleBoard.Services.ExternalServices.Implementations;
using MeepleBoard.Services.ExternalServices.Interfaces;
using MeepleBoard.Services.Implementations;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Job;
using MeepleBoard.Services.Validator;
using MeepleBoardApi.Services.Mapping.AutoMapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net.Http.Headers;

namespace MeepleBoard.CrossCutting.IoC
{
    public static class DependencyInjection
    {
        /// <summary>
        /// Centraliza toda a injeção de dependência da aplicação.
        /// </summary>
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            return services
                .ConfigureDatabase(configuration)
                .RegisterRepositories()
                .RegisterServices(configuration)
                .ConfigureAutoMapper()
                .ConfigureValidators()
                .ConfigureRateLimiting(configuration);
        }

        /// <summary>
        /// Configura o banco de dados com EF Core e SQL Server.
        /// </summary>
        private static IServiceCollection ConfigureDatabase(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.AddDbContext<MeepleBoardDbContext>(options =>
                options.UseSqlServer(
                    configuration.GetConnectionString("DefaultConnection"),
                    sqlOptions =>
                    {
                        sqlOptions.CommandTimeout(120);

                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 5,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null);
                    }));

            return services;
        }

        /// <summary>
        /// Regista todos os repositórios do domínio.
        /// </summary>
        private static IServiceCollection RegisterRepositories(
            this IServiceCollection services)
        {
            services.AddScoped<
                IUserRepository,
                UserRepository>();

            services.AddScoped<
                IGameRepository,
                GameRepository>();

            services.AddScoped<
                IGameSearchCatalogRepository,
                GameSearchCatalogRepository>();

            services.AddScoped<
                IGameSessionRepository,
                GameSessionRepository>();

            services.AddScoped<
                IGameSessionPlayerRepository,
                GameSessionPlayerRepository>();

            services.AddScoped<
                IMatchRepository,
                MatchRepository>();

            services.AddScoped<
                IMatchPlayerRepository,
                MatchPlayerRepository>();

            services.AddScoped<
                IUserGameLibraryRepository,
                UserGameLibraryRepository>();

            services.AddScoped<
                IRefreshTokenRepository,
                RefreshTokenRepository>();

            services.AddScoped<
                IEmailResendLogRepository,
                EmailResendLogRepository>();

            services.AddScoped<
                IFriendshipRepository,
                FriendshipRepository>();

            services.AddScoped<
                ICampaignRepository,
                CampaignRepository>();

            return services;
        }

        /// <summary>
        /// Regista todos os serviços de aplicação
        /// e integrações HTTP externas.
        /// </summary>
        private static IServiceCollection RegisterServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            /* =========================================================
               SERVIÇOS DE APLICAÇÃO
            ========================================================== */

            services.AddScoped<
                IUserService,
                UserService>();

            services.AddScoped<
                IGameService,
                GameService>();

            services.AddScoped<
                IGameSearchCatalogService,
                GameSearchCatalogService>();

            services.AddScoped<
                IGameSessionService,
                GameSessionService>();

            services.AddScoped<
                IMatchService,
                MatchService>();

            services.AddScoped<
                IMatchPlayerService,
                MatchPlayerService>();

            services.AddScoped<
                IUserGameLibraryService,
                UserGameLibraryService>();

            services.AddScoped<
                IEmailService,
                EmailService>();

            services.AddScoped<
                IAuthService,
                AuthService>();

            services.AddScoped<
                ITokenService,
                TokenService>();

            services.AddScoped<
                IFriendshipService,
                FriendshipService>();

            services.AddScoped<
                ICampaignService,
                CampaignService>();

            /* =========================================================
               SERVIÇOS EXTERNOS
            ========================================================== */

            services.AddSingleton<
                IPhotoStorageService,
                CloudinaryPhotoStorageService>();

            services.AddHttpClient<
                INotificationService,
                NotificationService>();

            /*
             * Importação do catálogo oficial do BoardGameGeek.
             *
             * Reutiliza:
             * - Bgg:Token
             * - Bgg:CatalogPageUrl
             *
             * Bgg:BaseUrl continua reservado para a XML API.
             */
            services.AddHttpClient<
                IBggGameCatalogImportService,
                BggGameCatalogImportService>(
                client =>
                {
                    var catalogPageUrl =
                        configuration["Bgg:CatalogPageUrl"];

                    var token =
                        configuration["Bgg:Token"];

                    if (Uri.TryCreate(
                        catalogPageUrl,
                        UriKind.Absolute,
                        out var catalogUri))
                    {
                        client.BaseAddress =
                            catalogUri;
                    }

                    if (!string.IsNullOrWhiteSpace(token))
                    {
                        client.DefaultRequestHeaders.Authorization =
                            new AuthenticationHeaderValue(
                                "Bearer",
                                token.Trim());
                    }

                    client.Timeout =
                        TimeSpan.FromMinutes(5);
                });

            /* =========================================================
               JOBS HANGFIRE
            ========================================================== */

            services.AddScoped<UserCleanupJob>();

            services.AddScoped<SessionCleanupJob>();

            services.AddScoped<MatchCleanupJob>();

            services.AddScoped<BGGSyncJob>();

            services.AddScoped<BggGameCatalogImportJob>();

            return services;
        }

        /// <summary>
        /// Configura o AutoMapper com o perfil de mapeamento.
        /// </summary>
        private static IServiceCollection ConfigureAutoMapper(
            this IServiceCollection services)
        {
            services.AddAutoMapper(
                typeof(AutoMapperConfig));

            return services;
        }

        /// <summary>
        /// Regista validadores do FluentValidation.
        /// </summary>
        private static IServiceCollection ConfigureValidators(
            this IServiceCollection services)
        {
            services.AddValidatorsFromAssemblyContaining<
                UserValidator>();

            return services;
        }

        /// <summary>
        /// Configura proteção contra abusos
        /// com Rate Limiting baseado em IP.
        /// </summary>
        private static IServiceCollection ConfigureRateLimiting(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<IpRateLimitOptions>(
                configuration.GetSection(
                    "RateLimiting"));

            services.Configure<IpRateLimitPolicies>(
                configuration.GetSection(
                    "RateLimiting"));

            services.AddMemoryCache();

            services.AddInMemoryRateLimiting();

            services.AddSingleton<
                IIpPolicyStore,
                MemoryCacheIpPolicyStore>();

            services.AddSingleton<
                IRateLimitCounterStore,
                MemoryCacheRateLimitCounterStore>();

            services.AddSingleton<
                IProcessingStrategy,
                AsyncKeyLockProcessingStrategy>();

            services.AddSingleton<
                IRateLimitConfiguration,
                RateLimitConfiguration>();

            return services;
        }
    }
}