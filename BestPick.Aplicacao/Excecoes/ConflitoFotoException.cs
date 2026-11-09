namespace BestPick.Aplicacao.Excecoes;

public class ConflitoFotoException : Exception
{
    public ConflitoFotoException() : base("A conta foi alterada durante a operação. Tente novamente.") { }
}
