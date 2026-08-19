using MeepleBoard.Domain.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace MeepleBoard.CrossCutting.IoC
{
    public static class UserSeeder
    {
        public static async Task SeedRolesAsync(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();

            var roleManager =
                scope.ServiceProvider
                    .GetRequiredService<RoleManager<IdentityRole<Guid>>>();

            string[] roles =
            {
                "User",
                "Admin",
                "Moderator",
                "SuperAdmin"
            };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                {
                    await roleManager.CreateAsync(
                        new IdentityRole<Guid>
                        {
                            Name = role
                        });
                }
            }
        }

        public static async Task SeedTestUsersAsync(
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            using var scope = serviceProvider.CreateScope();

            var userManager =
                scope.ServiceProvider
                    .GetRequiredService<UserManager<User>>();

            var adminEmail =
                configuration["Seed:AdminEmail"];

            var adminPassword =
                configuration["Seed:AdminPassword"];

            var userEmail =
                configuration["Seed:UserEmail"];

            var userPassword =
                configuration["Seed:UserPassword"];

            // ==================================================
            // ADMIN DE TESTE
            // ==================================================

            if (!string.IsNullOrWhiteSpace(adminEmail)
                && !string.IsNullOrWhiteSpace(adminPassword))
            {
                var adminUser =
                    await userManager.FindByEmailAsync(adminEmail);

                if (adminUser == null)
                {
                    adminUser = new User(
                        "Admin",
                        adminEmail,
                        "Local")
                    {
                        EmailConfirmed = true
                    };

                    var result =
                        await userManager.CreateAsync(
                            adminUser,
                            adminPassword);

                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(
                            adminUser,
                            "Admin");

                        Console.WriteLine(
                            "Conta Admin de teste criada com sucesso.");
                    }
                    else
                    {
                        Console.WriteLine(
                            "Não foi possível criar a conta Admin de teste.");

                        foreach (var error in result.Errors)
                        {
                            Console.WriteLine(
                                $"{error.Code}: {error.Description}");
                        }
                    }
                }
                else
                {
                    if (!adminUser.EmailConfirmed)
                    {
                        adminUser.EmailConfirmed = true;

                        await userManager.UpdateAsync(
                            adminUser);
                    }

                    if (!await userManager.IsInRoleAsync(
                            adminUser,
                            "Admin"))
                    {
                        await userManager.AddToRoleAsync(
                            adminUser,
                            "Admin");
                    }
                }
            }

            // ==================================================
            // UTILIZADOR NORMAL DE TESTE
            // ==================================================

            if (!string.IsNullOrWhiteSpace(userEmail)
                && !string.IsNullOrWhiteSpace(userPassword))
            {
                var normalUser =
                    await userManager.FindByEmailAsync(userEmail);

                if (normalUser == null)
                {
                    normalUser = new User(
                        "User",
                        userEmail,
                        "Local")
                    {
                        EmailConfirmed = true
                    };

                    var result =
                        await userManager.CreateAsync(
                            normalUser,
                            userPassword);

                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(
                            normalUser,
                            "User");

                        Console.WriteLine(
                            "Conta User de teste criada com sucesso.");
                    }
                    else
                    {
                        Console.WriteLine(
                            "Não foi possível criar a conta User de teste.");

                        foreach (var error in result.Errors)
                        {
                            Console.WriteLine(
                                $"{error.Code}: {error.Description}");
                        }
                    }
                }
                else
                {
                    if (!normalUser.EmailConfirmed)
                    {
                        normalUser.EmailConfirmed = true;

                        await userManager.UpdateAsync(
                            normalUser);
                    }

                    if (!await userManager.IsInRoleAsync(
                            normalUser,
                            "User"))
                    {
                        await userManager.AddToRoleAsync(
                            normalUser,
                            "User");
                    }
                }
            }
        }
    }
}