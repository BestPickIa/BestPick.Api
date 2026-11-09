namespace BestPick.Servicos.Fotos;

public interface IArmazenamentoFotos
{
    Task<string> GravarAsync(byte[] jpeg);
    Task<byte[]> LerAsync(string nomeArquivo);
    Task RemoverAsync(string nomeArquivo);
}
