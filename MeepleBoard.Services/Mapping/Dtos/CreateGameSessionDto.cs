using System.ComponentModel.DataAnnotations;

namespace MeepleBoard.Application.DTOs
{
    public class CreateGameSessionDto
    {
        [Required]
        [MinLength(3)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? Location { get; set; }

        // membros da sessão escolhidos (amigos)
        public List<Guid> PlayerIds { get; set; } = new();
    }
}
