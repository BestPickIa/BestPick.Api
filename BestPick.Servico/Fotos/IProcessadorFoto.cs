namespace BestPick.Servicos.Fotos;

public interface IProcessadorFoto
{
    Task<byte[]> ProcessarAsync(Stream arquivo, string nomeOriginal, string tipoConteudo,
        CancellationToken cancellationToken = default);
}
