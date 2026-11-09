namespace BestPick.Aplicacao.Excecoes;

public class ConflitoSenhaException : Exception
{
    public ConflitoSenhaException()
        : base("A conta foi alterada durante a operação. Tente novamente.")
    {
    }
}
