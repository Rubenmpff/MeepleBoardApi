using MeepleBoard.Domain.Enums;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// Representa um jogo dentro da biblioteca pessoal de um usuário.
    /// </summary>
    public class UserGameLibrary
    {
        // 🔹 Construtor público exigido pelo Entity Framework
        public UserGameLibrary()
        { }

        /// <summary>
        /// Construtor para criar um novo jogo na biblioteca do usuário.
        /// </summary>
        public UserGameLibrary(Guid userId, Guid gameId, GameLibraryStatus status, decimal? pricePaid = null)
        {
            if (userId == Guid.Empty)
                throw new ArgumentException("ID do usuário inválido.");

            if (gameId == Guid.Empty)
                throw new ArgumentException("ID do jogo inválido.");

            Id = Guid.NewGuid();
            UserId = userId;
            GameId = gameId;
            Status = status;
            PricePaid = pricePaid < 0 ? throw new ArgumentException("O preço pago deve ser um valor positivo.") : pricePaid;
            AddedAt = DateTime.UtcNow;
        }

        // 🔹 Chave primária
        [Key]
        public Guid Id { get; private set; }

        // 🔹 Relacionamento com User (Proprietário da biblioteca)
        [Required]
        public Guid UserId { get; private set; }

        [ForeignKey("UserId")]
        public virtual User? User { get; private set; }

        // 🔹 Relacionamento com Game (Jogo na biblioteca)
        [Required]
        public Guid GameId { get; private set; }

        [ForeignKey("GameId")]
        public virtual Game? Game { get; private set; }

        // 🔹 Status do jogo na biblioteca
        [Required]
        public GameLibraryStatus Status { get; private set; } = GameLibraryStatus.Owned;

        // 💰 Valor pago pelo jogo (Opcional, não pode ser negativo)
        [Column(TypeName = "decimal(18,2)")]
        public decimal? PricePaid { get; private set; }

        // ⏳ Total de partidas jogadas
        [Range(0, int.MaxValue)]
        public int TotalTimesPlayed { get; private set; } = 0;

        // ⏱️ Total de horas jogadas
        [Range(0, int.MaxValue)]
        public int TotalHoursPlayed { get; private set; } = 0;

        // 📅 Última vez que o usuário jogou este jogo
        public DateTime? LastPlayedAt { get; private set; }

        // 🔹 Data de quando o jogo foi adicionado à biblioteca
        public DateTime AddedAt { get; private set; } = DateTime.UtcNow;

        // 🔹 Última atualização
        public DateTime? UpdatedAt { get; private set; }

        /// <summary>
        /// Atualiza o status do jogo na biblioteca.
        /// </summary>
        public void UpdateStatus(GameLibraryStatus newStatus)
        {
            if (Status != newStatus)
            {
                Status = newStatus;
                UpdateTimestamp();
            }
        }

        /// <summary>
        /// Define o preço pago pelo jogo.
        /// </summary>
        public void SetPricePaid(decimal? price)
        {
            if (price.HasValue && price < 0)
                throw new ArgumentException("O preço pago deve ser um valor positivo.");

            if (PricePaid != price)
            {
                PricePaid = price;
                UpdateTimestamp();
            }
        }

        /// <summary>
        /// Atualiza a data da última vez que o jogo foi jogado.
        /// </summary>
        public void SetLastPlayedAt(DateTime? lastPlayedAt)
        {
            if (lastPlayedAt.HasValue && lastPlayedAt > DateTime.UtcNow)
                throw new ArgumentException("A data da última jogada não pode ser no futuro.");

            if (LastPlayedAt != lastPlayedAt)
            {
                LastPlayedAt = lastPlayedAt;
                UpdateTimestamp();
            }
        }

        /// <summary>
        /// Atualiza o total de partidas jogadas.
        /// </summary>
        public void IncrementTimesPlayed()
        {
            TotalTimesPlayed++;
            UpdateTimestamp();
        }

        /// <summary>
        /// Atualiza o total de horas jogadas.
        /// </summary>
        public void AddHoursPlayed(int hours)
        {
            if (hours < 0)
                throw new ArgumentException("O total de horas jogadas não pode ser negativo.");

            TotalHoursPlayed += hours;
            UpdateTimestamp();
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