using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/users")]
    [ApiController]
    [Authorize]
    public class UserGameLibraryController : ControllerBase
    {
        private readonly IUserGameLibraryService _libraryService;

        public UserGameLibraryController(IUserGameLibraryService libraryService)
        {
            _libraryService = libraryService;
        }

        [HttpGet("{userId:guid}/games")]
        public async Task<ActionResult<IEnumerable<UserGameLibraryDto>>> GetUserLibrary(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                return BadRequest(new { Message = "O ID do usuário não pode ser vazio." });

            try
            {
                var requesterId = User.GetUserId();
                var library = await _libraryService.GetUserLibraryAsync(requesterId, userId, cancellationToken);

                if (library == null || !library.Any())
                    return NoContent();

                return Ok(library);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Forbid(ex.Message);
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

        [HttpGet("{userId:guid}/played-games")]
        public async Task<ActionResult<IEnumerable<PlayedGameDto>>> GetPlayedGames(Guid userId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty)
                return BadRequest(new { Message = "O ID do usuário não pode ser vazio." });

            try
            {
                var played = await _libraryService.GetPlayedGamesAsync(userId, cancellationToken);

                if (played == null || !played.Any())
                    return NoContent();

                return Ok(played);
            }
            catch (KeyNotFoundException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Erro ao obter jogos jogados.", Details = ex.Message });
            }
        }

        [HttpPost("{userId:guid}/games")]
        public async Task<ActionResult> AddGameToLibrary(Guid userId, [FromBody] UserGameLibraryDto gameDto, CancellationToken cancellationToken)
        {
            if (gameDto == null)
                return BadRequest(new { Message = "Os dados do jogo são obrigatórios." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (userId != User.GetUserId())
                return Forbid();

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

        [HttpPatch("{userId:guid}/games/{gameId:guid}")]
        public async Task<ActionResult> UpdateGameInLibrary(
            Guid userId, Guid gameId, [FromBody] UpdateUserGameLibraryDto dto, CancellationToken cancellationToken)
        {
            if (dto == null)
                return BadRequest(new { Message = "Os dados são obrigatórios." });

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            if (userId != User.GetUserId())
                return Forbid();

            try
            {
                await _libraryService.UpdateGameInLibraryAsync(userId, gameId, dto.Status, dto.PricePaid, cancellationToken);
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { Message = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return NotFound(new { Message = ex.Message });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { Message = "Erro ao atualizar jogo na biblioteca.", Details = ex.Message });
            }
        }

        [HttpDelete("{userId:guid}/games/{gameId:guid}")]
        public async Task<ActionResult> RemoveGameFromLibrary(Guid userId, Guid gameId, CancellationToken cancellationToken)
        {
            if (userId == Guid.Empty || gameId == Guid.Empty)
                return BadRequest(new { Message = "Os IDs do usuário e do jogo não podem ser vazios." });

            if (userId != User.GetUserId())
                return Forbid();

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