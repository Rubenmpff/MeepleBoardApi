using System.Net;
using MeepleBoard.Services.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace MeepleBoardApi.Controllers
{
    /// <summary>
    /// Account deletion requested through support (docs/proposta-eliminacao-conta.md in the app, section 2): an administrator
    /// registers the request; the account's own address receives a single-use link; the page behind it deletes only on POST
    /// (mail scanners open links on their own), then confirms by email. The answer never reveals whether an account exists.
    /// </summary>
    [Route("MeepleBoard")]
    [ApiController]
    public class AccountDeletionController(IAccountDeletionService deletion) : ControllerBase
    {
        public sealed record SupportRequest(string? Email);

        [Authorize(Roles = "Admin")]
        [HttpPost("admin/account-deletion/requests")]
        public async Task<IActionResult> RequestThroughSupport([FromBody] SupportRequest request, CancellationToken ct)
        {
            await deletion.RequestThroughSupportAsync(request?.Email ?? "", $"{Request.Scheme}://{Request.Host}{Request.PathBase}/MeepleBoard/account-deletion/confirm", ct);
            return Accepted(new { message = "Se o endereço pertencer a uma conta, foi enviada uma ligação de confirmação para esse endereço." });
        }

        [AllowAnonymous]
        [HttpGet("account-deletion/confirm")]
        public async Task<IActionResult> Confirm([FromQuery] Guid u, [FromQuery] string? t, CancellationToken ct)
        {
            if (!await deletion.IsValidLinkAsync(u, t ?? "", ct)) return Page(HttpStatusCode.BadRequest, Invalid);
            return Page(HttpStatusCode.OK, $"""
                <h1>Eliminar a conta MeepleBoard</h1>
                <p>Vais eliminar a tua conta. O acesso termina e os teus dados pessoais, amigos, coleção, notas, avaliações, fotografias e publicações são apagados. Nas partidas partilhadas, o teu lugar fica como «Conta eliminada», para não mudar o histórico dos outros. Não pode ser desfeito.</p>
                <form method="post" action="confirm">
                  <input type="hidden" name="u" value="{WebUtility.HtmlEncode(u.ToString())}">
                  <input type="hidden" name="t" value="{WebUtility.HtmlEncode(t)}">
                  <button type="submit">Eliminar conta</button>
                </form>
                <p class="note">Se não pediste a eliminação, fecha esta página: a conta continua ativa.</p>
                """);
        }

        [AllowAnonymous]
        [HttpPost("account-deletion/confirm")]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> ConfirmPost([FromForm] Guid u, [FromForm] string? t, CancellationToken ct)
        {
            if (!await deletion.ConfirmThroughSupportAsync(u, t ?? "", ct)) return Page(HttpStatusCode.BadRequest, Invalid);
            return Page(HttpStatusCode.OK, """
                <h1>Conta eliminada</h1>
                <p>A tua conta MeepleBoard foi eliminada e enviámos uma confirmação para o endereço da conta. Cópias técnicas (cópias de segurança e registos de fornecedores) desaparecem no fim do respetivo prazo, descrito na política de privacidade.</p>
                """);
        }

        const string Invalid = """
            <h1>Ligação inválida ou expirada</h1>
            <p>Esta ligação já foi usada, expirou (24 horas) ou não é válida. Nada foi alterado. Para eliminar a conta, faz um novo pedido ao apoio ou usa a app: Perfil › Definições › Eliminar conta.</p>
            """;

        ContentResult Page(HttpStatusCode status, string body) => new()
        {
            StatusCode = (int)status,
            ContentType = "text/html; charset=utf-8",
            Content = $"""
                <!doctype html><html lang="pt"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
                <meta name="robots" content="noindex"><title>MeepleBoard</title>
                <style>body{"{"}font-family:-apple-system,system-ui,sans-serif;max-width:34rem;margin:2rem auto;padding:0 1rem;color:#30283D;background:#FBFBF8;line-height:1.5{"}"}
                button{"{"}font:inherit;font-weight:600;color:#fff;background:#B3261E;border:0;border-radius:10px;padding:.8rem 1.4rem;min-height:44px{"}"}.note{"{"}color:#58616A{"}"}</style>
                </head><body>{body}</body></html>
                """,
        };
    }
}
