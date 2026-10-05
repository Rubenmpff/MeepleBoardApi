using Hangfire;
using MeepleBoard.Services.Job;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Endpoints administrativos relacionados com a manutenção
    /// do catálogo BGG.
    /// </summary>
    [ApiController]
    [Route("MeepleBoard/admin/bgg/catalog")]
    [Authorize(Roles = "Admin")]
    public class BggCatalogAdminController : ControllerBase
    {
        private readonly IBackgroundJobClient _backgroundJobs;
        private readonly ILogger<BggCatalogAdminController> _logger;

        public BggCatalogAdminController(
            IBackgroundJobClient backgroundJobs,
            ILogger<BggCatalogAdminController> logger)
        {
            _backgroundJobs =
                backgroundJobs ??
                throw new ArgumentNullException(
                    nameof(backgroundJobs));

            _logger =
                logger ??
                throw new ArgumentNullException(
                    nameof(logger));
        }

        [HttpPost("enrich")]
        [ProducesResponseType(StatusCodes.Status202Accepted)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public IActionResult EnrichCatalog(
            [FromQuery] int batchSize = 100)
        {
            var safeBatchSize =
                Math.Clamp(
                    batchSize,
                    1,
                    500);

            var jobId =
                _backgroundJobs.Enqueue<BggCatalogDetailsEnrichmentJob>(
                    job => job.ExecuteAsync(
                        safeBatchSize,
                        CancellationToken.None));

            _logger.LogInformation(
                "Job de enriquecimento do catálogo BGG {JobId} " +
                "colocado em fila por {User}. Batch: {BatchSize}.",
                jobId,
                User.Identity?.Name ?? "desconhecido",
                safeBatchSize);

            return Accepted(
                new
                {
                    message =
                        "Enriquecimento do catálogo BGG colocado em fila.",
                    jobId,
                    batchSize =
                        safeBatchSize
                });
        }
    }
}