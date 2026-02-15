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
            // Mantém tracking por defeito (precisas disto para Unit of Work/Updates)
            ChangeTracker.QueryTrackingBehavior = QueryTrackingBehavior.TrackAll;
        }

        public DbSet<Game> Games { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
        public DbSet<GameSessionPlayer> GameSessionPlayers { get; set; }
        public DbSet<Match> Matches { get; set; }
        public DbSet<MatchPlayer> MatchPlayers { get; set; }
        public DbSet<UserGameLibrary> UserGameLibraries { get; set; }
        public DbSet<RefreshToken> RefreshTokens { get; set; }
        public DbSet<EmailResendLog> EmailResendLogs { get; set; }
        public DbSet<Friendship> Friendships { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Aplica configurações por assembly (se tiveres IEntityTypeConfiguration<>)
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(MeepleBoardDbContext).Assembly);

            /* --------------------------- MATCH / PLAYERS --------------------------- */

            modelBuilder.Entity<MatchPlayer>()
                .HasOne(mp => mp.User)
                .WithMany(u => u.Matches)
                .HasForeignKey(mp => mp.UserId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Match>()
                .HasOne(m => m.Game)
                .WithMany(g => g.Matches)
                .HasForeignKey(m => m.GameId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<MatchPlayer>()
                .HasOne(mp => mp.Match)
                .WithMany(m => m.MatchPlayers)
                .HasForeignKey(mp => mp.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<MatchPlayer>()
                .HasIndex(mp => new { mp.MatchId, mp.UserId })
                .IsUnique();

            /* ------------------------- USER GAME LIBRARY -------------------------- */

            modelBuilder.Entity<UserGameLibrary>()
                .HasOne(ug => ug.User)
                .WithMany(u => u.UserGameLibraries)
                .HasForeignKey(ug => ug.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<UserGameLibrary>()
                .HasOne(ug => ug.Game)
                .WithMany(g => g.UserGameLibraries)
                .HasForeignKey(ug => ug.GameId)
                .OnDelete(DeleteBehavior.Cascade);

            /* ------------------------------ TOKENS -------------------------------- */

            modelBuilder.Entity<RefreshToken>()
                .HasOne(rt => rt.User)
                .WithMany(u => u.RefreshTokens)
                .HasForeignKey(rt => rt.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            /* ------------------------------- GAMES -------------------------------- */

            modelBuilder.Entity<Game>()
                .HasMany(g => g.Expansions)
                .WithOne(g => g.BaseGame)
                .HasForeignKey(g => g.BaseGameId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<Game>()
                .Property(g => g.Description)
                .HasMaxLength(10_000);

            /* ---------------------------- FRIENDSHIPS ----------------------------- */

            modelBuilder.Entity<Friendship>(b =>
            {
                b.HasKey(x => x.Id);

                // Par ordenado (A,B) único — evitar duplicados (A,B) / (B,A)
                b.HasIndex(x => new { x.UserAId, x.UserBId }).IsUnique();

                // Check: A != B (API moderna via TableBuilder → elimina CS0618)
                b.ToTable(t => t.HasCheckConstraint(
                    "CK_Friendship_UserA_Not_UserB",
                    "[UserAId] <> [UserBId]"));

                // FKs para User com Restrict (evita cascatas indesejadas)
                b.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserAId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.UserBId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne<User>()
                    .WithMany()
                    .HasForeignKey(x => x.InitiatorId)
                    .OnDelete(DeleteBehavior.Restrict);

                // Colunas simples
                b.Property(x => x.Status).IsRequired();
                b.Property(x => x.CreatedAt).IsRequired();
                b.Property(x => x.UpdatedAt);
                b.Property(x => x.BlockedById); // nullable
            });
        }
    }
}
