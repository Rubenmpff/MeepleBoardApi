using System;
using System.IO;
using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MeepleBoard.Infra.Data
{
    /// <summary>
    /// Factory utilizada pelo Entity Framework em tempo de design (migrations).
    /// Carrega:
    /// - appsettings.json
    /// - appsettings.{Environment}.json
    /// - User Secrets (em Development)
    /// - Variáveis de ambiente
    /// 
    /// Funciona em Windows e Mac.
    /// </summary>
    public class MeepleBoardDbContextFactory : IDesignTimeDbContextFactory<MeepleBoardDbContext>
    {
        public MeepleBoardDbContext CreateDbContext(string[] args)
        {
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            // 🔹 Caminho até o projeto da API
            var basePath = Path.GetFullPath(
                Path.Combine(Directory.GetCurrentDirectory(), "../MeepleBoardApi")
            );

            var builder = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false)
                .AddJsonFile($"appsettings.{environment}.json", optional: true);

            // 🔐 Em Development, carregar User Secrets
            if (environment == "Development")
            {
                builder.AddUserSecrets("cd050e39-77b3-43af-a903-3acaa804ef4c");                
            }

            builder.AddEnvironmentVariables();

            var configuration = builder.Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' não encontrada. " +
                    "Verifique appsettings, User Secrets ou variáveis de ambiente."
                );
            }

            var optionsBuilder = new DbContextOptionsBuilder<MeepleBoardDbContext>();
            optionsBuilder.UseSqlServer(connectionString, options =>
            {
                options.EnableRetryOnFailure(5);
            });

            return new MeepleBoardDbContext(optionsBuilder.Options);
        }
    }
}
