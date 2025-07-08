using FluentValidation;
using MeepleBoard.Services.DTOs;

namespace MeepleBoard.Services.Validator
{
    public class UserValidator : AbstractValidator<UserDto>
    {
        public UserValidator()
        {
            ClassLevelCascadeMode = CascadeMode.Stop; // ✅ Corrigido para nova versão

            RuleFor(user => user.Email)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("O email é obrigatório.")
                .EmailAddress().WithMessage("Formato de email inválido.");

            RuleFor(user => user.UserName)
                .Cascade(CascadeMode.Stop)
                .NotEmpty().WithMessage("O nome de usuário é obrigatório.")
                .MinimumLength(3).WithMessage("O nome de usuário deve ter pelo menos 3 caracteres.");
        }
    }
}