using System.Security.Claims;
using BestPick.Aplicacao.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace BestPick.Api.Attributes;

/// <summary>
/// Atributo que valida se a conta do usuário autenticado ainda está ativa.
/// Impede que tokens de contas deletadas continuem acessando recursos.
/// </summary>
public class ValidarContaAtivaAttribute : Attribute, IAsyncActionFilter
{
    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var usuarioId = context.HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        
        if (!int.TryParse(usuarioId, out var id) || id <= 0)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var usuarioAplicacao = context.HttpContext.RequestServices.GetService<IUsuarioAplicacao>();
        
        if (usuarioAplicacao == null)
        {
            context.Result = new StatusCodeResult(500);
            return;
        }

        try
        {
            // Tentar obter o usuário; se for null ou inativo, lança exceção
            await usuarioAplicacao.ObterPorIdAsync(id);
            await next();
        }
        catch
        {
            // Conta não existe ou foi deletada
            context.Result = new UnauthorizedResult();
        }
    }
}
