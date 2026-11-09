using BestPick.Dominio.Entidades;

namespace BestPick.Repositorio.Interfaces;

public interface IFotoUsuarioRepositorio
{
    Task<Usuario> ObterAtivoAsync(int id);
    Task<bool> AtualizarAsync(Usuario usuario, string nomeArquivo, DateTime? atualizadaEm);
    Task<bool> ReferenciadaAsync(string nomeArquivo);
}
