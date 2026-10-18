namespace MeepleBoard.Domain.Entities
{
    /// <summary>
    /// A block made by one person (docs/proposta-bloqueio-comunidade.md in the app). Separate from friendships: each person
    /// creates and removes only their own; unblocking never restores a friendship; a block either way stops new interaction
    /// between the two (requests, search, profiles, invitations, new matches, public publications) and leaves history as it is.
    /// </summary>
    public class UserBlock
    {
        private UserBlock() { }

        public UserBlock(Guid blockerId, Guid blockedId)
        {
            if (blockerId == Guid.Empty || blockedId == Guid.Empty || blockerId == blockedId) throw new ArgumentException("Pedido inválido.");
            BlockerId = blockerId; BlockedId = blockedId; CreatedAt = DateTime.UtcNow;
        }

        public Guid BlockerId { get; private set; }
        public virtual User? Blocker { get; private set; }
        public Guid BlockedId { get; private set; }
        public virtual User? Blocked { get; private set; }
        public DateTime CreatedAt { get; private set; }
    }
}
