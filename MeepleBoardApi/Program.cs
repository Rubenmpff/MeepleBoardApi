using AspNetCoreRateLimit;
using Hangfire;
using MeepleBoard.CrossCutting.IoC;
using MeepleBoard.CrossCutting.Middlewares;
using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Infra.Data.Context;
using MeepleBoard.Services.Interfaces;
using MeepleBoard.Services.Settings;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using System.Net;
using System.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);
var configuration = builder.Configuration;

// 🔐 Carrega e valida a chave JWT (User Secrets em DEV / Env Vars em PROD)
var jwtKey = configuration["JWT_KEY"];
if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Length < 32)
    throw new InvalidOperationException("❌ A chave JWT_KEY não foi encontrada ou é muito curta.");

// ⚠️ Evita logar segredos em produção
if (builder.Environment.IsDevelopment())
{
    Console.WriteLine($"🔑 JWT_KEY carregada: {jwtKey.Substring(0, 10)}******");
}


// ✅ Configuração do JWT
builder.Services.Configure<JwtSettings>(options =>
{
    options.Key = jwtKey;
    options.Issuer = configuration["Jwt:Issuer"] ?? "MeepleBoardIssuer";
    options.Audience = configuration["Jwt:Audience"] ?? "MeepleBoardAudience";
    options.ExpiryHours = int.TryParse(configuration["Jwt:ExpiryHours"], out var expiry) ? expiry : 24;
});

// ✅ CORS (libera geral apenas em DEV)
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader());
});

// ✅ Serviços da aplicação
builder.Services.AddInfrastructure(configuration);
builder.Services.AddIdentityConfiguration(configuration);
builder.Services.AddOAuthProviders(configuration);

// ✅ Banco de dados EF Core
builder.Services.AddDbContext<MeepleBoardDbContext>(options =>
    options.UseSqlServer(configuration.GetConnectionString("DefaultConnection")));

// ✅ Controllers e Swagger
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "MeepleBoard API",
        Version = "v1",
        Description = "API para gerenciamento de Meeple Boards",
        Contact = new OpenApiContact
        {
            Name = "MeepleBoard Team",
            Email = "contato@meepleboard.com",
            Url = new Uri("https://github.com/meepleboard")
        }
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "Insira o token JWT no campo abaixo (Bearer <seu_token>)",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = "Bearer" }
            },
            Array.Empty<string>()
        }
    });
});

// ✅ Configura o HttpClient para descomprimir automaticamente respostas GZIP do BGG
builder.Services.AddHttpClient<IBGGService, BGGService>((sp, client) =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    // Base URL SEM www
    client.BaseAddress = new Uri(config["Bgg:BaseUrl"] ?? "https://boardgamegeek.com/xmlapi2/");

    // Token vindo de config/user-secrets/env var
    var token = config["Bgg:Token"];
    if (!string.IsNullOrWhiteSpace(token))
    {
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", token);
    }

    client.DefaultRequestHeaders.UserAgent.ParseAdd("MeepleBoard/1.0");
});

// ✅ Hangfire Configuração (armazenamento em SQL Server)
builder.Services.AddHangfire(config =>
    config.SetDataCompatibilityLevel(CompatibilityLevel.Version_170)
          .UseSimpleAssemblyNameTypeSerializer()
          .UseRecommendedSerializerSettings()
          .UseSqlServerStorage(configuration.GetConnectionString("DefaultConnection")));

builder.Services.AddHangfireServer();
builder.Services.AddScoped<UserCleanupJob>(); // 👈 Registra o job como service

// ✅ Constrói a aplicação
var app = builder.Build();

// 🔃 Agendamento do Job recorrente com Hangfire
app.UseHangfireDashboard("/hangfire"); // Interface do painel
RecurringJob.AddOrUpdate<UserCleanupJob>(
    "cleanup-unconfirmed-users",
    job => job.ExecuteAsync(),
    Cron.Daily); // Executa diariamente

// ✅ Seeding de Admin e User
await UserSeeder.SeedAdminAndUserAsync(app.Services);

// ✅ Middlewares personalizados
app.UseMiddleware<ExceptionMiddleware>(); // Captura erros globais

// 🔄 Middleware de renovação de token
app.Use(async (context, next) =>
{
    using var scope = app.Services.CreateScope();
    var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<TokenAutoRefreshMiddleware>>();
    var middleware = new TokenAutoRefreshMiddleware(next, tokenService, logger);
    await middleware.InvokeAsync(context);
});

// ✅ Swagger só em DEV
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "MeepleBoard API v1");
        options.RoutePrefix = string.Empty;
    });
}

// ✅ Segurança e Middleware final
// ✅ CORS apenas em Development
if (app.Environment.IsDevelopment())
{
    app.UseCors("AllowAll");
}

// app.UseHttpsRedirection(); // Habilita em produção
app.UseIpRateLimiting();
app.UseAuthentication();
app.UseAuthorization();

// ✅ Endpoints
app.MapControllers();
app.MapGet("/", () => "MeepleBoard API está rodando com sucesso! 🚀");

// ✅ Roda a aplicação
app.Run();