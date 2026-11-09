using System.Text.RegularExpressions;

namespace BestPick.Servicos.Fotos;

public class ArmazenamentoFotosLocal : IArmazenamentoFotos
{
    private readonly string _diretorio;
    private static readonly Regex NomeSeguro = new("\\A[0-9a-f]{32}\\.jpg\\z", RegexOptions.CultureInvariant);

    public ArmazenamentoFotosLocal(string diretorio, string diretorioPublico)
    {
        if (string.IsNullOrWhiteSpace(diretorio) || !Path.IsPathFullyQualified(diretorio))
            throw new InvalidOperationException("Configure um diretório absoluto para fotos.");
        _diretorio = Path.TrimEndingDirectorySeparator(Path.GetFullPath(diretorio));
        var publico = Path.TrimEndingDirectorySeparator(Path.GetFullPath(diretorioPublico));
        if (_diretorio.Equals(publico, StringComparison.OrdinalIgnoreCase) ||
            _diretorio.StartsWith(publico + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("O diretório de fotos deve ficar fora dos arquivos públicos.");
        ValidarDiretorio();
    }

    private void ValidarDiretorio()
    {
        // A pasta deve ser privada ao serviço. Não seguir links/junctions preexistentes.
        for (var dir = new DirectoryInfo(_diretorio); dir != null; dir = dir.Parent)
            if (dir.Exists && (dir.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Diretório de fotos indisponível.");
    }

    private string Caminho(string nome)
    {
        if (nome == null || !NomeSeguro.IsMatch(nome))
            throw new IOException("Referência de foto inválida.");
        ValidarDiretorio();
        var caminho = Path.Combine(_diretorio, nome);
        if (File.Exists(caminho) && (File.GetAttributes(caminho) & FileAttributes.ReparsePoint) != 0)
            throw new IOException("Referência de foto inválida.");
        return caminho;
    }

    public async Task<string> GravarAsync(byte[] jpeg)
    {
        ValidarDiretorio();
        Directory.CreateDirectory(_diretorio);
        var nome = Guid.NewGuid().ToString("N") + ".jpg";
        var caminho = Caminho(nome);
        // CreateNew impede sobrescrita; a referência só é publicada após fechar o arquivo.
        await using var arquivo = new FileStream(caminho, FileMode.CreateNew, FileAccess.Write,
            FileShare.None, 81920, FileOptions.Asynchronous);
        await arquivo.WriteAsync(jpeg);
        await arquivo.FlushAsync();
        return nome;
    }

    public async Task<byte[]> LerAsync(string nomeArquivo)
    {
        var caminho = Caminho(nomeArquivo);
        try { return await File.ReadAllBytesAsync(caminho); }
        catch (FileNotFoundException) { return null; }
        catch (DirectoryNotFoundException) { return null; }
    }

    public Task RemoverAsync(string nomeArquivo)
    {
        File.Delete(Caminho(nomeArquivo));
        return Task.CompletedTask;
    }
}
