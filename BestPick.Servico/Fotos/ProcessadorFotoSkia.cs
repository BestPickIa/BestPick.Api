using System.ComponentModel.DataAnnotations;
using SkiaSharp;

namespace BestPick.Servicos.Fotos;

public sealed class ProcessadorFotoSkia : IProcessadorFoto, IDisposable
{
    public const int MaximoBytes = 5 * 1024 * 1024;
    private readonly SemaphoreSlim _decodificacoes = new(2);

    public async Task<byte[]> ProcessarAsync(Stream arquivo, string nomeOriginal, string tipoConteudo,
        CancellationToken cancellationToken = default)
    {
        var extensao = Path.GetExtension(nomeOriginal ?? "").ToLowerInvariant();
        var jpeg = extensao is ".jpg" or ".jpeg" && tipoConteudo == "image/jpeg";
        var png = extensao == ".png" && tipoConteudo == "image/png";
        if (!jpeg && !png) throw new ValidationException("Envie uma imagem JPEG ou PNG com extensão e tipo correspondentes.");

        await _decodificacoes.WaitAsync(cancellationToken);
        try
        {
            using var entrada = new MemoryStream();
            var buffer = new byte[81920];
            int lidos;
            while ((lidos = await arquivo.ReadAsync(buffer, cancellationToken)) != 0)
            {
                if (entrada.Length + lidos > MaximoBytes)
                    throw new ValidationException("A foto deve ter no máximo 5 MB.");
                await entrada.WriteAsync(buffer.AsMemory(0, lidos), cancellationToken);
            }
            if (entrada.Length == 0) throw new ValidationException("Envie uma imagem não vazia.");
            using var dados = SKData.CreateCopy(entrada.GetBuffer().AsSpan(0, (int)entrada.Length));
            using var codec = SKCodec.Create(dados);
            if (codec == null || codec.EncodedFormat != (jpeg ? SKEncodedImageFormat.Jpeg : SKEncodedImageFormat.Png))
                throw new ValidationException("O conteúdo não corresponde a uma imagem JPEG ou PNG válida.");

            var info = codec.Info;
            // Inspeção antes da alocação de pixels: até 64 MB de raster RGBA por operação.
            if (info.Width < 200 || info.Height < 200 || info.Width > 5000 || info.Height > 5000 ||
                (long)info.Width * info.Height > 16_000_000 || codec.FrameCount > 1)
                throw new ValidationException("Use uma imagem estática de 200 a 5000 pixels por lado e até 16 megapixels.");

            using var original = new SKBitmap(new SKImageInfo(info.Width, info.Height, SKColorType.Rgba8888, SKAlphaType.Premul));
            if (original.GetPixels() == IntPtr.Zero || codec.GetPixels(original.Info, original.GetPixels()) != SKCodecResult.Success)
                throw new ValidationException("Não foi possível decodificar a imagem completa.");

            var gira = codec.EncodedOrigin is SKEncodedOrigin.LeftTop or SKEncodedOrigin.RightTop or
                SKEncodedOrigin.RightBottom or SKEncodedOrigin.LeftBottom;
            var largura = gira ? info.Height : info.Width;
            var altura = gira ? info.Width : info.Height;
            var proporcao = Math.Min(1d, 2048d / Math.Max(largura, altura));
            var destinoInfo = new SKImageInfo(Math.Max(1, (int)Math.Round(largura * proporcao)),
                Math.Max(1, (int)Math.Round(altura * proporcao)), SKColorType.Rgba8888, SKAlphaType.Opaque);
            using var destino = new SKBitmap(destinoInfo);
            using (var canvas = new SKCanvas(destino))
            {
                canvas.Clear(SKColors.White);
                canvas.Scale((float)destinoInfo.Width / largura, (float)destinoInfo.Height / altura);
                var matriz = Orientacao(codec.EncodedOrigin, info.Width, info.Height);
                canvas.Concat(in matriz);
                canvas.DrawBitmap(original, 0, 0, new SKSamplingOptions(SKFilterMode.Linear));
                canvas.Flush();
            }
            // Raster novo: EXIF, GPS, comentários, perfis e nome original não são copiados.
            using var imagem = SKImage.FromBitmap(destino);
            using var resultado = imagem.Encode(SKEncodedImageFormat.Jpeg, 85);
            if (resultado == null) throw new IOException("Não foi possível processar a foto.");
            return resultado.ToArray();
        }
        finally { _decodificacoes.Release(); }
    }

    private static SKMatrix Orientacao(SKEncodedOrigin origem, int w, int h)
    {
        var (a, b, c, d, e, f) = origem switch
        {
            SKEncodedOrigin.TopRight => (-1, 0, w, 0, 1, 0),
            SKEncodedOrigin.BottomRight => (-1, 0, w, 0, -1, h),
            SKEncodedOrigin.BottomLeft => (1, 0, 0, 0, -1, h),
            SKEncodedOrigin.LeftTop => (0, 1, 0, 1, 0, 0),
            SKEncodedOrigin.RightTop => (0, -1, h, 1, 0, 0),
            SKEncodedOrigin.RightBottom => (0, -1, h, -1, 0, w),
            SKEncodedOrigin.LeftBottom => (0, 1, 0, -1, 0, w),
            _ => (1, 0, 0, 0, 1, 0)
        };
        return new SKMatrix { ScaleX = a, SkewX = b, TransX = c, SkewY = d, ScaleY = e, TransY = f, Persp2 = 1 };
    }

    public void Dispose() => _decodificacoes.Dispose();
}
