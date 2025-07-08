using System.ComponentModel;

namespace MeepleBoard.Domain.Enums
{
    /// <summary>
    /// Define o status de um jogo dentro da biblioteca do usuário.
    /// </summary>
    public enum GameLibraryStatus
    {
        [Description("Tenho o jogo")]
        Owned = 1, // O usuário possui este jogo na coleção

        [Description("Já joguei")]
        Played = 2, // O usuário já jogou este jogo antes

        [Description("Quero jogar")]
        Wishlist = 3 // O usuário quer jogar este jogo no futuro
    }

    /// <summary>
    /// Métodos auxiliares para manipulação de GameLibraryStatus.
    /// </summary>
    public static class GameLibraryStatusExtensions
    {
        /// <summary>
        /// Retorna a descrição amigável do status.
        /// </summary>
        public static string GetDescription(this GameLibraryStatus status)
        {
            return status switch
            {
                GameLibraryStatus.Owned => "Tenho o jogo",
                GameLibraryStatus.Played => "Já joguei",
                GameLibraryStatus.Wishlist => "Quero jogar",
                _ => "Desconhecido"
            };
        }
    }
}