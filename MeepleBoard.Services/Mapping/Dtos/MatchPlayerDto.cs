using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// DTO para representar um jogador em uma partida.
    /// </summary>
    public class MatchPlayerDto
    {
        /// <summary>
        /// Identificador único do jogador na partida.
        /// </summary>
        public Guid Id { get; init; }

        /// <summary>
        /// Identificador da partida (Obrigatório).
        /// </summary>
        [Required(ErrorMessage = "O ID da partida é obrigatório.")]
        public Guid MatchId { get; init; }

        /// <summary>
        /// Identificador do usuário (Obrigatório).
        /// </summary>
        [Required(ErrorMessage = "O ID do usuário é obrigatório.")]
        public Guid UserId { get; init; }

        /// <summary>
        /// Nome do jogador (Opcional, no máximo 100 caracteres).
        /// </summary>
        [MaxLength(100, ErrorMessage = "O nome do jogador deve ter no máximo 100 caracteres.")]
        public string? UserName { get; init; }

        /// <summary>
        /// Pontuação do jogador (opcional, inteiro com sinal).
        /// </summary>
        public int? Score { get; init; }

        /// <summary>
        /// Indica se o jogador venceu a partida.
        /// </summary>
        public string? Outcome { get; init; }

        public bool IsWinner { get; init; } = false;

        /// <summary>
        /// Posição do jogador na partida (Opcional, deve ser positiva).
        /// </summary>
        [Range(1, int.MaxValue, ErrorMessage = "A posição do jogador deve ser um número positivo.")]
        public int? RankPosition { get; init; }
    }
}