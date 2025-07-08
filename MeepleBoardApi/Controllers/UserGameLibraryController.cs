using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/users")]
    [ApiController]
    public class UserGameLibraryController : ControllerBase
    {
        private readonly IUserGameLibraryService _libraryService;

        public UserGameLibraryController(IUserGameLibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        /// <summary>
        /// 🔹 Obtém a biblioteca de jogos de um usuário.
        /// </summary>
        /// <param name="userId">ID do usuário.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>Lista de jogos na biblioteca do usuário.</returns>
        /// <response code="200">Retorna a biblioteca do usuário.</response>
        /// <response code="204">Nenhum jogo na biblioteca.</response>
        /// <response code="404">Usuário não encontrado.</response>
        [HttpGet("{userId:guid}/games")]
        public async Task<ActionResult<IEnumerable<UserGameLibraryDto>>> GetUserLibrary(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                return BadRequest(new { Message = "O ID do usuário não pode ser vazio." });

            try
            {
                var library = await _libraryService.GetUserLibraryAsync(userId, cancellationToken);

                if (library == null || !library.Any())
                    return NoContent();

                return Ok(library);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Erro ao obter biblioteca.", Details = ex.Message });
            }
        }

        /// <summary>
        /// 🔹 Adiciona um jogo à biblioteca do usuário.
        /// </summary>
        /// <param name="userId">ID do usuário.</param>
        /// <param name="gameDto">Dados do jogo a ser adicionado.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>Mensagem de sucesso.</returns>
        /// <response code="201">Jogo adicionado com sucesso.</response>
        /// <response code="400">Dados inválidos.</response>
        /// <response code="404">Usuário não encontrado.</response>
        /// <response code="409">Jogo já existe na biblioteca.</response>
        [HttpPost("{userId:guid}/games")]
        public async Task<ActionResult> AddGameToLibrary(Guid userId, [FromBody] UserGameLibraryDto gameDto, CancellationToken cancellationToken)
        {
            if (gameDto == null)
                return BadRequest(new { Message = "Os dados do jogo são obrigatórios." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            try
            {
                await _libraryService.AddGameToLibraryAsync(userId, gameDto.GameId, gameDto.GameName, gameDto.Status, gameDto.PricePaid, cancellationToken);
                return CreatedAtAction(nameof(GetUserLibrary), new { userId }, new { Message = "Jogo adicionado à biblioteca." });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { Message = ex.Message });
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Erro ao adicionar jogo.", Details = ex.Message });
            }
        }

        /// <summary>
        /// 🔹 Remove um jogo da biblioteca do usuário.
        /// </summary>
        /// <param name="userId">ID do usuário.</param>
        /// <param name="gameId">ID do jogo a ser removido.</param>
        /// <param name="cancellationToken">Token para cancelamento da requisição.</param>
        /// <returns>204 No Content se removido.</returns>
        /// <response code="204">Jogo removido com sucesso.</response>
        /// <response code="400">ID inválido.</response>
        /// <response code="404">Jogo não encontrado.</response>
        [HttpDelete("{userId:guid}/games/{gameId:guid}")]
        public async Task<ActionResult> RemoveGameFromLibrary(Guid userId, Guid gameId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty || gameId == Guid.Empty)
                return BadRequest(new { Message = "Os IDs do usuário e do jogo não podem ser vazios." });

            try
            {
                await _libraryService.RemoveGameFromLibraryAsync(userId, gameId, cancellationToken);
                return NoContent();
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Erro ao remover jogo.", Details = ex.Message });
            }
        }
    }
}