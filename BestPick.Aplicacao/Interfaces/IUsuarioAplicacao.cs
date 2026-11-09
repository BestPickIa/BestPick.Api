using BestPick.Aplicacao.Models;

namespace BestPick.Aplicacao.Interfaces;

public interface IUsuarioAplicacao
{
    Task<UsuarioRespostaDto> AdicionarAsync(UsuarioCriar usuario);
    Task AlterarSenhaAsync(int usuarioId, AlterarSenhaRequest request);
    Task<UsuarioRespostaDto> ObterPorIdAsync(int id);
    Task AtualizarAsync(UsuarioAtualizar usuario, int usuarioId);
    Task RemoverAsync(int id);
    Task DeletarPermanentementeAsync(int usuarioId, string senha);
    Task<LoginResposta> LoginAsync(Login login);
}
