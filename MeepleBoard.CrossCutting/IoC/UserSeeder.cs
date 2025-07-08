using MeepleBoard.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace MeepleBoard.CrossCutting.IoC
{
    public static class UserSeeder
    {
        public static async Task SeedAdminAndUserAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<User>>();
            var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            string[] roles = { "User", "Admin", "Moderator", "SuperAdmin" };

            // ✅ Criar papéis (Roles) caso não existam
            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(new IdentityRole<Guid> { Name = role });
                }
            }

            // ✅ Criar usuário ADMIN se não existir
            string adminEmail = "admin@meepleboard.com";
            string adminPassword = "Admin@123";

            var adminUser = await userManager.FindByEmailAsync(adminEmail);
            if (adminUser == null)
            {
                adminUser = new User("Admin", adminEmail, "Local") { EmailConfirmed = true };
                var createAdmin = await userManager.CreateAsync(adminUser, adminPassword);
                if (createAdmin.Succeeded)
                {
                    await userManager.AddToRoleAsync(adminUser, "Admin");
                    Console.WriteLine("✅ Conta Admin criada com sucesso!");
                }
            }
            else if (!adminUser.EmailConfirmed)
            {
                adminUser.EmailConfirmed = true;
                await userManager.UpdateAsync(adminUser);
                Console.WriteLine("✅ Email do Admin confirmado automaticamente.");
            }

            // ✅ Criar usuário NORMAL se não existir
            string userEmail = "user@meepleboard.com";
            string userPassword = "User@123";

            var normalUser = await userManager.FindByEmailAsync(userEmail);
            if (normalUser == null)
            {
                normalUser = new User("User", userEmail, "Local");
                var createUser = await userManager.CreateAsync(normalUser, userPassword);
                if (createUser.Succeeded)
                {
                    await userManager.AddToRoleAsync(normalUser, "User");
                    Console.WriteLine("✅ Conta Usuário criada com sucesso!");
                }
            }
        }
    }
}