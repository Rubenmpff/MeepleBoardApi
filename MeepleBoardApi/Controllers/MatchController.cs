using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/matches")]
    [ApiController]
    public class MatchController : ControllerBase
    {
        private readonly IMatchService _matchService;

        public MatchController(IMatchService matchService)
        {
            _matchService = matchService;
        }

        /// <summary>
        /// 🔹 Obtém todas as partidas com paginação.
        /// </summary>
        /// <param name="pageIndex">Índice da página (padrão: 0).</param>
        /// <param name="pageSize">Quantidade de itens por página (padrão: 10).</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>Lista de partidas ou 204 se nenhuma partida for encontrada.</returns>
        /// <response code="200">Retorna a lista de partidas.</response>
        /// <response code="204">Nenhuma partida encontrada.</response>
        [HttpGet]
        public async Task<ActionResult<IEnumerable<MatchDto>>> GetAll(int pageIndex = 0, int pageSize = 10, CancellationToken cancellationToken = default)
        {
            if (pageIndex < 0 || pageSize <= 0)
            {
                return BadRequest("Os parâmetros de paginação devem ser positivos.");
            }

            var matches = await _matchService.GetAllAsync(pageIndex, pageSize, cancellationToken);

            if (matches == null || !matches.Any())
            {
                return NoContent();
            }

            return Ok(matches);
        }

        /// <summary>
        /// 🔹 Obtém uma partida pelo ID.
        /// </summary>
        /// <param name="id">Identificador da partida.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>O jogo encontrado ou erro 404.</returns>
        /// <response code="200">Retorna a partida encontrada.</response>
        /// <response code="400">ID inválido.</response>
        /// <response code="404">Partida não encontrada.</response>
        [HttpGet("{id:guid}")]
        public async Task<ActionResult<MatchDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID da partida não pode ser vazio.");

            var match = await _matchService.GetByIdAsync(id, cancellationToken);
            if (match == null)
                return NotFound("Partida não encontrada.");

            return Ok(match);
        }

        [HttpGet("last")]
        [Authorize]
        public async Task<IActionResult> GetLastMatchForUser()
        {
            var userId = User.GetUserId(); // Usa extensão de ClaimsPrincipal

            var lastMatch = await _matchService.GetLastMatchForUserAsync(userId);

            if (lastMatch == null)
                return NotFound(new { message = "Nenhuma partida encontrada." });

            return Ok(lastMatch);
        }

        /// <summary>
        /// 🔹 Cria uma nova partida.
        /// </summary>
        /// <param name="matchDto">Dados da partida.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>Partida criada.</returns>
        /// <response code="201">Partida criada com sucesso.</response>
        /// <response code="400">Dados inválidos.</response>
        [HttpPost]
        public async Task<ActionResult<MatchDto>> Create([FromBody] MatchDto matchDto, CancellationToken cancellationToken)
        {
            if (matchDto == null)
                return BadRequest("Os dados da partida são obrigatórios.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var createdMatch = await _matchService.AddAsync(matchDto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = createdMatch.Id }, createdMatch);
        }

        /// <summary>
        /// 🔹 Atualiza uma partida existente.
        /// </summary>
        /// <param name="id">ID da partida a ser atualizada.</param>
        /// <param name="matchDto">Dados da partida.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>Sem conteúdo em caso de sucesso.</returns>
        /// <response code="204">Partida atualizada com sucesso.</response>
        /// <response code="400">IDs não coincidem ou dados inválidos.</response>
        /// <response code="404">Partida não encontrada.</response>
        [HttpPut("{id:guid}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] MatchDto matchDto, CancellationToken cancellationToken)
        {
            if (matchDto == null)
                return BadRequest("Os dados da partida são obrigatórios.");

            if (id != matchDto.Id)
                return BadRequest("O ID da partida na URL não corresponde ao ID do corpo da requisição.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var existingMatch = await _matchService.GetByIdAsync(id, cancellationToken);
            if (existingMatch == null)
                return NotFound("Partida não encontrada.");

            var rowsAffected = await _matchService.UpdateAsync(matchDto, cancellationToken);
            if (rowsAffected == 0)
                return NotFound("Nenhuma partida foi atualizada.");

            return NoContent();
        }

        /// <summary>
        /// 🔹 Exclui uma partida pelo ID.
        /// </summary>
        /// <param name="id">ID da partida a ser excluída.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>Sem conteúdo se excluído com sucesso.</returns>
        /// <response code="204">Partida excluída com sucesso.</response>
        /// <response code="400">ID inválido.</response>
        /// <response code="404">Partida não encontrada.</response>
        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID da partida não pode ser vazio.");

            var success = await _matchService.DeleteAsync(id, cancellationToken);
            if (!success)
                return NotFound("Partida não encontrada.");

            return NoContent();
        }
    }
}