using FluentValidation;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Validator
{
    public class UserGameLibraryValidator : AbstractValidator<UserGameLibraryDto>
    {
        public UserGameLibraryValidator()
        {
            ClassLevelCascadeMode = CascadeMode.Stop; // ✅ Corrigido para nova versão

            RuleFor(ugl => ugl.GameId)
                .NotEmpty().WithMessage("O ID do jogo é obrigatório.");

            RuleFor(ugl => ugl.GameName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("O nome do jogo é obrigatório.")
                .MaximumLength(150).WithMessage("O nome do jogo deve ter no máximo 150 caracteres.");

            RuleFor(ugl => ugl.GameImageUrl)
                .Cascade(CascadeMode.Stop)
                .Must(BeAValidUrl).When(ugl => !string.IsNullOrEmpty(ugl.GameImageUrl))
                .WithMessage("A URL da imagem deve ser válida.");

            RuleFor(ugl => ugl.PricePaid)
                .Cascade(CascadeMode.Stop)
                .GreaterThanOrEqualTo(0).When(ugl => ugl.PricePaid.HasValue)
                .WithMessage("O preço pago não pode ser negativo.");

            RuleFor(ugl => ugl.TotalTimesPlayed)
                .Cascade(CascadeMode.Stop)
                .GreaterThanOrEqualTo(0).WithMessage("O número de partidas jogadas não pode ser negativo.");

            RuleFor(ugl => ugl.TotalHoursPlayed)
                .Cascade(CascadeMode.Stop)
                .GreaterThanOrEqualTo(0).WithMessage("O total de horas jogadas não pode ser negativo.");

            RuleFor(ugl => ugl.LastPlayedAt)
                .Cascade(CascadeMode.Stop)
                .LessThanOrEqualTo(DateTime.UtcNow).When(ugl => ugl.LastPlayedAt.HasValue)
                .WithMessage("A data da última jogada não pode ser no futuro.");
        }

        private static bool BeAValidUrl(string? url)
        {
            return string.IsNullOrWhiteSpace(url) || Uri.TryCreate(url, UriKind.Absolute, out _);
        }
    }
}