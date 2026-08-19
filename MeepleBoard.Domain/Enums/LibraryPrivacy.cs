using System.ComponentModel;

namespace MeepleBoard.Domain.Enums
{
    /// <summary>
    /// Define quem pode ver a coleção (biblioteca) de jogos de um utilizador.
    /// </summary>
    public enum LibraryPrivacy
    {
        [Description("Privada")]
        Private = 0, // Só o próprio utilizador vê a sua coleção

        [Description("Apenas amigos")]
        FriendsOnly = 1, // Só amigos (Friendship.Status == Accepted) veem a coleção

        [Description("Pública")]
        Public = 2 // Qualquer utilizador autenticado vê a coleção
    }
}