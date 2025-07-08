namespace MeepleBoard.Domain.Enums
{
    [Flags] // Permite múltiplos papéis combinados
    public enum UserRole : byte
    {
        None = 0,
        User = 1,
        Moderator = 2,
        Admin = 4,
        SuperAdmin = 8
    }

    public static class UserRoleExtensions
    {
        // 🔹 Retorna o nome do papel de forma legívelgv
        public static string GetRoleName(this UserRole role)
        {
            return role switch
            {
                UserRole.User => "Usuário",
                UserRole.Moderator => "Moderador",
                UserRole.Admin => "Administrador",
                UserRole.SuperAdmin => "Super Administrador",
                _ => "Desconhecido"
            };
        }

        // 🔹 Converte UserRole em uma lista de strings para Identity
        public static List<string> ToRoleList(this UserRole role)
        {
            return Enum.GetValues(typeof(UserRole))
                .Cast<UserRole>()
                .Where(r => r != UserRole.None && role.HasFlag(r))
                .Select(r => r.ToString())
                .ToList();
        }

        // 🔹 Verifica se um usuário tem um papel específico
        public static bool HasRole(this UserRole role, UserRole checkRole)
        {
            return role.HasFlag(checkRole);
        }

        // 🔹 Converte uma lista de strings para UserRole
        public static UserRole FromRoleList(IEnumerable<string> roles)
        {
            UserRole result = UserRole.None;
            foreach (var role in roles)
            {
                if (Enum.TryParse(role, out UserRole parsedRole))
                {
                    result |= parsedRole;
                }
            }
            return result;
        }
    }
}