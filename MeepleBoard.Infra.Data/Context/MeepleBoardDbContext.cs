using MeepleBoard.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace MeepleBoard.Infra.Data.Context
{
    public class MeepleBoardDbContext
        : IdentityDbContext<User, IdentityRole<Guid>, Guid>
    {
        public MeepleBoardDbContext(DbContextOptions<MeepleBoardDbContext> options)
            : base(options)
        {
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

            modelBuilder.ApplyConfigurationsFromAssembly(
                typeof(MeepleBoardDbContext).Assembly
            );

            /* =========================================================
               MATCH CONFIGURATION
            ==========================================================*/

            modelBuilder.Entity<Match>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasOne(x => x.Game)
                    .WithMany(g => g.Matches)
                    .HasForeignKey(x => x.GameId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.Winner)
                    .WithMany()
                    .HasForeignKey(x => x.WinnerId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasOne(x => x.GameSession)
                    .WithMany(s => s.Matches)
                    .HasForeignKey(x => x.GameSessionId)
                    .OnDelete(DeleteBehavior.SetNull);
            });

            modelBuilder.Entity<MatchPlayer>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasIndex(x => new { x.MatchId, x.UserId })
                    .IsUnique();

                b.HasOne(x => x.Match)
                    .WithMany(m => m.MatchPlayers)
                    .HasForeignKey(x => x.MatchId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                    .WithMany(u => u.Matches)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            /* =========================================================
               USER GAME LIBRARY
            ==========================================================*/

            modelBuilder.Entity<UserGameLibrary>(b =>
            {
                b.HasOne(x => x.User)
                    .WithMany(u => u.UserGameLibraries)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.Game)
                    .WithMany(g => g.UserGameLibraries)
                    .HasForeignKey(x => x.GameId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            /* =========================================================
               GAME
            ==========================================================*/

            modelBuilder.Entity<Game>(b =>
            {
                b.Property(x => x.Description)
                    .HasMaxLength(10_000);

                b.HasMany(g => g.Expansions)
                    .WithOne(g => g.BaseGame)
                    .HasForeignKey(g => g.BaseGameId)
                    .OnDelete(DeleteBehavior.Restrict);
            });

            /* =========================================================
               GAME SESSION
            ==========================================================*/

            modelBuilder.Entity<GameSession>(b =>
            {
                b.HasKey(x => x.Id);

                b.Property(x => x.Name)
                    .IsRequired()
                    .HasMaxLength(200);

                b.Property(x => x.Location)
                    .HasMaxLength(200);

                // ✅ Organizer relation
                b.HasOne(x => x.Organizer)
                    .WithMany()
                    .HasForeignKey(x => x.OrganizerId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.HasMany(x => x.Players)
                    .WithOne(p => p.Session)
                    .HasForeignKey(p => p.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            modelBuilder.Entity<GameSessionPlayer>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasIndex(x => new { x.SessionId, x.UserId })
                    .IsUnique();

                b.HasOne(x => x.Session)
                    .WithMany(s => s.Players)
                    .HasForeignKey(x => x.SessionId)
                    .OnDelete(DeleteBehavior.Cascade);

                b.HasOne(x => x.User)
                    .WithMany()
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Restrict);

                b.Property(x => x.JoinedAt)
                    .IsRequired();
            });

            /* =========================================================
               REFRESH TOKENS
            ==========================================================*/

            modelBuilder.Entity<RefreshToken>(b =>
            {
                b.HasOne(x => x.User)
                    .WithMany(u => u.RefreshTokens)
                    .HasForeignKey(x => x.UserId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            /* =========================================================
               FRIENDSHIPS
            ==========================================================*/

            modelBuilder.Entity<Friendship>(b =>
            {
                b.HasKey(x => x.Id);

                b.HasIndex(x => new { x.UserAId, x.UserBId })
                    .IsUnique();

                b.ToTable(t => t.HasCheckConstraint(
                    "CK_Friendship_UserA_Not_UserB",
                    "[UserAId] <> [UserBId]"
                ));

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

                b.Property(x => x.Status).IsRequired();
                b.Property(x => x.CreatedAt).IsRequired();
            });
        }
    }
}
