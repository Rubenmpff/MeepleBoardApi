using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Services.DTOs
{
    /// <summary>
    /// DTO com os dados básicos de um jogo retornado pela API do BoardGameGeek.
    /// </summary>
    public class BGGGameDto
    {
        /// <summary> ID no BoardGameGeek. </summary>
        [Required(ErrorMessage = "O ID do jogo no BGG é obrigatório.")]
        [Range(1, int.MaxValue, ErrorMessage = "O ID deve ser um número positivo.")]
        public int Id { get; set; }

        /// <summary> Nome do jogo. </summary>
        [Required(ErrorMessage = "O nome do jogo é obrigatório.")]
        [MaxLength(200, ErrorMessage = "O nome do jogo deve ter no máximo 200 caracteres.")]
        public string Name { get; set; } = string.Empty;

        /// <summary> Imagem de capa. </summary>
        [Url(ErrorMessage = "A URL da imagem não é válida.")]
        public string? ImageUrl { get; set; }

        /// <summary> Ranking no BGG (opcional). </summary>
        [Range(1, int.MaxValue, ErrorMessage = "O ranking deve ser um número positivo.")]
        public int? BggRanking { get; set; }

        /// <summary> Nota média de avaliação (0–10). </summary>
        [Range(0, 10, ErrorMessage = "A nota média deve estar entre 0 e 10.")]
        public double? AverageRating { get; set; }
    }
}