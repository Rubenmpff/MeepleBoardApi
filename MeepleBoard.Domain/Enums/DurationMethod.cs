namespace MeepleBoard.Domain.Enums
{
    /// <summary>
    /// How a match duration was obtained. Null on a match = recorded before the question existed
    /// («método não indicado» when it has minutes).
    /// </summary>
    public enum DurationMethod
    {
        /// <summary>Timed during the match.</summary>
        Measured = 1,
        /// <summary>The player's estimate.</summary>
        Estimated = 2,
        /// <summary>Explicit «Não sei»: no minutes; never counted.</summary>
        Unknown = 3
    }
}
