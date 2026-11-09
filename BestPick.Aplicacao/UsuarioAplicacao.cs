using BestPick.Aplicacao.Interfaces;
using BestPick.Repositorio.Interfaces;
using BestPick.Aplicacao.Models;
using BestPick.Dominio.Entidades;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.IdentityModel.Tokens;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Identity;
using System.ComponentModel.DataAnnotations;
using BestPick.Aplicacao.Excecoes;

namespace BestPick.Aplicacao.Aplicacoes;

public class UsuarioAplicacao : IUsuarioAplicacao
{
    private readonly IUsuarioRepositorio _usuarioRepositorio;
    private readonly IConfiguration _configuration;
    private readonly IPasswordHasher<Usuario> _passwordHasher;
    private readonly IFotoUsuarioAplicacao? _fotoAplicacao;

    public UsuarioAplicacao(IUsuarioRepositorio usuarioRepositorio, IConfiguration configuration,
        IPasswordHasher<Usuario> passwordHasher, IFotoUsuarioAplicacao? fotoAplicacao = null)
    {
        _usuarioRepositorio = usuarioRepositorio;
        _configuration = configuration;
        _passwordHasher = passwordHasher;
        _fotoAplicacao = fotoAplicacao;
    }

    private static UsuarioRespostaDto ParaDto(Usuario usuario)
    {
        return new UsuarioRespostaDto
        {
            Id = usuario.Id,
            Nome = usuario.Nome,
            Email = usuario.Email
        };
    }

    public async Task<UsuarioRespostaDto> AdicionarAsync(UsuarioCriar usuario)
    {
        if (usuario == null)
            throw new ValidationException("Usuário obrigatório!");

        Validator.ValidateObject(usuario, new ValidationContext(usuario), validateAllProperties: true);

        if (await _usuarioRepositorio.ObterPorEmailAsync(usuario.Email) != null)
            throw new ValidationException("Já existe um usuário com este e-mail.");

        var usuarioDominio = new Usuario
        {
            Nome = usuario.Nome,
            Email = usuario.Email
        };
        usuarioDominio.Senha = _passwordHasher.HashPassword(usuarioDominio, usuario.Senha);

        return ParaDto(await _usuarioRepositorio.AdicionarAsync(usuarioDominio));
    }
    public async Task<UsuarioRespostaDto> ObterPorIdAsync(int id)
    {
        var obtido = await _usuarioRepositorio.ObterPorIdAsync(id);

        if (obtido == null)
            throw new Exception("Usuário não existe!");

        return ParaDto(obtido);
    }

    public async Task AtualizarAsync(UsuarioAtualizar usuario, int usuarioId)
    {
        var obtido = await _usuarioRepositorio.ObterPorIdAsync(usuarioId);

        if (obtido == null)
            throw new Exception("Usuário não existe!");

        obtido.Nome = usuario.Nome;
        obtido.Email = usuario.Email;

        await _usuarioRepositorio.AtualizarAsync(obtido);
    }

    public async Task RemoverAsync(int id)
    {
        var obtido = await _usuarioRepositorio.ObterPorIdAsync(id);

        if (obtido == null)
            throw new Exception("Usuário não existe!");

        obtido.Ativo = false;

        await _usuarioRepositorio.AtualizarAsync(obtido);
    }

    public async Task DeletarPermanentementeAsync(int usuarioId, string senha)
    {
        var usuario = await _usuarioRepositorio.ObterPorIdAsync(usuarioId);

        if (usuario == null || !usuario.Ativo)
            throw new UnauthorizedAccessException("Conta não encontrada ou inativa.");

        // Validar senha
        var verificacao = VerificarSenha(usuario, senha);
        if (!SenhaValida(verificacao))
            throw new UnauthorizedAccessException("Senha atual incorreta.");

        // Remover foto (se existir e se serviço disponível)
        if (_fotoAplicacao != null && usuario.FotoNomeArquivo != null)
        {
            try
            {
                await _fotoAplicacao.RemoverAsync(usuarioId);
            }
            catch
            {
                // Log mas não falha a exclusão da conta
            }
        }

        // Deletar permanentemente (EF Core gerencia cascata via migrations)
        await _usuarioRepositorio.DeletarPermanentementeAsync(usuarioId);
    }

