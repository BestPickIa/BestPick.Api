using BestPick.Aplicacao.Aplicacoes;
using BestPick.Aplicacao.Excecoes;
using BestPick.Aplicacao.Interfaces;
using BestPick.Dominio.Entidades;
using BestPick.Repositorio.Interfaces;
using BestPick.Servicos.Fotos;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BestPick.Tests;

public class FotosConcorrenciaAplicacaoTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Uploads_e_remocoes_intercalados_preservam_vencedor_e_compensam_arquivo_perdedor(bool primeiraRemove, bool segundaRemove)
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        using var inicial = await ambiente.UploadAsync(ambiente.Servidor.TokenPara(id.ToString()), AmbienteFotos.Imagem());
        var antes = await ambiente.Servidor.ObterAsync();
        using var escopo = ambiente.Servidor.Servicos.CreateScope();
        var repositorio = new RepositorioIntercalado(
            escopo.ServiceProvider.GetRequiredService<IFotoUsuarioRepositorio>(), async () =>
            {
                using var outroEscopo = ambiente.Servidor.Servicos.CreateScope();
                var outraAplicacao = outroEscopo.ServiceProvider.GetRequiredService<IFotoUsuarioAplicacao>();
                if (segundaRemove) await outraAplicacao.RemoverAsync(id);
                else
                {
                    using var outroArquivo = new MemoryStream(AmbienteFotos.Imagem());
                    await outraAplicacao.EnviarAsync(id, outroArquivo, "segunda.jpg", "image/jpeg");
                }
            });
        var aplicacao = new FotoUsuarioAplicacao(repositorio,
            escopo.ServiceProvider.GetRequiredService<IProcessadorFoto>(), ambiente.Arquivos,
            NullLogger<FotoUsuarioAplicacao>.Instance);
        using var arquivo = new MemoryStream(AmbienteFotos.Imagem());
        await Assert.ThrowsAsync<ConflitoFotoException>(() => primeiraRemove ? aplicacao.RemoverAsync(id) :
            aplicacao.EnviarAsync(id, arquivo, "primeira.jpg", "image/jpeg"));
        var atual = await ambiente.Servidor.ObterAsync();
        Assert.Equal(antes.Senha, atual.Senha);
        if (segundaRemove)
        {
            Assert.Null(atual.FotoNomeArquivo);
            Assert.Empty(Directory.GetFiles(ambiente.Diretorio));
        }
        else
        {
            Assert.NotEqual(antes.FotoNomeArquivo, atual.FotoNomeArquivo);
            Assert.Equal(atual.FotoNomeArquivo, Path.GetFileName(Assert.Single(Directory.GetFiles(ambiente.Diretorio))));
            Assert.NotEmpty(await ambiente.Arquivos.LerAsync(atual.FotoNomeArquivo));
        }
    }

    private sealed class RepositorioIntercalado(IFotoUsuarioRepositorio interno, Func<Task> antesDeAtualizar) : IFotoUsuarioRepositorio
    {
        public Task<Usuario> ObterAtivoAsync(int id) => interno.ObterAtivoAsync(id);
        public Task<bool> ReferenciadaAsync(string nomeArquivo) => interno.ReferenciadaAsync(nomeArquivo);
        public async Task<bool> AtualizarAsync(Usuario usuario, string nomeArquivo, DateTime? atualizadaEm)
        {
            await antesDeAtualizar();
            return await interno.AtualizarAsync(usuario, nomeArquivo, atualizadaEm);
        }
    }
}
