using System.ComponentModel.DataAnnotations;

namespace BestPick.Aplicacao.Models;

public class AlterarSenhaRequest
{
    [Required(ErrorMessage = "Informe a senha atual.")]
    [StringLength(40, MinimumLength = 6, ErrorMessage = "A senha atual deve ter entre 6 e 40 caracteres.")]
    public string SenhaAtual { get; set; }

    [Required(ErrorMessage = "Informe a nova senha.")]
    [StringLength(40, MinimumLength = 6, ErrorMessage = "A nova senha deve ter entre 6 e 40 caracteres.")]
    public string SenhaNova { get; set; }

    [Required(ErrorMessage = "Confirme a nova senha.")]
    [StringLength(40, ErrorMessage = "A confirmação deve ter no máximo 40 caracteres.")]
    [Compare(nameof(SenhaNova), ErrorMessage = "A confirmação de senha não corresponde à nova senha.")]
    public string ConfirmarSenha { get; set; }
}
