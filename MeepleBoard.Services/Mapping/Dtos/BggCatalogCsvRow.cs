namespace MeepleBoard.Services.DTOs
{
    public sealed class BggCatalogCsvRow
    {
        public int BggId { get; init; }

        public string Name { get; init; } =
            string.Empty;

        public int? YearPublished { get; init; }

        public int? BggRank { get; init; }

        public double? AverageRating { get; init; }

        public int? RatingsCount { get; init; }

        public bool IsExpansion { get; init; }
    }
}