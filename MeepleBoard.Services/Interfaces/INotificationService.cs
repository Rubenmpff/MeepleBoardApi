namespace MeepleBoard.Services.Interfaces
{
    public interface INotificationService
    {
        /// <summary>
        /// Envia notificação push a um utilizador específico.
        /// </summary>
        Task SendAsync(string expoPushToken, string title, string body, object? data = null, CancellationToken ct = default);

        /// <summary>
        /// Envia notificação push a múltiplos utilizadores.
        /// </summary>
        Task SendToManyAsync(IEnumerable<string> expoPushTokens, string title, string body, object? data = null, CancellationToken ct = default);

        /// <summary>
        /// Notifica todos os jogadores de uma partida para avaliarem.
        /// </summary>
        Task NotifyMatchCreatedAsync(Guid matchId, string gameName, IEnumerable<string> playerTokens, CancellationToken ct = default);

        /// <summary>
        /// Notifica os outros jogadores quando alguém avalia uma partida.
        /// </summary>
        Task NotifyJournalEntryAddedAsync(Guid matchId, string gameName, string evaluatorName, IEnumerable<string> otherPlayerTokens, CancellationToken ct = default);

        /// <summary>
        /// Notifica os jogadores quando o diário de uma partida fecha automaticamente.
        /// </summary>
        Task NotifyMatchJournalClosedAsync(Guid matchId, string gameName, IEnumerable<string> pendingPlayerTokens, CancellationToken ct = default);

        /// <summary>
        /// Notifica um utilizador quando é convidado para uma campanha.
        /// </summary>
        Task NotifyCampaignInviteAsync(string expoPushToken, string campaignName, string inviterName, CancellationToken ct = default);
    }
}