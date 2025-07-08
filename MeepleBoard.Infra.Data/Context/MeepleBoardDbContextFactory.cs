using MeepleBoard.Infra.Data.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace MeepleBoard.Infra.Data
{
    /// <summary>
    /// Fábrica para criar o contexto do banco de dados em tempo de design (migrations).
    /// </summary>
    public class MeepleBoardDbContextFactory : IDesignTimeDbContextFactory<MeepleBoardDbContext>
    {
        public MeepleBoardDbContext CreateDbContext(string[] args)
        {
            // 🔹 Define o diretório base para buscar os arquivos de configuração
            var basePath = Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), "../MeepleBoardApi"));
            var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

            // 🔹 Carrega as configurações do arquivo appsettings.json
            var configuration = new ConfigurationBuilder()
                .SetBasePath(basePath)
                .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                .AddJsonFile($"appsettings.{environment}.json", optional: true)
                .AddEnvironmentVariables()
                .Build();

            var connectionString = configuration.GetConnectionString("DefaultConnection");

            if (string.IsNullOrEmpty(connectionString))
            {
                throw new InvalidOperationException("Connection string 'DefaultConnection' não encontrada.");
            }

            // 🔹 Configura o DbContextOptionsBuilder com SQL Server
            var optionsBuilder = new DbContextOptionsBuilder<MeepleBoardDbContext>();
            optionsBuilder.UseSqlServer(connectionString, options =>
            {
                options.EnableRetryOnFailure(5); // 🔹 Configura tentativas automáticas de reconexão
            });

            return new MeepleBoardDbContext(optionsBuilder.Options);
        }
    }
}