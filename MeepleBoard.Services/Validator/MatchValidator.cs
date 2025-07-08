using FluentValidation;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Validator
{
    public class MatchValidator : AbstractValidator<MatchDto>
    {
        public MatchValidator()
        {
            ClassLevelCascadeMode = CascadeMode.Stop; // ✅ Melhor controle de fluxo

            RuleFor(match => match.GameId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("O jogo associado à partida é obrigatório.")
                .NotEqual(Guid.Empty).WithMessage("O ID do jogo deve ser válido.");

            RuleFor(match => match.MatchDate)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("A data da partida é obrigatória.")
                .LessThanOrEqualTo(DateTime.UtcNow).WithMessage("A data da partida não pode estar no futuro.");

            RuleFor(match => match.Players)
                .Cascade(CascadeMode.Stop)
                .NotNull().WithMessage("A partida deve ter pelo menos um jogador.")
                .Must(players => players.Count >= 1).WithMessage("A partida deve ter no mínimo 1 jogador.")
                .Must((match, players) => match.IsSoloGame ? players.Count == 1 : players.Count >= 2)
                .WithMessage("Número de jogadores inválido para esse tipo de jogo.");

            RuleFor(match => match.WinnerId)
                .Cascade(CascadeMode.Stop)
                .Must(winnerId => winnerId == null || winnerId != Guid.Empty)
                .WithMessage("O ID do vencedor deve ser válido se informado.");

            RuleFor(match => match.DurationInMinutes)
                .GreaterThanOrEqualTo(0).When(match => match.DurationInMinutes.HasValue)
                .WithMessage("A duração da partida não pode ser negativa.");
        }
    }
}