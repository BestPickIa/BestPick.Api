using System.Net;
using BestPick.Servicos.Fotos;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;
using Xunit;

namespace BestPick.Tests;

public class FotosHttpTests
{
    [Theory]
    [InlineData(SKEncodedImageFormat.Jpeg, "foto.jpeg", "image/jpeg")]
    [InlineData(SKEncodedImageFormat.Png, "foto.png", "image/png")]
    public async Task Upload_e_consulta_recodificam_JPEG_sem_expor_caminho(SKEncodedImageFormat formato, string nome, string mime)
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var upload = await ambiente.UploadAsync(token, AmbienteFotos.Imagem(formato), nome, mime);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Equal($"{{\"url\":\"/Usuario/foto/{id}\"}}", await upload.Content.ReadAsStringAsync());
        var usuario = await ambiente.Servidor.ObterAsync();
        Assert.Matches("^[a-f0-9]{32}\\.jpg$", usuario.FotoNomeArquivo);
        Assert.NotNull(usuario.FotoAtualizadaEm);
        using var resposta = await ambiente.ConsultarAsync(token, id);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("image/jpeg", resposta.Content.Headers.ContentType!.MediaType);
        Assert.True(resposta.Headers.CacheControl!.NoStore);
        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        using var dados = SKData.CreateCopy(await resposta.Content.ReadAsByteArrayAsync());
        using var codec = SKCodec.Create(dados);
        Assert.Equal(SKEncodedImageFormat.Jpeg, codec.EncodedFormat);
        Assert.Equal(300, codec.Info.Width);
        Assert.Equal(200, codec.Info.Height);
    }

    [Theory]
    [InlineData("vazio", 400)]
    [InlineData("grande", 413)]
    [InlineData("mime", 400)]
    [InlineData("extensao", 400)]
    [InlineData("falso", 400)]
    [InlineData("corrompido", 400)]
    [InlineData("dimensao", 400)]
    [InlineData("formato", 400)]
    public async Task Upload_invalido_nao_cria_referencia(string caso, int status)
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var bytes = caso switch
        {
            "vazio" => [],
            "grande" => new byte[ProcessadorFotoSkia.MaximoBytes + 1],
            "falso" => "nao-e-imagem"u8.ToArray(),
            "corrompido" => AmbienteFotos.Imagem()[..100],
            "dimensao" => AmbienteFotos.Imagem(largura: 100),
            "formato" => AmbienteFotos.Imagem(SKEncodedImageFormat.Png),
            _ => AmbienteFotos.Imagem()
        };
        using var resposta = await ambiente.UploadAsync(ambiente.Servidor.TokenPara(id.ToString()), bytes,
            caso == "extensao" ? "foto.exe" : "foto.jpg", caso == "mime" ? "image/gif" : "image/jpeg");
        Assert.Equal(status, (int)resposta.StatusCode);
        Assert.Null((await ambiente.Servidor.ObterAsync()).FotoNomeArquivo);
        Assert.False(Directory.Exists(ambiente.Diretorio));
    }

    [Fact]
    public async Task Rotas_exigem_autenticacao_e_conta_ativa()
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste", ativo: false);
        foreach (var token in new[] { null, "invalido", ambiente.Servidor.TokenPara(id.ToString()) })
        {
            using var post = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
            using var get = await ambiente.ConsultarAsync(token, id);
            using var delete = await ambiente.RemoverAsync(token);
            Assert.Equal(HttpStatusCode.Unauthorized, post.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, get.StatusCode);
            Assert.Equal(HttpStatusCode.Unauthorized, delete.StatusCode);
        }
    }

    [Fact]
    public async Task Id_forjado_nao_altera_outra_conta_e_leitura_autenticada_permite_avatar()
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var outro = await ambiente.Servidor.SemearAsync("senhateste", email: "outro@example.test");
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var upload = await ambiente.UploadAsync(token, AmbienteFotos.Imagem(), idForjado: outro);
        Assert.Equal(HttpStatusCode.OK, upload.StatusCode);
        Assert.Null((await ambiente.Servidor.ObterAsync("outro@example.test")).FotoNomeArquivo);
        using var get = await ambiente.ConsultarAsync(ambiente.Servidor.TokenPara(outro.ToString()), id);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        using var removerOutro = await ambiente.RemoverAsync(ambiente.Servidor.TokenPara(outro.ToString()));
        Assert.Equal(HttpStatusCode.NoContent, removerOutro.StatusCode);
        Assert.NotNull((await ambiente.Servidor.ObterAsync()).FotoNomeArquivo);
    }

    [Fact]
    public async Task Substituicao_e_remocao_preservam_senha_e_removem_apenas_arquivo_antigo()
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var antes = await ambiente.Servidor.ObterAsync();
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var primeiro = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        var antigo = (await ambiente.Servidor.ObterAsync()).FotoNomeArquivo;
        using var segundo = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        var atual = await ambiente.Servidor.ObterAsync();
        Assert.NotEqual(antigo, atual.FotoNomeArquivo);
        Assert.False(File.Exists(Path.Combine(ambiente.Diretorio, antigo)));
        Assert.Equal(antes.Senha, atual.Senha);
        Assert.Equal(antes.Email, atual.Email);
        using var remover = await ambiente.RemoverAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, remover.StatusCode);
        Assert.Null((await ambiente.Servidor.ObterAsync()).FotoNomeArquivo);
        Assert.Empty(Directory.GetFiles(ambiente.Diretorio));
        using var inexistente = await ambiente.ConsultarAsync(token, id);
        Assert.Equal(HttpStatusCode.NotFound, inexistente.StatusCode);
    }

    [Theory]
    [InlineData("arquivo")]
    [InlineData("permissao")]
    [InlineData("banco_upload")]
    [InlineData("banco_remocao")]
    public async Task Falha_preserva_foto_anterior_e_nao_expoe_detalhes(string caso)
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var primeiro = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        var antes = await ambiente.Servidor.ObterAsync();
        ambiente.Arquivos.FalharGravacao = caso == "arquivo";
        ambiente.Arquivos.SemPermissao = caso == "permissao";
        ambiente.Servidor.Falha.Ativa = caso.StartsWith("banco");
        using var falha = caso == "banco_remocao" ? await ambiente.RemoverAsync(token) : await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        Assert.Equal(HttpStatusCode.InternalServerError, falha.StatusCode);
        var corpo = await falha.Content.ReadAsStringAsync();
        Assert.DoesNotContain("detalhe-interno", corpo);
        Assert.DoesNotContain(ambiente.Diretorio, corpo);
        Assert.Equal(antes.FotoNomeArquivo, (await ambiente.Servidor.ObterAsync()).FotoNomeArquivo);
        Assert.Single(Directory.GetFiles(ambiente.Diretorio));
        using var foto = await ambiente.ConsultarAsync(token, id);
        Assert.Equal(HttpStatusCode.OK, foto.StatusCode);
    }

    [Fact]
    public async Task Falha_de_limpeza_nao_desfaz_upload_confirmado()
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var primeiro = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        var antes = await ambiente.Servidor.ObterAsync();
        ambiente.Arquivos.FalharRemocao = true;
        using var segundo = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        Assert.Equal(HttpStatusCode.OK, segundo.StatusCode);
        Assert.NotEqual(antes.FotoNomeArquivo, (await ambiente.Servidor.ObterAsync()).FotoNomeArquivo);
        Assert.Equal(2, Directory.GetFiles(ambiente.Diretorio).Length);
        Assert.Contains(ambiente.Servidor.Logs.Mensagens, x => x.Contains("Limpeza de foto pendente"));
        Assert.DoesNotContain(ambiente.Servidor.Logs.Mensagens, x => x.Contains("detalhe-interno-limpeza"));
    }

    [Fact]
    public async Task Arquivo_referenciado_por_outra_conta_inativa_nao_e_apagado()
    {
        await using var ambiente = await AmbienteFotos.CriarAsync();
        var id = await ambiente.Servidor.SemearAsync("senhateste");
        var outro = await ambiente.Servidor.SemearAsync("senhateste", ativo: false, email: "outro@example.test");
        var token = ambiente.Servidor.TokenPara(id.ToString());
        using var primeiro = await ambiente.UploadAsync(token, AmbienteFotos.Imagem());
        var nome = (await ambiente.Servidor.ObterAsync()).FotoNomeArquivo;
        using (var escopo = ambiente.Servidor.Servicos.CreateScope())
        {
            var contexto = escopo.ServiceProvider.GetRequiredService<BestPickContexto>();
            (await contexto.Usuarios.SingleAsync(x => x.Id == outro)).FotoNomeArquivo = nome;
            await contexto.SaveChangesAsync();
        }
        using var remover = await ambiente.RemoverAsync(token);
        Assert.Equal(HttpStatusCode.NoContent, remover.StatusCode);
        Assert.True(File.Exists(Path.Combine(ambiente.Diretorio, nome)));
    }
}
