using System.ComponentModel.DataAnnotations;

namespace BestPick.Aplicacao.Models;

public class ConfirmarExclusaoRequest
{
    [Required(ErrorMessage = "Informe a senha atual para confirmar a exclusão.")]
    [StringLength(40, MinimumLength = 6, ErrorMessage = "A senha deve ter entre 6 e 40 caracteres.")]
    public string Senha { get; set; }
}
