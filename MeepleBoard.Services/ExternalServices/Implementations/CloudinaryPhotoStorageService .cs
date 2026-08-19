using CloudinaryDotNet;
using CloudinaryDotNet.Actions;
using MeepleBoard.Services.ExternalServices.Interfaces;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace MeepleBoard.Services.ExternalServices.Implementations
{
    /// <summary>
    /// Upload de fotos das partidas (diário) para o Cloudinary.
    ///
    /// Segue o mesmo padrão do JWT_KEY: em desenvolvimento, define via
    /// `dotnet user-secrets`; em produção, via variáveis de ambiente.
    /// NUNCA vai para o appsettings.json (fica fora do controlo de versões).
    ///
    ///   dotnet user-secrets set "CLOUDINARY_CLOUD_NAME" "o-teu-cloud-name"
    ///   dotnet user-secrets set "CLOUDINARY_API_KEY" "a-tua-api-key"
    ///   dotnet user-secrets set "CLOUDINARY_API_SECRET" "o-teu-api-secret"
    ///
    /// Pasta usada no Cloudinary: "meepleboard/match-journal"
    /// As imagens são redimensionadas (máx. 1600x1600) e comprimidas
    /// automaticamente no upload, para poupar espaço e banda.
    /// </summary>
    public class CloudinaryPhotoStorageService : IPhotoStorageService
    {
        private const string Folder = "meepleboard/match-journal";
        private readonly Lazy<Cloudinary> _cloudinaryLazy;
        private readonly ILogger<CloudinaryPhotoStorageService> _logger;

        public CloudinaryPhotoStorageService(
            IConfiguration configuration,
            ILogger<CloudinaryPhotoStorageService> logger)
        {
            // ── Validação adiada (Lazy) ──────────────────────────────────────
            // Não valida nem liga ao Cloudinary aqui. Se validássemos já no
            // construtor, bastava o .NET precisar de UM CampaignService (para
            // qualquer coisa, nem que fosse listar campanhas) para rebentar
            // com erro 500 em toda a app só por faltarem credenciais do
            // Cloudinary. Isso só deve acontecer quando alguém tentar mesmo
            // enviar/remover uma foto.
            _cloudinaryLazy = new Lazy<Cloudinary>(() =>
            {
                var cloudName = configuration["CLOUDINARY_CLOUD_NAME"];
                var apiKey = configuration["CLOUDINARY_API_KEY"];
                var apiSecret = configuration["CLOUDINARY_API_SECRET"];

                if (string.IsNullOrWhiteSpace(cloudName) ||
                    string.IsNullOrWhiteSpace(apiKey) ||
                    string.IsNullOrWhiteSpace(apiSecret))
                {
                    throw new InvalidOperationException(
                        "As credenciais do Cloudinary não estão configuradas. " +
                        "Em desenvolvimento: dotnet user-secrets set \"CLOUDINARY_CLOUD_NAME\" \"...\" (e API_KEY/API_SECRET). " +
                        "Em produção: variáveis de ambiente com o mesmo nome. " +
                        "O upload de fotos não está disponível até isto ser configurado.");
                }

                return new Cloudinary(new Account(cloudName, apiKey, apiSecret));
            });

            _logger = logger;
        }

        public async Task<string> UploadAsync(Stream fileStream, string fileName, CancellationToken ct = default)
        {
            if (fileStream == null || fileStream.Length == 0)
                throw new ArgumentException("Ficheiro de imagem inválido.", nameof(fileStream));

            var uploadParams = new ImageUploadParams
            {
                File = new FileDescription(fileName, fileStream),
                Folder = Folder,
                // Comprime e limita o tamanho — evita fotos gigantes da câmara do telemóvel
                Transformation = new Transformation()
                    .Width(1600).Height(1600).Crop("limit")
                    .Quality("auto:good")
                    .FetchFormat("auto"),
                UniqueFilename = true,
                Overwrite = false,
            };

            var result = await _cloudinaryLazy.Value.UploadAsync(uploadParams, ct);

            if (result.Error != null)
            {
                _logger.LogError("Erro ao enviar foto para o Cloudinary: {Error}", result.Error.Message);
                throw new InvalidOperationException($"Não foi possível enviar a imagem: {result.Error.Message}");
            }

            var url = result.SecureUrl?.ToString() ?? result.Url?.ToString();
            if (string.IsNullOrEmpty(url))
                throw new InvalidOperationException("O Cloudinary não devolveu um URL válido para a imagem.");

            return url;
        }

        public async Task DeleteAsync(string photoUrl, CancellationToken ct = default)
        {
            var publicId = ExtractPublicId(photoUrl);
            if (string.IsNullOrEmpty(publicId))
            {
                _logger.LogWarning("Não foi possível extrair o public_id do URL: {Url}", photoUrl);
                return;
            }

            var deleteParams = new DeletionParams(publicId);
            var result = await _cloudinaryLazy.Value.DestroyAsync(deleteParams);

            if (result.Result != "ok" && result.Result != "not found")
                _logger.LogWarning("Cloudinary não confirmou a remoção de {PublicId}: {Result}", publicId, result.Result);
        }

        /// <summary>
        /// Extrai o public_id do Cloudinary a partir do URL guardado, ex:
        /// https://res.cloudinary.com/xxx/image/upload/v1234567/meepleboard/match-journal/abc123.jpg
        ///   → meepleboard/match-journal/abc123
        /// </summary>
        private static string? ExtractPublicId(string url)
        {
            try
            {
                var uri = new Uri(url);
                var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

                var uploadIndex = Array.IndexOf(segments, "upload");
                if (uploadIndex < 0 || uploadIndex == segments.Length - 1) return null;

                var rest = segments.Skip(uploadIndex + 1)
                    // salta o segmento de versão "v1234567", se existir
                    .SkipWhile(s => s.Length > 1 && s[0] == 'v' && s[1..].All(char.IsDigit))
                    .ToArray();

                if (rest.Length == 0) return null;

                var joined = string.Join('/', rest);
                var dotIndex = joined.LastIndexOf('.');
                return dotIndex > 0 ? joined[..dotIndex] : joined;
            }
            catch
            {
                return null;
            }
        }
    }
}