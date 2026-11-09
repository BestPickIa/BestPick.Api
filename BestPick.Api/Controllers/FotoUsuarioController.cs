using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using BestPick.Aplicacao.Excecoes;
using BestPick.Aplicacao.Interfaces;
using BestPick.Servicos.Fotos;
using BestPick.Api.Attributes;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace BestPick.Api.Controllers;

[ApiController]
[Authorize]

[Route("Usuario/foto")]
public class FotoUsuarioController : ControllerBase
{
    private readonly IFotoUsuarioAplicacao _fotos;
    public FotoUsuarioController(IFotoUsuarioAplicacao fotos) => _fotos = fotos;

    private int? UsuarioId => int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) && id > 0 ? id : null;

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6 * 1024 * 1024)]
    [RequestFormLimits(MultipartBodyLengthLimit = 6 * 1024 * 1024)]
    public Task<IActionResult> EnviarAsync([FromForm] IFormFile arquivo) => ExecutarAsync(async id =>
    {
        if (arquivo == null || arquivo.Length == 0) return BadRequest("Envie uma foto não vazia.");
        if (arquivo.Length > ProcessadorFotoSkia.MaximoBytes) return StatusCode(413, "A foto deve ter no máximo 5 MB.");
        await using var stream = arquivo.OpenReadStream();
        await _fotos.EnviarAsync(id, stream, arquivo.FileName, arquivo.ContentType, HttpContext.RequestAborted);
        return Ok(new { url = $"/Usuario/foto/{id}" });
    });

    [HttpGet("{id:int:min(1)}")]
    public Task<IActionResult> ObterAsync(int id) => ExecutarAsync(async solicitante =>
    {
        var foto = await _fotos.ObterAsync(solicitante, id);
        Response.Headers.CacheControl = "private, no-store";
        Response.Headers["X-Content-Type-Options"] = "nosniff";
        return foto == null ? NotFound() : File(foto, "image/jpeg");
    });

    [HttpDelete]
    public Task<IActionResult> RemoverAsync() => ExecutarAsync(async id =>
    {
        await _fotos.RemoverAsync(id);
        return NoContent();
    });

    private async Task<IActionResult> ExecutarAsync(Func<int, Task<IActionResult>> operacao)
    {
        if (UsuarioId is not int id) return Unauthorized();
        try { return await operacao(id); }
        catch (ValidationException ex) { return BadRequest(ex.Message); }
        catch (ContaFotoIndisponivelException) { return Unauthorized(); }
        catch (ConflitoFotoException ex) { return Conflict(ex.Message); }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested) { return StatusCode(499); }
        catch (Exception) { return StatusCode(500, "Não foi possível concluir a operação de foto. Tente novamente."); }
    }
}
