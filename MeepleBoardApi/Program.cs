using AspNetCoreRateLimit;
using Hangfire;
using Hangfire.Dashboard;
using MeepleBoard.CrossCutting.IoC;
using MeepleBoard.CrossCutting.Middlewares;
using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Domain.Interfaces;
using MeepleBoard.Infra.Data.Context;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Job;
using MeepleBoard.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// ======================================================
// AMBIENTES
// ======================================================

var isDevelopment = builder.Environment.IsDevelopment();
var isQa = builder.Environment.IsEnvironment("QA");
var isProduction = builder.Environment.IsProduction();

// ======================================================
// CONFIGURAÇÃO JWT
// ======================================================

var jwtKey = configuration["JWT_KEY"];

if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
{
    throw new InvalidOperationException(
        "A variável JWT_KEY não foi encontrada ou tem menos de 32 caracteres.");
}

if (isDevelopment)
{
    Console.WriteLine("JWT_KEY carregada e validada com sucesso.");
}

builder.Services.Configure<JwtSettings>(options =>
{
    options.Key = jwtKey;

    options.Issuer =
        configuration["Jwt:Issuer"]
        ?? "MeepleBoardIssuer";

    options.Audience =
        configuration["Jwt:Audience"]
        ?? "MeepleBoardAudience";

    options.ExpiryHours =
        int.TryParse(
            configuration["Jwt:ExpiryHours"],
            out var expiryHours)
            ? expiryHours
            : 24;
});

// ======================================================
// CORS
// ======================================================

builder.Services.AddCors(options =>
{
    // --------------------------------------------------
    // DEVELOPMENT / QA
    // --------------------------------------------------
    //
    // Durante desenvolvimento e testes permitimos
    // qualquer origem para facilitar testes locais,
    // Expo, Postman, Swagger, etc.

    options.AddPolicy("DevelopmentCors", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });

    // --------------------------------------------------
    // PRODUCTION
    // --------------------------------------------------
    //
    // Em produção apenas serão permitidas origens
    // configuradas explicitamente.
    //
    // Exemplo no Azure:
    //
    // Cors__AllowedOrigins__0 = https://meepleboard.com
    // Cors__AllowedOrigins__1 = https://www.meepleboard.com

    options.AddPolicy("ProductionCors", policy =>
    {
        var allowedOrigins =
            configuration
                .GetSection("Cors:AllowedOrigins")
                .Get<string[]>()
            ?? Array.Empty<string>();

        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyMethod()
                .AllowAnyHeader();
        }
    });
});

// ======================================================
// SERVIÇOS DA APLICAÇÃO
// ======================================================

builder.Services.AddInfrastructure(configuration);
builder.Services.AddIdentityConfiguration(configuration);
builder.Services.AddOAuthProviders(configuration);

// ======================================================
// BASE DE DADOS
// ======================================================

var connectionString =
    configuration.GetConnectionString("DefaultConnection");

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        "A connection string DefaultConnection não foi encontrada.");
}

