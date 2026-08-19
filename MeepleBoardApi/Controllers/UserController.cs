using MeepleBoard.CrossCutting.Security;
using MeepleBoard.Services.DTOs;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

namespace MeepleBoardApi.Controllers
{
    [Route("MeepleBoard/users")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly IUserService _userService;

        public UserController(IUserService userService)
        {
            _userService = userService ?? throw new ArgumentNullException(nameof(userService));
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserDto>>> GetAll(CancellationToken cancellationToken)
        {
            var users = await _userService.GetAllAsync(cancellationToken);
            if (users == null || !users.Any()) return NoContent();
            return Ok(users);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest("O ID do usuário não pode ser vazio.");
            var user = await _userService.GetByIdAsync(id, cancellationToken);
            if (user == null) return NotFound("Usuário não encontrado.");
            return Ok(user);
        }

        [Authorize]
        [HttpGet("me")]
        public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized("Claim com o ID não encontrada no token.");

            var user = await _userService.GetByIdAsync(userId, cancellationToken);
            return user is null ? NotFound() : Ok(user);
        }

        /// <summary>
        /// Regista ou atualiza o Expo Push Token do utilizador autenticado.
        /// Chamado automaticamente quando a app abre.
        /// </summary>
        [Authorize]
        [HttpPost("push-token")]
        public async Task<ActionResult> UpdatePushToken(
            [FromBody] UpdatePushTokenDto dto,
            CancellationToken cancellationToken)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.ExpoPushToken))
                return BadRequest("O token de push é obrigatório.");

            var userId = User.GetUserId();
            try
            {
                await _userService.UpdatePushTokenAsync(userId, dto.ExpoPushToken, cancellationToken);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }

        [HttpPost]
        public async Task<ActionResult> Create([FromBody] UserDto userDto, CancellationToken cancellationToken)
        {
            if (userDto == null) return BadRequest("Os dados do usuário são obrigatórios.");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            await _userService.AddAsync(userDto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = userDto.Id }, userDto);
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] UserDto userDto, CancellationToken cancellationToken)
        {
            if (userDto == null) return BadRequest("Os dados do usuário são obrigatórios.");
            if (id != userDto.Id) return BadRequest("O ID do usuário na URL não corresponde ao ID do corpo.");
            if (!ModelState.IsValid) return BadRequest(ModelState);
            var existingUser = await _userService.GetByIdAsync(id, cancellationToken);
            if (existingUser == null) return NotFound("Usuário não encontrado.");
            await _userService.UpdateAsync(userDto, cancellationToken);
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty) return BadRequest("O ID do usuário não pode ser vazio.");
            var existingUser = await _userService.GetByIdAsync(id, cancellationToken);
            if (existingUser == null) return NotFound("Usuário não encontrado.");
            await _userService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }

        /// <summary>
        /// Atualiza quem pode ver a coleção de jogos do utilizador autenticado.
        /// </summary>
        [Authorize]
        [HttpPatch("me/library-privacy")]
        public async Task<ActionResult> UpdateLibraryPrivacy(
            [FromBody] UpdateLibraryPrivacyDto dto,
            CancellationToken cancellationToken)
        {
            if (dto == null) return BadRequest(new { message = "Os dados são obrigatórios." });

            var userId = User.GetUserId();
            try
            {
                await _userService.UpdateLibraryPrivacyAsync(userId, dto.LibraryPrivacy, cancellationToken);
                return NoContent();
            }
            catch (KeyNotFoundException ex) { return NotFound(new { message = ex.Message }); }
        }
    }

    public class UpdatePushTokenDto
    {
        public string ExpoPushToken { get; set; } = string.Empty;
    }
}