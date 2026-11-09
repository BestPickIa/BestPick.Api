namespace BestPick.Aplicacao.Excecoes;

public class ContaFotoIndisponivelException : Exception
{
    public ContaFotoIndisponivelException() : base("Conta indisponível.") { }
}