// ======================================================
// CONTROLLERS
// ======================================================

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// ======================================================
// SWAGGER
// ======================================================

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc(
        "v1",
        new OpenApiInfo
        {
            Title = "MeepleBoard API",
            Version = "v1",
            Description = "API da aplicação MeepleBoard"
        });

    options.AddSecurityDefinition(
        "Bearer",
        new OpenApiSecurityScheme
        {
            Name = "Authorization",
            Description = "Introduz o token JWT.",
            In = ParameterLocation.Header,
            Type = SecuritySchemeType.Http,
            Scheme = "bearer",
            BearerFormat = "JWT"
        });

    options.AddSecurityRequirement(
        new OpenApiSecurityRequirement
        {
            {
                new OpenApiSecurityScheme
                {
                    Reference = new OpenApiReference
                    {
                        Type = ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
        });
});

// ======================================================
// BGG HTTP CLIENT
// ======================================================

builder.Services.AddHttpClient<IBGGService, BGGService>(
    (serviceProvider, client) =>
    {
        var config =
            serviceProvider.GetRequiredService<IConfiguration>();

        var baseUrl =
            config["Bgg:BaseUrl"]
            ?? "https://boardgamegeek.com/xmlapi2/";

        if (!Uri.TryCreate(
                baseUrl,
                UriKind.Absolute,
                out var parsedBaseUrl))
        {
            throw new InvalidOperationException(
                "O valor Bgg:BaseUrl não é um URL válido.");
        }

        client.BaseAddress = parsedBaseUrl;

        var bggToken = config["Bgg:Token"];

        if (!string.IsNullOrWhiteSpace(bggToken))
        {
            client.DefaultRequestHeaders.Authorization =
                new AuthenticationHeaderValue(
                    "Bearer",
                    bggToken);
        }

        client.DefaultRequestHeaders.UserAgent.ParseAdd(
            "MeepleBoard/1.0");
    });

// ======================================================
// HANGFIRE
// ======================================================

builder.Services.AddHangfire(config =>
{
    config
        .SetDataCompatibilityLevel(
            CompatibilityLevel.Version_170)
        .UseSimpleAssemblyNameTypeSerializer()
        .UseRecommendedSerializerSettings()
        .UseSqlServerStorage(connectionString);
});

// Mantém o processamento dos jobs ativo.
//
// Separamos os jobs normais da importação pesada do catálogo BGG.
// Desta forma, o catálogo nunca ocupa os workers utilizados pelos
// cleanups e pelos restantes jobs da aplicação.
//
// Os valores podem ser sobrescritos por configuração, por exemplo:
// Hangfire:DefaultWorkerCount
// Hangfire:BggCatalogWorkerCount
//
// Os defaults mantêm um total máximo de 4 workers neste ambiente:
// 3 para a fila normal + 1 dedicado ao catálogo BGG.
var defaultHangfireWorkerCount =
    Math.Max(
        1,
        configuration.GetValue<int?>(
            "Hangfire:DefaultWorkerCount")
        ?? 3);

var bggCatalogWorkerCount =
    Math.Max(
        1,
        configuration.GetValue<int?>(
            "Hangfire:BggCatalogWorkerCount")
        ?? 1);

// Jobs normais da aplicação.
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount =
        defaultHangfireWorkerCount;

    options.Queues =
    [
        "default"
    ];
});

// Importação pesada do catálogo BGG.
//
// A queue tem, por defeito, apenas um worker.
// Em conjunto com DisableConcurrentExecution no job,
// isto dá-nos duas camadas de proteção contra concorrência.
builder.Services.AddHangfireServer(options =>
{
    options.WorkerCount =
        bggCatalogWorkerCount;

    options.Queues =
    [
        "bgg-catalog"
    ];
});

builder.Services.AddScoped<UserCleanupJob>();
builder.Services.AddScoped<SessionCleanupJob>();
builder.Services.AddScoped<MatchCleanupJob>();
builder.Services.AddScoped<BGGSyncJob>();
builder.Services.AddScoped<BggGameCatalogImportJob>();

// ======================================================
// CREDENCIAIS DO DASHBOARD DO HANGFIRE
// ======================================================
//
// O Dashboard só está ativo em Development e QA.
//
// Por isso só exigimos estas credenciais nesses ambientes.
//
// Em Azure QA:
// HangfireDashboard__Username
// HangfireDashboard__Password

string? hangfireUsername = null;
string? hangfirePassword = null;

if (isDevelopment || isQa)
{
    hangfireUsername =
        configuration["HangfireDashboard:Username"];

    hangfirePassword =
        configuration["HangfireDashboard:Password"];

    if (string.IsNullOrWhiteSpace(hangfireUsername))
    {
        throw new InvalidOperationException(
            "HangfireDashboard:Username não está configurado.");
    }

    if (string.IsNullOrWhiteSpace(hangfirePassword)
        || hangfirePassword.Length < 16)
    {
        throw new InvalidOperationException(
            "HangfireDashboard:Password não está configurada ou tem menos de 16 caracteres.");
    }
}

// ======================================================
// CONSTRUÇÃO DA APLICAÇÃO
// ======================================================

var app = builder.Build();

// ======================================================
// TRATAMENTO GLOBAL DE ERROS
// ======================================================

app.UseMiddleware<ExceptionMiddleware>();

// ======================================================
// SWAGGER
// ======================================================
//
// Development: ✅
// QA:          ✅
// Production:  ❌
//
// Em produção o Swagger fica desligado publicamente.

if (isDevelopment || isQa)
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint(
            "/swagger/v1/swagger.json",
            "MeepleBoard API v1");

        options.RoutePrefix = string.Empty;
    });
}

// ======================================================
// CORS
// ======================================================

if (isDevelopment || isQa)
{
    app.UseCors("DevelopmentCors");
}
else if (isProduction)
{
    app.UseCors("ProductionCors");
}

