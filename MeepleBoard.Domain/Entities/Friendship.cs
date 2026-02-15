public enum FriendshipStatus
{
    Pending = 0,
    Accepted = 1,
    Rejected = 2,
    Blocked = 3
}

public class Friendship
{
    public Guid Id { get; set; }

    // Par ordenado (A < B) para garantir unicidade
    public Guid UserAId { get; set; }
    public Guid UserBId { get; set; }

    // Quem iniciou o pedido (A ou B)
    public Guid InitiatorId { get; set; }

    public FriendshipStatus Status { get; set; } = FriendshipStatus.Pending;

    // Quando Status = Blocked
    public Guid? BlockedById { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
