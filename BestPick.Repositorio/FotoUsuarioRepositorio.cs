using BestPick.Dominio.Entidades;
using BestPick.Repositorio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace BestPick.Repositorio.Repositorios;

public class FotoUsuarioRepositorio : IFotoUsuarioRepositorio
{
    private readonly BestPickContexto _contexto;
    public FotoUsuarioRepositorio(BestPickContexto contexto) => _contexto = contexto;

    public Task<Usuario> ObterAtivoAsync(int id) =>
        _contexto.Usuarios.FirstOrDefaultAsync(x => x.Id == id && x.Ativo);

    // Inclui contas inativas: seus arquivos também não podem ser apagados.
    public Task<bool> ReferenciadaAsync(string nomeArquivo) =>
        _contexto.Usuarios.AnyAsync(x => x.FotoNomeArquivo == nomeArquivo);

    public async Task<bool> AtualizarAsync(Usuario usuario, string nomeArquivo, DateTime? atualizadaEm)
    {
        var entrada = _contexto.Entry(usuario);
        if (entrada.State == EntityState.Detached) _contexto.Usuarios.Attach(usuario);
        var nome = entrada.Property(x => x.FotoNomeArquivo);
        var data = entrada.Property(x => x.FotoAtualizadaEm);
        var nomeAnterior = nome.CurrentValue;
        var dataAnterior = data.CurrentValue;
        nome.CurrentValue = nomeArquivo;
        data.CurrentValue = atualizadaEm;
        nome.IsModified = true;
        data.IsModified = true;
        try
        {
            await _contexto.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            Restaurar();
            return false;
        }
        catch
        {
            Restaurar();
            throw;
        }

        void Restaurar()
        {
            nome.CurrentValue = nomeAnterior;
            data.CurrentValue = dataAnterior;
            nome.IsModified = false;
            data.IsModified = false;
        }
    }
}
