namespace MeepleBoard.Domain.Entities;

// Explicit choices only. Legacy rows never pass through this resolver during reads.
public static class MatchOutcomeRules
{
    public static Dictionary<Guid, string> Resolve(string? mode, string result, IEnumerable<Guid> participants,
        IEnumerable<Guid>? selected, bool sharedVictoryAllowed, bool inSession = false)
    {
        if (mode is not ("COMPETITIVE" or "SOLO" or "COOPERATIVE")) throw new ArgumentException("Indica um modo de jogo válido.");
        if (result is not ("Win" or "Loss" or "Draw" or "Undefined")) throw new ArgumentException("Indica um resultado válido.");
        var ids = participants.ToHashSet(); var raw = (selected ?? Array.Empty<Guid>()).ToList();
        if (ids.Contains(Guid.Empty) || ids.Count == 0) throw new ArgumentException("Indica os participantes da partida.");
        if (raw.Count != raw.Distinct().Count() || raw.Any(id => !ids.Contains(id))) throw new ArgumentException("A seleção de resultado deve conter participantes distintos da partida.");
        if (mode == "SOLO" && (ids.Count != 1 || inSession)) throw new ArgumentException("Solo exige um jogador e não está disponível em sessões.");
        if (mode != "SOLO" && ids.Count < 2) throw new ArgumentException("Este modo exige pelo menos dois participantes distintos.");
        if (sharedVictoryAllowed && (mode != "COMPETITIVE" || result != "Win")) throw new ArgumentException("Vitória partilhada só se aplica à vitória competitiva.");
        if (mode != "COMPETITIVE" || result == "Undefined") {
            if (raw.Count != 0) throw new ArgumentException("Este resultado não permite selecionar vencedores individuais.");
            return ids.ToDictionary(id => id, _ => result);
        }
        if (result == "Loss") throw new ArgumentException("No competitivo indica vitória, empate ou resultado não definido.");
        if (result == "Win" && (raw.Count == 0 || (raw.Count > 1 && !sharedVictoryAllowed))) throw new ArgumentException("Seleciona o vencedor; vários vencedores exigem confirmar que as regras permitem vitória partilhada.");
        if (result == "Draw" && raw.Count < 2) throw new ArgumentException("Seleciona pelo menos dois jogadores empatados no primeiro lugar.");
        var selectedIds = raw.ToHashSet();
        return ids.ToDictionary(id => id, id => selectedIds.Contains(id) ? result : "Loss");
    }

    public static string SharedResult(string? mode, string? result, string? current, string? other)
    {
        if (result == null) return "legacyUnknown";
        if (result == "Undefined") return "undefined";
        if (mode == "COOPERATIVE") return result == "Win" ? "teamWin" : result == "Loss" ? "teamLoss" : "teamDraw";
        if (current == "Win" && other == "Win") return "sharedWin";
        if (current == "Win") return "currentUserWin";
        if (other == "Win") return "otherUserWin";
        if (current == "Draw" && other == "Draw") return "draw";
        if (current == "Draw") return "currentUserDraw";
        if (other == "Draw") return "otherUserDraw";
        return current == "Loss" && other == "Loss" ? "bothLost" : "undefined";
    }
}
