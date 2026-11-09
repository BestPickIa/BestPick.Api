using System.ComponentModel.DataAnnotations;
using System.Text;
using BestPick.Repositorio.Interfaces;
using BestPick.Repositorio.Repositorios;
using BestPick.Servicos.Fotos;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace BestPick.Tests;

public class FotosSegurancaTests
{
    [Fact]
    public async Task Redimensiona_proporcionalmente_e_preserva_orientacao_apos_remover_EXIF()
    {
        using var processador = new ProcessadorFotoSkia();
        using var entrada = new MemoryStream(AmbienteFotos.Imagem(largura: 4000, altura: 2000));
        var bytes = await processador.ProcessarAsync(entrada, "foto.jpg", "image/jpeg");
        using var saida = SKBitmap.Decode(bytes);
        Assert.Equal(2048, saida.Width);
        Assert.Equal(1024, saida.Height);

        var jpeg = AmbienteFotos.Imagem();
        byte[] exif = [69, 120, 105, 102, 0, 0, 73, 73, 42, 0, 8, 0, 0, 0,
            1, 0, 18, 1, 3, 0, 1, 0, 0, 0, 6, 0, 0, 0, 0, 0, 0, 0];
        using var comExif = new MemoryStream();
        comExif.Write(jpeg.AsSpan(0, 2));
        comExif.Write([255, 225, 0, (byte)(exif.Length + 2)]);
        comExif.Write(exif);
        comExif.Write(jpeg.AsSpan(2));
        comExif.Position = 0;
        var limpo = await processador.ProcessarAsync(comExif, "foto.jpg", "image/jpeg");
        using var rotacionado = SKBitmap.Decode(limpo);
        Assert.Equal(200, rotacionado.Width);
        Assert.Equal(300, rotacionado.Height);
        Assert.DoesNotContain("Exif", Encoding.Latin1.GetString(limpo));
    }

    [Theory]
    [InlineData(199, 200)]
    [InlineData(5001, 200)]
    [InlineData(4100, 4100)]
    public async Task Dimensoes_e_orcamento_de_pixels_sao_verificados(int largura, int altura)
    {
        using var processador = new ProcessadorFotoSkia();
        using var arquivo = new MemoryStream(AmbienteFotos.Imagem(largura: largura, altura: altura));
        await Assert.ThrowsAsync<ValidationException>(() => processador.ProcessarAsync(arquivo, "foto.jpg", "image/jpeg"));
    }

    [Fact]
    public async Task Limite_de_bytes_e_verificado_tambem_fora_do_HTTP()
    {
        using var processador = new ProcessadorFotoSkia();
        using var arquivo = new MemoryStream(new byte[ProcessadorFotoSkia.MaximoBytes + 1]);
        await Assert.ThrowsAsync<ValidationException>(() => processador.ProcessarAsync(arquivo, "foto.jpg", "image/jpeg"));
    }

    [Theory]
    [InlineData("../externo.jpg")]
    [InlineData("..\\externo.jpg")]
    [InlineData("C:\\externo.jpg")]
    [InlineData("foto.jpg")]
    public async Task Armazenamento_rejeita_caminhos_fornecidos_pelo_cliente(string nome)
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        await Assert.ThrowsAsync<IOException>(() => ambiente.Arquivos.LerAsync(nome));
        await Assert.ThrowsAsync<IOException>(() => ambiente.Arquivos.RemoverAsync(nome));
        Assert.False(Directory.Exists(ambiente.Diretorio));
    }

    [Fact]
    public void Armazenamento_nao_pode_ficar_na_raiz_publica()
    {
        var publico = Path.Combine(Path.GetTempPath(), "wwwroot-teste");
        Assert.Throws<InvalidOperationException>(() => new ArmazenamentoFotosLocal(Path.Combine(publico, "fotos"), publico));
    }

    [Theory]
    [InlineData("upload")]
    [InlineData("remocao")]
    [InlineData("senha")]
    [InlineData("desativacao")]
    public async Task Atualizacao_atrasada_nao_sobrescreve_operacao_concorrente(string operacao)
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        using var upload = await ambiente.UploadAsync(ambiente.Servidor.TokenPara(id.ToString()), AmbienteFotos.Imagem());
        using var escopo1 = ambiente.Servidor.Servicos.CreateScope();
        using var escopo2 = ambiente.Servidor.Servicos.CreateScope();
        var contexto1 = escopo1.ServiceProvider.GetRequiredService<BestPickContexto>();
        var contexto2 = escopo2.ServiceProvider.GetRequiredService<BestPickContexto>();
        var antigo = await contexto1.Usuarios.SingleAsync(x => x.Id == id);
        var atual = await contexto2.Usuarios.SingleAsync(x => x.Id == id);
        var nomeAnterior = antigo.FotoNomeArquivo;
        if (operacao == "upload") atual.FotoNomeArquivo = Guid.NewGuid().ToString("N") + ".jpg";
        if (operacao == "remocao") atual.FotoNomeArquivo = null!;
        if (operacao == "senha") atual.Senha = new Microsoft.AspNetCore.Identity.PasswordHasher<BestPick.Dominio.Entidades.Usuario>().HashPassword(atual, "outrasenha");
        if (operacao == "desativacao") atual.Ativo = false;
        await contexto2.SaveChangesAsync();
        var repositorio = escopo1.ServiceProvider.GetRequiredService<IFotoUsuarioRepositorio>();
        Assert.False(await repositorio.AtualizarAsync(antigo, Guid.NewGuid().ToString("N") + ".jpg", DateTime.UtcNow));
        Assert.Equal(nomeAnterior, antigo.FotoNomeArquivo);
        Assert.False(contexto1.Entry(antigo).Property(x => x.FotoNomeArquivo).IsModified);
        var persistido = await ambiente.Servidor.ObterAsync();
        Assert.Equal(atual.FotoNomeArquivo, persistido.FotoNomeArquivo);
        Assert.Equal(atual.Senha, persistido.Senha);
        Assert.Equal(atual.Ativo, persistido.Ativo);
    }

    [Fact]
    public async Task Alteracao_generica_antiga_nao_reintroduz_referencia_removida()
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var upload = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        using var escopo = ambiente.Servidor.Servicos.CreateScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        var antigo = await contexto.Usuarios.SingleAsync(x => x.Id == id);
        using var remocao = await ambiente.RemoverAsync(token);
        antigo.Nome = "Nome concorrente";
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new UsuarioRepositorio(contexto).AtualizarAsync(antigo));
        Assert.Null((await ambiente.Servidor.ObterAsync()).FotoNomeArquivo);
    }

    [Fact]
    public void Migration_gera_somente_duas_colunas_novas_sem_conectar_ao_banco()
    {
        using var contexto = new BestPickContexto(new DbContextOptionsBuilder<BestPickContexto>()
            .UseSqlServer("Server=127.0.0.1,1;Database=TesteNaoConectar;Integrated Security=True").Options);
        var script = contexto.GetService<IMigrator>().GenerateScript(
            "20261006180816_TrocarSpPorView", "20261106000000_AdicionarFotoUsuario");
        Assert.Contains("ADD [FotoNomeArquivo] nvarchar(36) NULL", script);
        Assert.Contains("ADD [FotoAtualizadaEm] datetime2 NULL", script);
        Assert.DoesNotContain("DROP", script);
        Assert.DoesNotContain("ALTER COLUMN", script);
        Assert.DoesNotContain("UPDATE ", script);
        Assert.DoesNotContain("DELETE ", script);
    }
}
