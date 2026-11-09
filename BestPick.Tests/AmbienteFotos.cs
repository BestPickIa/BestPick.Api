using System.Net.Http.Headers;
using BestPick.Aplicacao.Aplicacoes;
using BestPick.Aplicacao.Interfaces;
using BestPick.Repositorio.Interfaces;
using BestPick.Repositorio.Repositorios;
using BestPick.Servicos.Fotos;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace BestPick.Tests;

internal sealed class AmbienteFotos : IAsyncDisposable
{
    public required ServidorSenhas Servidor { get; init; }
    public required string Diretorio { get; init; }
    public required ArquivosControlados Arquivos { get; init; }

    public static async Task<AmbienteFotos> CriarAsync()
    {
        var diretorio = Path.Combine(Path.GetTempPath(), "bestpick-fotos-" + Guid.NewGuid().ToString("N"));
        var arquivos = new ArquivosControlados(new ArmazenamentoFotosLocal(diretorio, Path.Combine(diretorio, "wwwroot")));
        var servidor = await ServidorSenhas.CriarAsync(servicos =>
        {
            servicos.AddScoped<IFotoUsuarioRepositorio, FotoUsuarioRepositorio>();
            servicos.AddScoped<IFotoUsuarioAplicacao, FotoUsuarioAplicacao>();
            servicos.AddSingleton<IProcessadorFoto, ProcessadorFotoSkia>();
            servicos.AddSingleton<IArmazenamentoFotos>(arquivos);
        });
        return new AmbienteFotos { Servidor = servidor, Diretorio = diretorio, Arquivos = arquivos };
    }

    public static byte[] Imagem(SKEncodedImageFormat formato = SKEncodedImageFormat.Jpeg, int largura = 300, int altura = 200)
    {
        using var bitmap = new SKBitmap(largura, altura);
        bitmap.Erase(SKColors.Blue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(formato, 90);
        return data.ToArray();
    }

    public Task<HttpResponseMessage> UploadAsync(string? token, byte[] bytes, string nome = "foto.jpg", string mime = "image/jpeg", int? idForjado = null)
    {
        var form = new MultipartFormDataContent();
        var conteudo = new ByteArrayContent(bytes);
        conteudo.Headers.ContentType = new MediaTypeHeaderValue(mime);
        form.Add(conteudo, "arquivo", nome);
        if (idForjado != null) form.Add(new StringContent(idForjado.ToString()!), "usuarioId");
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/Usuario/foto") { Content = form };
        if (token != null) pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Servidor.Cliente.SendAsync(pedido);
    }

    public Task<HttpResponseMessage> ConsultarAsync(string? token, int id) => EnviarAsync(token, HttpMethod.Get, $"/Usuario/foto/{id}");
    public Task<HttpResponseMessage> RemoverAsync(string? token) => EnviarAsync(token, HttpMethod.Delete, "/Usuario/foto");
    private Task<HttpResponseMessage> EnviarAsync(string? token, HttpMethod metodo, string rota)
    {
        var pedido = new HttpRequestMessage(metodo, rota);
        if (token != null) pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Servidor.Cliente.SendAsync(pedido);
    }

    public async ValueTask DisposeAsync()
    {
        await Servidor.DisposeAsync();
        var raiz = Path.GetFullPath(Diretorio);
        var temporario = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
        if (!raiz.StartsWith(temporario, StringComparison.OrdinalIgnoreCase) ||
            !Path.GetFileName(raiz).StartsWith("bestpick-fotos-", StringComparison.Ordinal))
            throw new InvalidOperationException("Diretório temporário de teste inválido.");
        if (Directory.Exists(raiz)) Directory.Delete(raiz, recursive: true);
    }
}

internal sealed class ArquivosControlados(IArmazenamentoFotos interno) : IArmazenamentoFotos
{
    public bool FalharGravacao { get; set; }
    public bool FalharRemocao { get; set; }
    public bool SemPermissao { get; set; }
    public Task<string> GravarAsync(byte[] jpeg) => SemPermissao ? throw new UnauthorizedAccessException("detalhe-interno-permissao") :
        FalharGravacao ? throw new IOException("detalhe-interno-arquivo") : interno.GravarAsync(jpeg);
    public Task<byte[]> LerAsync(string nomeArquivo) => interno.LerAsync(nomeArquivo);
    public Task RemoverAsync(string nomeArquivo) => FalharRemocao ? throw new IOException("detalhe-interno-limpeza") : interno.RemoverAsync(nomeArquivo);
}
