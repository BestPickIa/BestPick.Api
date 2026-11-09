namespace BestPick.Aplicacao.Interfaces;

public interface IFotoUsuarioAplicacao
{
    Task EnviarAsync(int usuarioId, Stream arquivo, string nomeOriginal, string tipoConteudo, CancellationToken cancellationToken = default);
    Task<byte[]> ObterAsync(int solicitanteId, int usuarioId);
    Task RemoverAsync(int usuarioId);
}
