using System.ComponentModel.DataAnnotations;
using BestPick.Aplicacao.Aplicacoes;
using BestPick.Aplicacao.Excecoes;
using BestPick.Aplicacao.Interfaces;
using BestPick.Aplicacao.Models;
using BestPick.Dominio.Entidades;
using BestPick.Repositorio.Interfaces;
using BestPick.Repositorio.Repositorios;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace BestPick.Tests;

public class SenhasPersistenciaTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Atualizacao_concorrente_nao_sobrescreve_troca_ou_desativacao(bool desativacao)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync("senhaoriginal");
        using var primeiroEscopo = servidor.Servicos.CreateScope();
        using var segundoEscopo = servidor.Servicos.CreateScope();
        var primeiro = primeiroEscopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        var segundo = segundoEscopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        var usuarioAntigo = await primeiro.Usuarios.SingleAsync(x => x.Id == id);
        var hashAnterior = usuarioAntigo.Senha;
        var usuarioAtual = await segundo.Usuarios.SingleAsync(x => x.Id == id);
        var hasher = new PasswordHasher<Usuario>();
        var hashConcorrente = hasher.HashPassword(usuarioAtual, "senhaconcorrente");
        if (desativacao)
            usuarioAtual.Ativo = false;
        else
            usuarioAtual.Senha = hashConcorrente;
        await segundo.SaveChangesAsync();

        var resultado = await new UsuarioRepositorio(primeiro)
            .AtualizarSenhaAsync(usuarioAntigo, hasher.HashPassword(usuarioAntigo, "senhatardia"));
        Assert.False(resultado);
        Assert.Equal(hashAnterior, usuarioAntigo.Senha);
        Assert.False(primeiro.Entry(usuarioAntigo).Property(x => x.Senha).IsModified);
        Assert.Equal(hashAnterior, primeiro.Entry(usuarioAntigo).Property(x => x.Senha).OriginalValue);
        var persistido = await servidor.ObterAsync();
        Assert.Equal(desativacao ? hashAnterior : hashConcorrente, persistido.Senha);
        Assert.Equal(!desativacao, persistido.Ativo);
    }

    [Fact]
    public async Task Atualizacao_de_senha_preserva_nome_editado_em_outro_contexto()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync("senhaoriginal");
        using var primeiroEscopo = servidor.Servicos.CreateScope();
        using var segundoEscopo = servidor.Servicos.CreateScope();
        var primeiro = primeiroEscopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        var segundo = segundoEscopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        var usuario = await primeiro.Usuarios.SingleAsync(x => x.Id == id);
        var outro = await segundo.Usuarios.SingleAsync(x => x.Id == id);
        outro.Nome = "Nome atualizado concorrentemente";
        outro.Email = "alterado@example.test";
        await segundo.SaveChangesAsync();
        var novoHash = new PasswordHasher<Usuario>().HashPassword(usuario, "senhanova");
        Assert.True(await new UsuarioRepositorio(primeiro).AtualizarSenhaAsync(usuario, novoHash));
        var persistido = await servidor.ObterAsync("alterado@example.test");
        Assert.Equal(outro.Nome, persistido.Nome);
        Assert.Equal(outro.Email, persistido.Email);
        Assert.Equal(novoHash, persistido.Senha);
    }

    [Fact]
    public async Task Falha_na_gravacao_restaura_hash_rastreado_e_permite_nova_tentativa()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync("senhaoriginal");
        using var escopo = servidor.Servicos.CreateScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        var usuario = await contexto.Usuarios.SingleAsync(x => x.Id == id);
        var antes = usuario.Senha;
        var novoHash = new PasswordHasher<Usuario>().HashPassword(usuario, "senhanova");
        var repositorio = new UsuarioRepositorio(contexto);
        servidor.Falha.Ativa = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => repositorio.AtualizarSenhaAsync(usuario, novoHash));
        Assert.Equal(antes, usuario.Senha);
        Assert.Equal(antes, contexto.Entry(usuario).Property(x => x.Senha).OriginalValue);
        Assert.False(contexto.Entry(usuario).Property(x => x.Senha).IsModified);
        Assert.Equal(antes, (await servidor.ObterAsync()).Senha);
        servidor.Falha.Ativa = false;
        Assert.True(await repositorio.AtualizarSenhaAsync(usuario, novoHash));
        Assert.Equal(novoHash, (await servidor.ObterAsync()).Senha);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(41)]
    public async Task Aplicacao_valida_cadastro_mesmo_sem_pipeline_HTTP(int tamanho)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var escopo = servidor.Servicos.CreateScope();
        var aplicacao = escopo.ServiceProvider.GetRequiredService<IUsuarioAplicacao>();
        var senha = new string('a', tamanho);
        await Assert.ThrowsAsync<ValidationException>(() => aplicacao.AdicionarAsync(new UsuarioCriar
        {
            Nome = "Pessoa teste", Email = "novo@example.test", Senha = senha, ConfirmarSenha = senha
        }));
        Assert.Empty(await escopo.ServiceProvider.GetRequiredService<BestPickContexto>().Usuarios.ToListAsync());
    }

    [Theory]
    [InlineData("cadastro")]
    [InlineData("alteracao")]
    public async Task Aplicacao_valida_confirmacao_mesmo_sem_pipeline_HTTP(string operacao)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync("senhaoriginal");
        using var escopo = servidor.Servicos.CreateScope();
        var aplicacao = escopo.ServiceProvider.GetRequiredService<IUsuarioAplicacao>();
        var antes = await servidor.ObterAsync();
        if (operacao == "cadastro")
            await Assert.ThrowsAsync<ValidationException>(() => aplicacao.AdicionarAsync(new UsuarioCriar
            {
                Nome = "Pessoa teste", Email = "segunda@example.test", Senha = "senhanova", ConfirmarSenha = "divergente"
            }));
        else
            await Assert.ThrowsAsync<ValidationException>(() => aplicacao.AlterarSenhaAsync(id, new AlterarSenhaRequest
            {
                SenhaAtual = "senhaoriginal", SenhaNova = "senhanova", ConfirmarSenha = "divergente"
            }));
        Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("alteracao")]
    public async Task Conta_inativa_e_rejeitada_tambem_pela_aplicacao(string operacao)
    {
        var usuario = new Usuario { Id = 1, Nome = "Teste", Email = "novo@example.test", Ativo = false };
        var hasher = new PasswordHasher<Usuario>();
        usuario.Senha = hasher.HashPassword(usuario, "senhaoriginal");
        var repo = new RepositorioControlado(usuario);
        var fotoMock = new FotoUsuarioAplicacaoMock();
        var app = new UsuarioAplicacao(repo, new ConfigurationBuilder().Build(), hasher, fotoMock);
        if (operacao == "login")
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.LoginAsync(new Login
            { Email = usuario.Email, Senha = "senhaoriginal" }));
        else
            await Assert.ThrowsAsync<UnauthorizedAccessException>(() => app.AlterarSenhaAsync(usuario.Id,
                new AlterarSenhaRequest { SenhaAtual = "senhaoriginal", SenhaNova = "senhanova", ConfirmarSenha = "senhanova" }));
        Assert.Equal(0, repo.Atualizacoes);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("alteracao")]
    public async Task Conflito_de_persistencia_interrompe_rehash_ou_troca_sem_emitir_resultado(string operacao)
    {
        var usuario = new Usuario { Id = 1, Nome = "Teste", Email = "novo@example.test" };
        var antigo = new PasswordHasher<Usuario>(Options.Create(new PasswordHasherOptions { IterationCount = 1000 }));
        usuario.Senha = antigo.HashPassword(usuario, "senhaoriginal");
        var hashAntes = usuario.Senha;
        var repo = new RepositorioControlado(usuario) { Conflito = true };
        var fotoMock = new FotoUsuarioAplicacaoMock();
        // Sem configuração JWT: sucesso prematuro falharia de outra forma e não passaria este teste.
        var app = new UsuarioAplicacao(repo, new ConfigurationBuilder().Build(), new PasswordHasher<Usuario>(), fotoMock);
        if (operacao == "login")
            await Assert.ThrowsAsync<ConflitoSenhaException>(() => app.LoginAsync(new Login
            { Email = usuario.Email, Senha = "senhaoriginal" }));
        else
            await Assert.ThrowsAsync<ConflitoSenhaException>(() => app.AlterarSenhaAsync(usuario.Id,
                new AlterarSenhaRequest { SenhaAtual = "senhaoriginal", SenhaNova = "senhanova", ConfirmarSenha = "senhanova" }));
        Assert.Equal(1, repo.Atualizacoes);
        Assert.Equal(hashAntes, usuario.Senha);
    }

    private sealed class RepositorioControlado(Usuario usuario) : IUsuarioRepositorio
    {
        public bool Conflito { get; init; }
        public int Atualizacoes { get; private set; }
        public Task<Usuario> ObterPorEmailAsync(string email) => Task.FromResult(usuario);
        public Task<Usuario> ObterPorIdAsync(int id) => Task.FromResult(usuario);
        public Task<bool> AtualizarSenhaAsync(Usuario alvo, string novoHash)
        {
            Atualizacoes++;
            if (Conflito) return Task.FromResult(false);
            alvo.Senha = novoHash;
            return Task.FromResult(true);
        }
        public Task<Usuario> AdicionarAsync(Usuario alvo) => throw new NotSupportedException();
        public Task<IEnumerable<Usuario>> ListarTodosAsync() => throw new NotSupportedException();
        public Task RemoverAsync(int id) => throw new NotSupportedException();
        public Task DeletarPermanentementeAsync(int id) => throw new NotSupportedException();
        public Task AtualizarAsync(Usuario alvo) => throw new NotSupportedException();
    }

    private sealed class FotoUsuarioAplicacaoMock : IFotoUsuarioAplicacao
    {
        public Task EnviarAsync(int usuarioId, Stream arquivo, string nomeOriginal, string tipoConteudo, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<byte[]?> ObterAsync(int solicitanteId, int usuarioId) => Task.FromResult<byte[]?>(null);
        public Task RemoverAsync(int usuarioId) => Task.CompletedTask;
    }
}
