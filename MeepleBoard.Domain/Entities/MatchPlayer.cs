using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Representa a relação entre um jogador e uma partida no MeepleBoard.
    /// </summary>
    public class MatchPlayer
    {
        // 🔹 Construtor privado exigido pelo Entity Framework
        private MatchPlayer()
        { }

        /// <summary>
        /// Construtor para criar um registro de jogador em uma partida.
        /// </summary>
        /// <param name="matchId">ID da partida.</param>
        /// <param name="userId">ID do jogador.</param>
        public MatchPlayer(Guid matchId, Guid userId)
        {
            if (matchId == Guid.Empty)
                throw new ArgumentException("ID da partida inválido.");

            if (userId == Guid.Empty)
                throw new ArgumentException("ID do jogador inválido.");

            Id = Guid.NewGuid();
            MatchId = matchId;
            UserId = userId;
            CreatedAt = DateTime.UtcNow;
        }

        // 🔹 Chave primária
        [Key]
        public Guid Id { get; private set; }

        // 🔹 Relacionamento com Match
        [Required]
        public Guid MatchId { get; private set; }

        [ForeignKey("MatchId")]
        public virtual Match? Match { get; private set; }

        // 🔹 Relacionamento com User
        [Required]
        public Guid UserId { get; private set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; private set; }

        // 🔹 Pontuação final do jogador
        private int? _score;

        public int? Score
        {
            get => _score;
            private set
            {
                if (value.HasValue && value < 0)
                    throw new ArgumentException("A pontuação do jogador não pode ser negativa.");

                if (_score != value)
                {
                    _score = value;
                    UpdateTimestamp();
                }
            }
        }

        // 🔹 Indica se o jogador venceu
        public bool IsWinner { get; private set; }

        // 🔹 Posição do jogador na partida
        private int? _rankPosition;

        [Range(1, int.MaxValue, ErrorMessage = "A posição do jogador deve ser um valor positivo.")]
        public int? RankPosition
        {
            get => _rankPosition;
            private set
            {
                if (value.HasValue && value < 1)
                    throw new ArgumentException("A posição do jogador deve ser pelo menos 1.");

                if (_rankPosition != value)
                {
                    _rankPosition = value;
                    UpdateTimestamp();
                }
            }
        }

        // 🔹 Datas de criação e atualização
        public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; private set; }

        /// <summary>
        /// Atualiza a pontuação do jogador.
        /// </summary>
        public void UpdateScore(int? score)
        {
            Score = score;
        }

        /// <summary>
        /// Define se o jogador venceu a partida.
        /// </summary>
        public void SetWinner(bool isWinner)
        {
            if (IsWinner != isWinner)
            {
                IsWinner = isWinner;
                UpdateTimestamp();
            }
        }

        /// <summary>
        /// Atualiza a posição do jogador no ranking da partida.
        /// </summary>
        public void SetRankPosition(int? position)
        {
            RankPosition = position;
        }

        /// <summary>
        /// Atualiza a data de modificação.
        /// </summary>
        private void UpdateTimestamp()
        {
            UpdatedAt = DateTime.UtcNow;
        }
    }
}