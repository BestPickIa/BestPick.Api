using BestPick.Aplicacao.Excecoes;
using BestPick.Aplicacao.Interfaces;
using BestPick.Dominio.Entidades;
using BestPick.Repositorio.Interfaces;
using BestPick.Servicos.Fotos;
using Microsoft.Extensions.Logging;

namespace BestPick.Aplicacao.Aplicacoes;

public class FotoUsuarioAplicacao : IFotoUsuarioAplicacao
{
    private readonly IFotoUsuarioRepositorio _repositorio;
    private readonly IProcessadorFoto _processador;
    private readonly IArmazenamentoFotos _arquivos;
    private readonly ILogger<FotoUsuarioAplicacao> _logger;

    public FotoUsuarioAplicacao(IFotoUsuarioRepositorio repositorio, IProcessadorFoto processador,
        IArmazenamentoFotos arquivos, ILogger<FotoUsuarioAplicacao> logger)
    {
        _repositorio = repositorio;
        _processador = processador;
        _arquivos = arquivos;
        _logger = logger;
    }

    private async Task<Usuario> ExigirContaAsync(int id) =>
        (id > 0 ? await _repositorio.ObterAtivoAsync(id) : null) ?? throw new ContaFotoIndisponivelException();

    public async Task EnviarAsync(int usuarioId, Stream arquivo, string nomeOriginal, string tipoConteudo,
        CancellationToken cancellationToken = default)
    {
        var usuario = await ExigirContaAsync(usuarioId);
        var anterior = usuario.FotoNomeArquivo;
        var jpeg = await _processador.ProcessarAsync(arquivo, nomeOriginal, tipoConteudo, cancellationToken);
        var novo = await _arquivos.GravarAsync(jpeg);
        try
        {
            if (!await _repositorio.AtualizarAsync(usuario, novo, DateTime.UtcNow))
                throw new ConflitoFotoException();
        }
        catch
        {
            // Até um erro de commit ambíguo é seguro: só limpar se o banco confirmar
            // ausência de referências. Falha na consulta preserva o arquivo.
            await LimparSemReferenciaAsync(novo);
            throw;
        }
        await LimparSemReferenciaAsync(anterior);
    }

    public async Task<byte[]> ObterAsync(int solicitanteId, int usuarioId)
    {
        await ExigirContaAsync(solicitanteId);
        var usuario = await _repositorio.ObterAtivoAsync(usuarioId);
        return usuario?.FotoNomeArquivo == null ? null : await _arquivos.LerAsync(usuario.FotoNomeArquivo);
    }

    public async Task RemoverAsync(int usuarioId)
    {
        var usuario = await ExigirContaAsync(usuarioId);
        var anterior = usuario.FotoNomeArquivo;
        if (anterior == null) return;
        if (!await _repositorio.AtualizarAsync(usuario, null, null)) throw new ConflitoFotoException();
        await LimparSemReferenciaAsync(anterior);
    }

    private async Task LimparSemReferenciaAsync(string nome)
    {
        if (nome == null) return;
        try
        {
            if (!await _repositorio.ReferenciadaAsync(nome)) await _arquivos.RemoverAsync(nome);
        }
        catch
        {
            // Sem exceção/caminho no log. A operação já confirmada permanece válida.
            _logger.LogWarning("Limpeza de foto pendente; arquivo preservado para reconciliação autorizada.");
        }
    }
}