// ======================================================
// HTTPS
// ======================================================
//
// Em Azure, o HTTPS é tratado pelo App Service.
//
// Mantém-se comentado enquanto também utilizarmos
// ambientes locais/túneis que possam necessitar disso.
//
// app.UseHttpsRedirection();

// ======================================================
// RATE LIMITING
// ======================================================
//
// ⚠️ TEMPORARIAMENTE DESATIVADO.
//
// Antes de Production devemos rever a configuração
// e voltar a ativar o rate limiting.
//
// app.UseIpRateLimiting();

// ======================================================
// AUTENTICAÇÃO E AUTORIZAÇÃO
// ======================================================

app.UseAuthentication();
app.UseAuthorization();

// ======================================================
// HANGFIRE DASHBOARD
// ======================================================
//
// IMPORTANTE:
//
// O Hangfire Server e os jobs continuam ativos em
// Development, QA e Production.
//
// Este bloco controla APENAS o painel visual /hangfire.
//
// Development: ✅ protegido
// QA:          ✅ protegido
// Production:  ❌ por enquanto
//
// TODO PRODUCTION:
// Quando o MeepleBoard for colocado em produção,
// avaliar a ativação do Dashboard apenas depois de
// adicionar uma camada adicional de segurança no Azure,
// por exemplo:
//
// - Azure App Service Access Restrictions / IP allowlist
// - VPN ou acesso privado
// - autenticação administrativa adicional
//
// Não expor o Dashboard diretamente na Internet em
// Production apenas com Basic Authentication.

if (isDevelopment || isQa)
{
    app.UseHangfireDashboard(
        "/hangfire",
        new DashboardOptions
        {
            Authorization =
            [
                new HangfireDashboardBasicAuthFilter(
                    hangfireUsername!,
                    hangfirePassword!)
            ],

            IsReadOnlyFunc = _ => false,

            DashboardTitle = "MeepleBoard Hangfire"
        });
}

// ======================================================
// RENOVAÇÃO AUTOMÁTICA DO TOKEN + LAST ACTIVE
// ======================================================
//
// Estes middlewares só são aplicados aos endpoints
// que começam por /MeepleBoard.
//
// Assim não interferem com:
//
// - /hangfire
// - /swagger
// - /index.html
// - /

app.UseWhen(
    context =>
        context.Request.Path.StartsWithSegments(
            "/MeepleBoard"),
    branch =>
    {
        // --------------------------------------------------
        // RENOVAÇÃO AUTOMÁTICA DO TOKEN
        // --------------------------------------------------

        branch.Use(async (context, next) =>
        {
            using var scope =
                app.Services.CreateScope();

            var tokenService =
                scope.ServiceProvider
                    .GetRequiredService<ITokenService>();

            var logger =
                scope.ServiceProvider
                    .GetRequiredService<
                        ILogger<TokenAutoRefreshMiddleware>>();

            var tokenMiddleware =
                new TokenAutoRefreshMiddleware(
                    next,
                    tokenService,
                    logger);

            await tokenMiddleware.InvokeAsync(context);
        });

        // --------------------------------------------------
        // ÚLTIMA ATIVIDADE DO UTILIZADOR
        // --------------------------------------------------
        //
        // Regista a última atividade do utilizador
        // autenticado.
        //
        // Serve para o indicador "online" nos ecrãs
        // de Amigos.

        branch.Use(async (context, next) =>
        {
            using var scope =
                app.Services.CreateScope();

            var userRepository =
                scope.ServiceProvider
                    .GetRequiredService<IUserRepository>();

            var lastActiveLogger =
                scope.ServiceProvider
                    .GetRequiredService<
                        ILogger<LastActiveMiddleware>>();

            var lastActiveMiddleware =
                new LastActiveMiddleware(
                    next,
                    userRepository,
                    lastActiveLogger);

            await lastActiveMiddleware.InvokeAsync(context);
        });
    });

// ======================================================
// ENDPOINTS
// ======================================================

app.MapControllers();

app.MapGet(
    "/",
    () => Results.Ok(
        new
        {
            message = "MeepleBoard API está a funcionar."
        }));

// ======================================================
// JOBS RECORRENTES
// ======================================================
//
// Estes jobs são registados em TODOS os ambientes:
//
// Development ✅
// QA          ✅
// Production  ✅

