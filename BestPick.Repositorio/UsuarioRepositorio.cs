using BestPick.Repositorio.Interfaces;
using BestPick.Dominio.Entidades;
using Microsoft.EntityFrameworkCore;

namespace BestPick.Repositorio.Repositorios;

public class UsuarioRepositorio : IUsuarioRepositorio
{
    private readonly BestPickContexto contexto;
    public UsuarioRepositorio(BestPickContexto bestPickContexto)
    {
        contexto = bestPickContexto;
    }

    public async Task<Usuario> ObterPorIdAsync(int id)
    {
        return await contexto.Usuarios
        .Where(f => f.Id == id)
        .Where(f => f.Ativo == true)
        .FirstOrDefaultAsync();
    }
    public async Task<IEnumerable<Usuario>> ListarTodosAsync()
    {
        return await contexto.Usuarios
        .Where(f => f.Ativo == true)
        .ToListAsync();
    }
    public async Task<Usuario> AdicionarAsync(Usuario usuario)
    {
        await contexto.AddAsync(usuario);
        await contexto.SaveChangesAsync();
        return usuario;
    }
    public async Task RemoverAsync(int id)
    {
        var obtido = await ObterPorIdAsync(id);
        obtido.Ativo = false;
        await contexto.SaveChangesAsync();
    }

    public async Task DeletarPermanentementeAsync(int id)
    {
        var usuario = await contexto.Usuarios.FindAsync(id);
        if (usuario == null)
            throw new Exception("Usuário não encontrado.");

        contexto.Usuarios.Remove(usuario);
        await contexto.SaveChangesAsync();
    }
    public async Task AtualizarAsync(Usuario usuario)
    {
        contexto.Usuarios.Update(usuario);
        await contexto.SaveChangesAsync();
    }

    public async Task<bool> AtualizarSenhaAsync(Usuario usuario, string novoHash)
    {
        var entrada = contexto.Entry(usuario);
        if (entrada.State == EntityState.Detached)
            contexto.Usuarios.Attach(usuario);

        var senha = entrada.Property(x => x.Senha);
        var hashAnterior = senha.CurrentValue;
        senha.CurrentValue = novoHash;
        senha.IsModified = true;

        try
        {
            // Senha e Ativo originais participam do WHERE: uma troca concorrente
            // ou desativação impede que este hash sobrescreva o estado mais recente.
            await contexto.SaveChangesAsync();
            return true;
        }
        catch (DbUpdateConcurrencyException)
        {
            senha.CurrentValue = hashAnterior;
            senha.IsModified = false;
            return false;
        }
        catch
        {
            senha.CurrentValue = hashAnterior;
            senha.IsModified = false;
            throw;
        }
    }
    public async Task<Usuario> ObterPorEmailAsync(string email)
    {
        return await contexto.Usuarios
            .FirstOrDefaultAsync(x => x.Email == email && x.Ativo);
    }
}
