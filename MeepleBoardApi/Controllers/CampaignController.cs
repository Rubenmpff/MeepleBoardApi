using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs.Campaign;
using MeepleBoard.Services.DTOs.MatchJournal;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/campaigns")]
    [ApiController]
    [Authorize]
    public class CampaignController : ControllerBase
    {
        private readonly ICampaignService _campaignService;

        public CampaignController(ICampaignService campaignService)
        {
            _campaignService = campaignService;
        }

        // ── Campanhas ─────────────────────────────────────────────────────────

        /// <summary>Lista todas as campanhas do utilizador autenticado.</summary>
        [HttpGet("mine")]
        public async Task<ActionResult<IEnumerable<CampaignDto>>> GetMine(CancellationToken ct)
        {
            var userId = User.GetUserId();
            var campaigns = await _campaignService.GetMineAsync(userId, ct);
            return Ok(campaigns);
        }

        /// <summary>Lista campanhas do utilizador para um jogo específico.</summary>
        [HttpGet("game/{gameId:guid}")]
        public async Task<ActionResult<IEnumerable<CampaignDto>>> GetByGame(
            Guid gameId, CancellationToken ct)
        {
            if (gameId == Guid.Empty) return BadRequest("ID do jogo inválido.");
            var userId = User.GetUserId();
            var campaigns = await _campaignService.GetByGameAsync(gameId, userId, ct);
            return Ok(campaigns);
        }

        /// <summary>Detalhe completo de uma campanha.</summary>
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<CampaignDto>> GetById(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                var campaign = await _campaignService.GetByIdAsync(id, userId, ct);
                if (campaign == null) return NotFound("Campanha não encontrada.");
                return Ok(campaign);
            }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        /// <summary>Cria uma nova campanha.</summary>
        [HttpPost]
        public async Task<ActionResult<CampaignDto>> Create(
            [FromBody] CreateCampaignDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.GetUserId();
            try
            {
                var created = await _campaignService.CreateAsync(dto, userId, ct);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
        }

        /// <summary>Atualiza nome e notas da campanha.</summary>
        [HttpPut("{id:guid}")]
        public async Task<ActionResult> Update(
            Guid id, [FromBody] UpdateCampaignDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.GetUserId();
            try
            {
                await _campaignService.UpdateAsync(id, dto, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        /// <summary>Marca como concluída.</summary>
        [HttpPost("{id:guid}/complete")]
        public async Task<ActionResult> Complete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.CompleteAsync(id, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        /// <summary>Abandona a campanha.</summary>
        [HttpPost("{id:guid}/abandon")]
        public async Task<ActionResult> Abandon(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.AbandonAsync(id, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        /// <summary>Apaga a campanha (só o criador).</summary>
        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.DeleteAsync(id, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        // ── Membros ───────────────────────────────────────────────────────────

        /// <summary>Convida um utilizador para a campanha.</summary>
        [HttpPost("{id:guid}/members/invite")]
        public async Task<ActionResult> Invite(
            Guid id, [FromBody] InviteMemberDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.GetUserId();
            try
            {
                await _campaignService.InviteMemberAsync(id, dto.UserId, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        /// <summary>Responder ao convite (aceitar/recusar).</summary>
        [HttpPost("{id:guid}/members/respond")]
        public async Task<ActionResult> Respond(
            Guid id, [FromBody] bool accept, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.RespondInviteAsync(id, userId, accept, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        /// <summary>Remove um membro da campanha.</summary>
        [HttpDelete("{id:guid}/members/{targetUserId:guid}")]
        public async Task<ActionResult> RemoveMember(
            Guid id, Guid targetUserId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.RemoveMemberAsync(id, targetUserId, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        /// <summary>Sair da campanha voluntariamente.</summary>
        [HttpPost("{id:guid}/members/leave")]
        public async Task<ActionResult> Leave(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.LeaveCampaignAsync(id, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        // ── Partidas ──────────────────────────────────────────────────────────

        /// <summary>Associa uma partida à campanha.</summary>
        [HttpPost("{id:guid}/matches")]
        public async Task<ActionResult> AddMatch(
            Guid id, [FromBody] AddMatchToCampaignDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.GetUserId();
            try
            {
                await _campaignService.AddMatchAsync(id, dto, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        /// <summary>Remove uma partida da campanha.</summary>
        [HttpDelete("{id:guid}/matches/{matchId:guid}")]
        public async Task<ActionResult> RemoveMatch(
            Guid id, Guid matchId, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                await _campaignService.RemoveMatchAsync(id, matchId, userId, ct);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        // ── Diário ────────────────────────────────────────────────────────────

        /// <summary>
        /// Cria ou atualiza a entrada de diário do utilizador autenticado para uma partida.
        /// Qualquer membro da campanha pode escrever a sua perspetiva.
        /// </summary>
        [HttpPut("matches/{matchId:guid}/journal")]
        public async Task<ActionResult<JournalEntryDto>> UpsertJournalEntry(
            Guid matchId, [FromBody] UpsertJournalEntryDto dto, CancellationToken ct)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var userId = User.GetUserId();
            try
            {
                var entry = await _campaignService.UpsertJournalEntryAsync(matchId, dto, userId, ct);
                return Ok(entry);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }

        /// <summary>Lista todas as entradas de diário de uma partida (de todos os membros).</summary>
        [HttpGet("matches/{matchId:guid}/journal")]
        public async Task<ActionResult<IEnumerable<JournalEntryDto>>> GetJournalEntries(
            Guid matchId, CancellationToken ct)
        {
            var entries = await _campaignService.GetJournalEntriesAsync(matchId, ct);
            return Ok(entries);
        }

        /// <summary>
        /// Adiciona uma foto à entrada de diário do utilizador autenticado para esta partida.
        /// Só jogadores que participaram na partida podem fazer upload. Máx. 8MB, imagens (jpg/png/webp).
        /// </summary>
        [HttpPost("matches/{matchId:guid}/journal/photos")]
        [RequestSizeLimit(8 * 1024 * 1024)]
        [Consumes("multipart/form-data")]
        public async Task<ActionResult<JournalEntryDto>> AddJournalPhoto(
            Guid matchId, IFormFile file, CancellationToken ct)
        {
            if (file == null || file.Length == 0)
                return BadRequest(new { message = "Nenhuma imagem foi enviada." });

            if (file.Length > 8 * 1024 * 1024)
                return BadRequest(new { message = "A imagem não pode exceder 8MB." });

            if (string.IsNullOrEmpty(file.ContentType) || !file.ContentType.StartsWith("image/"))
                return BadRequest(new { message = "O ficheiro tem de ser uma imagem." });

            var userId = User.GetUserId();
            try
            {
                await using var stream = file.OpenReadStream();
                var entry = await _campaignService.AddJournalPhotoAsync(
                    matchId, userId, stream, file.FileName, ct);
                return Ok(entry);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
            catch (InvalidOperationException ex) { return Conflict(new { message = ex.Message }); }
        }

        /// <summary>Remove uma foto da entrada de diário do utilizador autenticado para esta partida.</summary>
        [HttpDelete("matches/{matchId:guid}/journal/photos")]
        public async Task<ActionResult<JournalEntryDto>> RemoveJournalPhoto(
            Guid matchId, [FromQuery] string photoUrl, CancellationToken ct)
        {
            var userId = User.GetUserId();
            try
            {
                var entry = await _campaignService.RemoveJournalPhotoAsync(matchId, userId, photoUrl, ct);
                return Ok(entry);
            }
            catch (ArgumentException ex) { return BadRequest(new { message = ex.Message }); }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
            catch (UnauthorizedAccessException) { return Forbid(); }
        }
    }
}