using (var scope = app.Services.CreateScope())
{
    var recurringJobManager =
        scope.ServiceProvider
            .GetRequiredService<IRecurringJobManager>();

    recurringJobManager.AddOrUpdate<UserCleanupJob>(
        "cleanup-unconfirmed-users",
        job => job.ExecuteAsync(),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<SessionCleanupJob>(
        "session-cleanup",
        job => job.ExecuteAsync(
            CancellationToken.None),
        Cron.Hourly);

    recurringJobManager.AddOrUpdate<MatchCleanupJob>(
        "match-cleanup",
        job => job.ExecuteAsync(
            CancellationToken.None),
        Cron.Hourly);

    recurringJobManager.AddOrUpdate<BGGSyncJob>(
        "bgg-sync",
        job => job.ExecuteAsync(
            CancellationToken.None),
        Cron.Daily);

    recurringJobManager.AddOrUpdate<BggGameCatalogImportJob>(
        "bgg-game-catalog-import",
        job => job.ExecuteAsync(
            CancellationToken.None),
        Cron.Daily);
}

// ======================================================
// SEED DA BASE DE DADOS
// ======================================================

// As Roles são necessárias em qualquer ambiente.
//
// Development ✅
// QA          ✅
// Production  ✅

await UserSeeder.SeedRolesAsync(app.Services);

// Os utilizadores de TESTE só são criados em:
//
// Development ✅
// QA          ✅
// Production  ❌

if (isDevelopment || isQa)
{
    await UserSeeder.SeedTestUsersAsync(
        app.Services,
        configuration);
}

// ======================================================
// EXECUÇÃO
// ======================================================

app.Run();

// ======================================================
// AUTENTICAÇÃO BASIC DO DASHBOARD HANGFIRE
// ======================================================

public sealed class HangfireDashboardBasicAuthFilter
    : IDashboardAuthorizationFilter
{
    private readonly string _username;
    private readonly string _password;

    public HangfireDashboardBasicAuthFilter(
        string username,
        string password)
    {
        _username = username
            ?? throw new ArgumentNullException(
                nameof(username));

        _password = password
            ?? throw new ArgumentNullException(
                nameof(password));
    }

    public bool Authorize(
        DashboardContext context)
    {
        var httpContext =
            context.GetHttpContext();

        var authorizationHeader =
            httpContext.Request
                .Headers
                .Authorization
                .FirstOrDefault();

        if (string.IsNullOrWhiteSpace(
                authorizationHeader)
            || !authorizationHeader.StartsWith(
                "Basic ",
                StringComparison.OrdinalIgnoreCase))
        {
            RequestAuthentication(httpContext);

            return false;
        }

        try
        {
            var encodedCredentials =
                authorizationHeader[
                    "Basic ".Length..]
                .Trim();

            var decodedBytes =
                Convert.FromBase64String(
                    encodedCredentials);

            var decodedCredentials =
                Encoding.UTF8.GetString(
                    decodedBytes);

            var separatorIndex =
                decodedCredentials.IndexOf(':');

            if (separatorIndex <= 0)
            {
                RequestAuthentication(httpContext);

                return false;
            }

            var suppliedUsername =
                decodedCredentials[
                    ..separatorIndex];

            var suppliedPassword =
                decodedCredentials[
                    (separatorIndex + 1)..];

            var usernameMatches =
                SecureEquals(
                    suppliedUsername,
                    _username);

            var passwordMatches =
                SecureEquals(
                    suppliedPassword,
                    _password);

            if (!usernameMatches
                || !passwordMatches)
            {
                RequestAuthentication(httpContext);

                return false;
            }

            return true;
        }
        catch (FormatException)
        {
            RequestAuthentication(httpContext);

            return false;
        }
        catch (ArgumentException)
        {
            RequestAuthentication(httpContext);

            return false;
        }
    }

    private static void RequestAuthentication(
        HttpContext httpContext)
    {
        httpContext.Response.StatusCode =
            StatusCodes.Status401Unauthorized;

        httpContext.Response.Headers
            .WWWAuthenticate =
            "Basic realm=\"MeepleBoard Hangfire\", charset=\"UTF-8\"";
    }

    private static bool SecureEquals(
        string suppliedValue,
        string expectedValue)
    {
        var suppliedBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    suppliedValue));

        var expectedBytes =
            SHA256.HashData(
                Encoding.UTF8.GetBytes(
                    expectedValue));

        return CryptographicOperations
            .FixedTimeEquals(
                suppliedBytes,
                expectedBytes);
    }
}