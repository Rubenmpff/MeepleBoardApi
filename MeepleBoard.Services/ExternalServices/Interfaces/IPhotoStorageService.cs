namespace MeepleBoard.Services.ExternalServices.Interfaces
{
    /// <summary>
    /// Abstrai o fornecedor de armazenamento de imagens (hoje: Cloudinary).
    /// Permite trocar de fornecedor no futuro sem tocar na lógica de negócio.
    /// </summary>
    public interface IPhotoStorageService
    {
        /// <summary>
        /// Envia uma imagem para o armazenamento e devolve o URL público (HTTPS).
        /// </summary>
        Task<string> UploadAsync(Stream fileStream, string fileName, CancellationToken ct = default);

        /// <summary>
        /// Remove uma imagem previamente enviada, a partir do seu URL.
        /// Falhas na remoção não devem impedir o fluxo principal — quem chamar
        /// deve tratar erros aqui como não-críticos (a imagem fica órfã no storage).
        /// </summary>
        Task DeleteAsync(string photoUrl, CancellationToken ct = default);
    }
}