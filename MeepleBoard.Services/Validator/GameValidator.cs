using FluentValidation;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Validator
{
    public class GameValidator : AbstractValidator<GameDto>
    {
        public GameValidator()
        {
            ClassLevelCascadeMode = CascadeMode.Stop;

            RuleFor(game => game.Name)
                .Cascade(CascadeMode.Continue) // 🔹 Garante que todas as regras sejam validadas
                .NotEmpty().WithMessage("O nome do jogo é obrigatório.")
                .MinimumLength(3).WithMessage("O nome do jogo deve ter pelo menos 3 caracteres.")
                .MaximumLength(150).WithMessage("O nome do jogo deve ter no máximo 150 caracteres.")
                .WithName("Nome do Jogo");

            RuleFor(game => game.Description)
                .Cascade(CascadeMode.Continue)
                .NotEmpty().WithMessage("A descrição do jogo é obrigatória.")
                .MaximumLength(1000).WithMessage("A descrição do jogo deve ter no máximo 1000 caracteres.")
                .WithName("Descrição");

            RuleFor(game => game.ImageUrl)
                .Cascade(CascadeMode.Continue)
                .NotEmpty().WithMessage("A URL da imagem do jogo é obrigatória.")
                .Must(BeAValidUrl).WithMessage("A URL da imagem deve ser válida e começar com http:// ou https://.")
                .WithName("Imagem do Jogo");

            RuleFor(game => game.BggId)
                .Cascade(CascadeMode.Continue)
                .GreaterThan(0).WithMessage("O ID do BoardGameGeek (BGG) deve ser um número positivo.")
                .When(game => game.BggId.HasValue)
                .WithName("ID do BGG");

            RuleFor(game => game.MeepleBoardScore)
                .Cascade(CascadeMode.Continue)
                .InclusiveBetween(0, 100).WithMessage("O score do MeepleBoard deve estar entre 0 e 100.")
                .When(game => game.MeepleBoardScore.HasValue)
                .WithName("Score MeepleBoard");

            RuleFor(game => game.BggRanking)
                .Cascade(CascadeMode.Continue)
                .GreaterThan(0).WithMessage("O ranking do BoardGameGeek deve ser um número positivo.")
                .When(game => game.BggRanking.HasValue)
                .WithName("Ranking do BGG");
        }

        /// <summary>
        /// Valida se uma URL é válida e começa com http:// ou https://.
        /// </summary>
        private bool BeAValidUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return false;

            if (!Uri.TryCreate(url, UriKind.Absolute, out var uriResult))
                return false;

            return uriResult.Scheme == Uri.UriSchemeHttp || uriResult.Scheme == Uri.UriSchemeHttps;
        }
    }
}