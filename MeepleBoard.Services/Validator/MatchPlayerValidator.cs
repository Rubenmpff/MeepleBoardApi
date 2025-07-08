using FluentValidation;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Validator
{
    public class MatchPlayerValidator : AbstractValidator<MatchPlayerDto>
    {
        public MatchPlayerValidator()
        {
            ClassLevelCascadeMode = CascadeMode.Stop; // ✅ Corrigido para a nova versão

            RuleFor(mp => mp.MatchId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("O ID da partida é obrigatório.");

            RuleFor(mp => mp.UserId)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("O ID do usuário é obrigatório.");

            RuleFor(mp => mp.Score)
                .Cascade(CascadeMode.Stop)
                .GreaterThanOrEqualTo(0).WithMessage("A pontuação do jogador não pode ser negativa.");

            RuleFor(mp => mp.RankPosition)
                .Cascade(CascadeMode.Stop)
                .GreaterThanOrEqualTo(1)
                .When(mp => mp.RankPosition.HasValue)
                .WithMessage("A posição do jogador deve ser um número positivo.");

            RuleFor(mp => mp.UserName)
                .Cascade(CascadeMode.Stop)
                .MaximumLength(100).WithMessage("O nome do jogador deve ter no máximo 100 caracteres.");
        }
    }
}