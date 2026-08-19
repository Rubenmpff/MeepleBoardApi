using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace MeepleBoard.Services.Mapping.Dtos
{
    public record FriendLiteDto(
        Guid id,
        string userName,
        string? profilePictureUrl,
        int sharedMatchesCount,
        DateTime? lastPlayedAt,
        string? mostPlayedGame,
        /// <summary>Se este amigo fez algum pedido à API nos últimos 5 minutos.</summary>
        bool isOnline);

    public record FriendRequestDto(Guid requestId, Guid fromUserId, string fromUserName, DateTime createdAt);

    public record OutgoingFriendRequestDto(Guid requestId, Guid toUserId, string toUserName, DateTime createdAt);

    public record UserSearchResultDto(Guid id, string userName, string? profilePictureUrl, string relationshipStatus, Guid? requestId, bool isOnline);

    public record SharedGameDto(Guid gameId, string name, string? imageUrl, int matchesCount,
        int currentUserWins, int otherUserWins, int teamWins, bool isCooperative);

    public record SharedMatchDto(Guid matchId, Guid gameId, string gameName, string? imageUrl,
        DateTime matchDate, string result);

    public record SharedSessionDto(Guid sessionId, string name, DateTime date, int matchesCount);

    public record UserProfileDto(
        Guid id, string userName, string? profilePictureUrl, string relationshipStatus,
        Guid? requestId, DateTime? friendsSince, int totalMatches, int totalGamesPlayed,
        int totalGamesOwned, int sharedMatches, int sharedGames, int sharedMinutes,
        int sharedSessions, int currentUserWins, int otherUserWins, int draws,
        IReadOnlyList<SharedGameDto> topSharedGames,
        IReadOnlyList<SharedGameDto> commonOwnedGames,
        IReadOnlyList<SharedMatchDto> recentSharedMatches,
        IReadOnlyList<SharedSessionDto> recentSharedSessions,
        /// <summary>Privacidade da coleção deste utilizador: "Private" | "FriendsOnly" | "Public".</summary>
        string libraryPrivacy,
        /// <summary>Se quem pede o perfil pode ver a coleção (tab Coleção) deste utilizador.</summary>
        bool canViewLibrary,
        /// <summary>Se este utilizador fez algum pedido à API nos últimos 5 minutos.</summary>
        bool isOnline);

    /// <summary>
    /// Partida partilhada com detalhe (pontuações, duração, local) — usado na
    /// tab "Partidas" (paginada) e no ecrã "Histórico em conjunto" por jogo.
    /// </summary>
    public record SharedMatchDetailDto(
        Guid matchId, Guid gameId, string gameName, string? imageUrl,
        DateTime matchDate, string result, int? durationInMinutes, string? location,
        int? currentUserScore, int? otherUserScore);

    /// <summary>Página de partidas partilhadas (tab "Partidas").</summary>
    public record SharedMatchesPageDto(
        IReadOnlyList<SharedMatchDetailDto> items, int totalCount, int page, int pageSize);

}