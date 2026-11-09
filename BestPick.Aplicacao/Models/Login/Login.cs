namespace BestPick.Aplicacao.Models;

using System.ComponentModel.DataAnnotations;
public class Login
{
    [Required]
    [EmailAddress]
    [MaxLength(50)]
    public string Email { get; set; }

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(40, MinimumLength = 6, ErrorMessage = "A senha deve ter entre 6 e 40 caracteres.")]
    public string Senha { get; set; }
}
