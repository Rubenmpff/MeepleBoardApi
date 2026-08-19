using MeepleBoard.Services.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace MeepleBoard.Services.Implementations
{
    /// <summary>
    /// Serviço de notificações push via Expo Push API.
    /// Documentação: https://docs.expo.dev/push-notifications/sending-notifications/
    ///
    /// Fluxo:
    ///   1. Frontend guarda o ExpoPushToken no User quando a app abre
    ///   2. Backend chama este serviço quando há eventos relevantes
    ///   3. Expo entrega a notificação ao dispositivo via FCM/APNs
    /// </summary>
    public class NotificationService : INotificationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<NotificationService> _logger;

        private const string ExpoApiUrl = "https://exp.host/--/api/v2/push/send";

        public NotificationService(HttpClient httpClient, ILogger<NotificationService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task SendAsync(
            string expoPushToken,
            string title,
            string body,
            object? data = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(expoPushToken)) return;

            await SendToManyAsync(new[] { expoPushToken }, title, body, data, ct);
        }

        public async Task SendToManyAsync(
            IEnumerable<string> expoPushTokens,
            string title,
            string body,
            object? data = null,
            CancellationToken ct = default)
        {
            var validTokens = expoPushTokens
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Distinct()
                .ToList();

            if (validTokens.Count == 0) return;

            var messages = validTokens.Select(token => new
            {
                to = token,
                title = title,
                body = body,
                data = data ?? new { },
                sound = "default",
                badge = 1,
            }).ToList();

            try
            {
                var response = await _httpClient.PostAsJsonAsync(ExpoApiUrl, messages, ct);

                if (!response.IsSuccessStatusCode)
                {
                    var error = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning("⚠️ Expo Push API retornou {Status}: {Error}",
                        response.StatusCode, error);
                    return;
                }

                _logger.LogInformation(
                    "✅ Notificação enviada para {Count} dispositivo(s): {Title}",
                    validTokens.Count, title);
            }
            catch (Exception ex)
            {
                // Notificações nunca devem rebentar o fluxo principal
                _logger.LogError(ex, "❌ Erro ao enviar notificação push: {Title}", title);
            }
        }

        /* ── Métodos de negócio ───────────────────────────────────────────── */

        public async Task NotifyMatchCreatedAsync(
            Guid matchId,
            string gameName,
            IEnumerable<string> playerTokens,
            CancellationToken ct = default)
        {
            await SendToManyAsync(
                playerTokens,
                title: "🎲 Nova partida registada!",
                body: $"Deixa a tua avaliação de {gameName}.",
                data: new { matchId = matchId.ToString(), type = "match_created" },
                ct: ct);
        }

        public async Task NotifyJournalEntryAddedAsync(
            Guid matchId,
            string gameName,
            string evaluatorName,
            IEnumerable<string> otherPlayerTokens,
            CancellationToken ct = default)
        {
            await SendToManyAsync(
                otherPlayerTokens,
                title: $"⭐ {evaluatorName} avaliou a partida!",
                body: $"Vê o que {evaluatorName} achou de {gameName}.",
                data: new { matchId = matchId.ToString(), type = "journal_entry_added" },
                ct: ct);
        }

        public async Task NotifyMatchJournalClosedAsync(
            Guid matchId,
            string gameName,
            IEnumerable<string> pendingPlayerTokens,
            CancellationToken ct = default)
        {
            await SendToManyAsync(
                pendingPlayerTokens,
                title: "⏰ Ainda podes avaliar!",
                body: $"O diário de {gameName} fechou. Ainda podes deixar a tua avaliação.",
                data: new { matchId = matchId.ToString(), type = "journal_closed" },
                ct: ct);
        }

        public async Task NotifyCampaignInviteAsync(
            string expoPushToken,
            string campaignName,
            string inviterName,
            CancellationToken ct = default)
        {
            await SendAsync(
                expoPushToken,
                title: "🏰 Convite para campanha!",
                body: $"{inviterName} convidou-te para a campanha \"{campaignName}\".",
                data: new { type = "campaign_invite" },
                ct: ct);
        }
    }
}