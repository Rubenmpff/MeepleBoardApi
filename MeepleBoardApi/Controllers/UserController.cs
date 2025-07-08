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

            if (users == null || !users.Any())
            {
                return NoContent();
            }

            return Ok(users);
        }

        [HttpGet("{id:guid}")]
        public async Task<ActionResult<UserDto>> GetById(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID do usuário não pode ser vazio.");

            var user = await _userService.GetByIdAsync(id, cancellationToken);
            if (user == null)
                return NotFound("Usuário não encontrado.");

            return Ok(user);
        }

        [Authorize]                                       // ← só para quem tem token
        [HttpGet("me")]                                  // GET /MeepleBoard/users/me
        public async Task<ActionResult<UserDto>> GetMe(CancellationToken cancellationToken)
        {
            // O token gerado pelo teu AuthController deve conter o claim "sub" ou "NameIdentifier"
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !Guid.TryParse(userIdStr, out var userId))
                return Unauthorized("Claim com o ID não encontrada no token.");

            var user = await _userService.GetByIdAsync(userId, cancellationToken);
            return user is null ? NotFound() : Ok(user);
        }

        [HttpPost]
        public async Task<ActionResult> Create([FromBody] UserDto userDto, CancellationToken cancellationToken)
        {
            if (userDto == null)
                return BadRequest("Os dados do usuário são obrigatórios.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            await _userService.AddAsync(userDto, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = userDto.Id }, userDto);
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult> Update(Guid id, [FromBody] UserDto userDto, CancellationToken cancellationToken)
        {
            if (userDto == null)
                return BadRequest("Os dados do usuário são obrigatórios.");

            if (id != userDto.Id)
                return BadRequest("O ID do usuário na URL não corresponde ao ID do corpo da requisição.");

            if (!ModelState.IsValid)
                return BadRequest(ModelState);

            var existingUser = await _userService.GetByIdAsync(id, cancellationToken);
            if (existingUser == null)
                return NotFound("Usuário não encontrado.");

            await _userService.UpdateAsync(userDto, cancellationToken);
            return NoContent();
        }

        [HttpDelete("{id:guid}")]
        public async Task<ActionResult> Delete(Guid id, CancellationToken cancellationToken)
        {
            if (id == Guid.Empty)
                return BadRequest("O ID do usuário não pode ser vazio.");

            var existingUser = await _userService.GetByIdAsync(id, cancellationToken);
            if (existingUser == null)
                return NotFound("Usuário não encontrado.");

            await _userService.DeleteAsync(id, cancellationToken);
            return NoContent();
        }
    }
}