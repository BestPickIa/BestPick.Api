namespace BestPick.Aplicacao.Models;

using System.ComponentModel.DataAnnotations;
public class UsuarioAtualizar
{
    public string Nome { get; set; }

    public string Email { get; set; }
}

public class UsuarioCriar
{
    [Required]
    [MinLength(2)]
    [MaxLength(50)]
    public string Nome { get; set; }

    [Required]
    [EmailAddress]
    [MaxLength(50)]
    public string Email { get; set; }

    [Required(ErrorMessage = "Informe a senha.")]
    [StringLength(40, MinimumLength = 6, ErrorMessage = "A senha deve ter entre 6 e 40 caracteres.")]
    public string Senha { get; set; }

    [Required(ErrorMessage = "Confirme a senha.")]
    [StringLength(40, ErrorMessage = "A confirmação deve ter no máximo 40 caracteres.")]
    [Compare(nameof(Senha), ErrorMessage = "A confirmação de senha não corresponde à senha.")]
    public string ConfirmarSenha { get; set; }
}
