using System.Security.Claims;
using BestPick.Aplicacao.Interfaces;
using BestPick.Aplicacao.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using BestPick.Aplicacao.Excecoes;
using BestPick.Api.Attributes;

namespace BestPick.Api.Controllers;

[ApiController]
[Route("[controller]")]
public class UsuarioController : ControllerBase
{
    private readonly IUsuarioAplicacao _usuarioAplicacao;

    public UsuarioController(IUsuarioAplicacao usuarioAplicacao)
    {
        _usuarioAplicacao = usuarioAplicacao;
    }

    [HttpPost]
    public async Task<IActionResult> AdicionarAsync([FromBody] UsuarioCriar usuario)
    {
        try
        {
            var resultado = await _usuarioAplicacao.AdicionarAsync(usuario);

            return Ok(resultado);
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Não foi possível cadastrar o usuário. Tente novamente.");
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> LoginAsync([FromBody] Login login)
    {
        try
        {
            var resultado = await _usuarioAplicacao.LoginAsync(login);

            return Ok(resultado);
        }
        catch (UnauthorizedAccessException)
        {
            return BadRequest("Email ou senha inválidos.");
        }
        catch (ConflitoSenhaException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Não foi possível realizar o login. Tente novamente.");
        }
    }

    [Authorize]

    [HttpPut("senha")]
    public async Task<IActionResult> AlterarSenhaAsync([FromBody] AlterarSenhaRequest request)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId) || usuarioId <= 0)
            return Unauthorized();

        try
        {
            await _usuarioAplicacao.AlterarSenhaAsync(usuarioId, request);
            return Ok(new { mensagem = "Senha alterada com sucesso." });
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            // Senha atual incorreta deve ser HTTP 400 (BadRequest), não 401
            return BadRequest(ex.Message);
        }
        catch (ConflitoSenhaException ex)
        {
            return Conflict(ex.Message);
        }
        catch (Exception)
        {
            return StatusCode(500, "Não foi possível alterar a senha. Tente novamente.");
        }
    }

    [Authorize]

    [HttpGet]
    public async Task<IActionResult> ObterAsync()
    {
        try
        {
            var usuarioId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier).Value
            );

            var resultado = await _usuarioAplicacao.ObterPorIdAsync(usuarioId);

            return Ok(resultado);
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]

    [HttpPut]
    public async Task<IActionResult> AtualizarAsync(
        [FromBody] UsuarioAtualizar usuario)
    {
        try
        {
            var usuarioId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier).Value
            );

            await _usuarioAplicacao.AtualizarAsync(usuario, usuarioId);

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]
    [Authorize]

    [HttpDelete]
    public async Task<IActionResult> RemoverAsync()
    {
        try
        {
            var usuarioId = int.Parse(
                User.FindFirst(ClaimTypes.NameIdentifier).Value
            );

            await _usuarioAplicacao.RemoverAsync(usuarioId);

            return Ok();
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [Authorize]

    [HttpDelete("permanente")]
    public async Task<IActionResult> DeletarPermanentementeAsync([FromBody] ConfirmarExclusaoRequest request)
    {
        if (!int.TryParse(User.FindFirst(ClaimTypes.NameIdentifier)?.Value, out var usuarioId) || usuarioId <= 0)
            return Unauthorized();

        try
        {
            if (request == null)
                throw new ValidationException("Informe os dados para confirmar a exclusão.");

            System.ComponentModel.DataAnnotations.Validator.ValidateObject(request, 
                new System.ComponentModel.DataAnnotations.ValidationContext(request), 
                validateAllProperties: true);

            await _usuarioAplicacao.DeletarPermanentementeAsync(usuarioId, request.Senha);

            return Ok(new { mensagem = "Conta excluída permanentemente." });
        }
        catch (ValidationException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (UnauthorizedAccessException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (Exception ex)
        {
            return StatusCode(500, "Não foi possível excluir a conta. Tente novamente.");
        }
    }
}