    public async Task<LoginResposta> LoginAsync(Login login)
    {
        if (login == null || !Validator.TryValidateObject(login, new ValidationContext(login),
                new List<ValidationResult>(), validateAllProperties: true))
            throw new UnauthorizedAccessException("Email ou senha inválidos.");

        var usuario = await _usuarioRepositorio.ObterPorEmailAsync(login.Email);

        if (usuario == null || !usuario.Ativo)
            throw new UnauthorizedAccessException("Email ou senha inválidos.");

        var verificacao = VerificarSenha(usuario, login.Senha);
        switch (verificacao)
        {
            case PasswordVerificationResult.Success:
                break;
            case PasswordVerificationResult.SuccessRehashNeeded:
                await PersistirSenhaAsync(usuario, login.Senha);
                break;
            default:
                throw new UnauthorizedAccessException("Email ou senha inválidos.");
        }

        var claims = new[]
        {
            new Claim(
                ClaimTypes.NameIdentifier,
                usuario.Id.ToString()
            ),
            new Claim(
                ClaimTypes.Name,
                usuario.Nome
            ),
            new Claim(
                ClaimTypes.Email,
                usuario.Email
            )
        };

        var chave = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(
                _configuration["Jwt:Key"]
            )
        );

        var credenciais = new SigningCredentials(
            chave,
            SecurityAlgorithms.HmacSha256
        );

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"],
            audience: _configuration["Jwt:Audience"],
            claims: claims,
            expires: DateTime.Now.AddHours(8),
            signingCredentials: credenciais
        );

        return new LoginResposta
        {
            Token = new JwtSecurityTokenHandler()
                .WriteToken(token)
        };
    }

    public async Task AlterarSenhaAsync(int usuarioId, AlterarSenhaRequest request)
    {
        if (request == null)
            throw new ValidationException("Informe os dados para alterar a senha.");

        Validator.ValidateObject(request, new ValidationContext(request), validateAllProperties: true);

        var usuario = await _usuarioRepositorio.ObterPorIdAsync(usuarioId);
        if (usuario == null || !usuario.Ativo || !SenhaValida(VerificarSenha(usuario, request.SenhaAtual)))
            throw new UnauthorizedAccessException("Não foi possível validar a conta e a senha atual.");

        if (SenhaValida(VerificarSenha(usuario, request.SenhaNova)))
            throw new ValidationException("A nova senha deve ser diferente da senha atual.");

        // SuccessRehashNeeded também comprova a senha atual. A troca gera e
        // persiste apenas o hash novo, sem uma atualização intermediária.
        await PersistirSenhaAsync(usuario, request.SenhaNova);
    }

    private PasswordVerificationResult VerificarSenha(Usuario usuario, string senha)
    {
        if (string.IsNullOrEmpty(usuario.Senha) || string.IsNullOrEmpty(senha))
            return PasswordVerificationResult.Failed;

        try
        {
            return _passwordHasher.VerifyHashedPassword(usuario, usuario.Senha, senha);
        }
        catch (FormatException)
        {
            // Valores legados ou corrompidos não são credenciais válidas.
            return PasswordVerificationResult.Failed;
        }
    }

    private static bool SenhaValida(PasswordVerificationResult resultado) =>
        resultado == PasswordVerificationResult.Success ||
        resultado == PasswordVerificationResult.SuccessRehashNeeded;

    private async Task PersistirSenhaAsync(Usuario usuario, string senha)
    {
        var novoHash = _passwordHasher.HashPassword(usuario, senha);
        if (!await _usuarioRepositorio.AtualizarSenhaAsync(usuario, novoHash))
            throw new ConflitoSenhaException();
    }
}
