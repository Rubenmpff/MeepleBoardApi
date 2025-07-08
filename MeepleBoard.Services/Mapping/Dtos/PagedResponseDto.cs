/// <summary>
/// Representa uma resposta paginada genérica com metadados de paginação.
/// Usada para retornar coleções com suporte a paginação nos endpoints da API.
/// </summary>
public class PagedResponse<T>
{
    /// <summary>
    /// Lista de itens da página atual.
    /// </summary>
    public IReadOnlyList<T> Data { get; set; }

    /// <summary>
    /// Total de itens disponíveis no conjunto completo (sem paginação).
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Número de itens por página.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Índice da página atual (0-based).
    /// </summary>
    public int PageIndex { get; set; }

    /// <summary>
    /// Número total de páginas disponíveis.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    public PagedResponse(IReadOnlyList<T> data, int totalCount, int pageSize, int pageIndex)
    {
        Data = data ?? throw new ArgumentNullException(nameof(data));
        TotalCount = totalCount;
        PageSize = pageSize;
        PageIndex = pageIndex;
    }
}
