using MeepleBoard.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MeepleBoard.Infra.Data.Context
{
    public class MeepleBoardDbContext : IdentityDbContext<User, IdentityRole<Guid>, Guid>
    {
        public MeepleBoardDbContext(DbContextOptions<MeepleBoardDbContext> options)
            : base(options)
        {
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll; // 🔹 Rastreia alterações por padrão
        }

        public DbSet<Game> Games { get; set; }
        public DbSet<Match> Matches { get; set; }
        public DbSet<MatchPlayer> MatchPlayers { get; set; }
        public DbSet<UserGameLibrary> UserGameLibraries { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<EmailResendLog> EmailResendLogs { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // 🔹 Aplica configurações via Fluent API (ex: GameConfiguration.cs, etc)
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeepleBoardDbContext).Assembly);

            // 🔗 MatchPlayer → User
            modelBuilder.Entity<MatchPlayer>()
                .HasOne(mp => mp.User)
                .WithMany(u => u.Matches)
                .HasForeignKey(mp => mp.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            // 🔗 Match → Game
            modelBuilder.Entity<Match>()
                .HasOne(m => m.Game)
                .WithMany(g => g.Matches)
                .HasForeignKey(m => m.GameId)
                .OnDelete(DeleteBehavior.Restrict);

            // 🔗 MatchPlayer → Match
            modelBuilder.Entity<MatchPlayer>()
                .HasOne(mp => mp.Match)
                .WithMany(m => m.MatchPlayers)
                .HasForeignKey(mp => mp.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            // 🔐 Garante um jogador por partida (único)
            modelBuilder.Entity<MatchPlayer>()
                .HasIndex(mp => new { mp.MatchId, mp.UserId })
                .IsUnique();

            // 🔗 UserGameLibrary → User
            modelBuilder.Entity<UserGameLibrary>()
                .HasOne(ug => ug.User)
                .WithMany(u => u.UserGameLibraries)
                .HasForeignKey(ug => ug.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 🔗 UserGameLibrary → Game
            modelBuilder.Entity<UserGameLibrary>()
                .HasOne(ug => ug.Game)
                .WithMany(g => g.UserGameLibraries)
                .HasForeignKey(ug => ug.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            // 🔗 RefreshToken → User
            modelBuilder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // 🧩 Game ↔ Expansions (self-referencing)
            modelBuilder.Entity<Game>()
                .HasMany(g => g.Expansions)
                .WithOne(g => g.BaseGame)
                .HasForeignKey(g => g.BaseGameId)
                .OnDelete(DeleteBehavior.Restrict); // Evita exclusão em cascata

            // 📝 Limite de tamanho para descrições grandes
            modelBuilder.Entity<Game>()
                .Property(g => g.Description)
                .HasMaxLength(10000);

            // ⚠️ (Opcional) Garante nome único de jogo
            // modelBuilder.Entity<Game>()
            //     .HasIndex(g => g.Name)
            //     .IsUnique();
        }
    }
}