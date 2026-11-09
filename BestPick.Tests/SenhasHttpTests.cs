using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using BestPick.Dominio.Entidades;
using Microsoft.AspNetCore.Identity;
using Xunit;

namespace BestPick.Tests;

public class SenhasHttpTests
{
    private const string SenhaInicial = "somenteletras";
    private const string SenhaNova = "outrasletras";

    [Theory]
    [InlineData(5, HttpStatusCode.BadRequest)]
    [InlineData(6, HttpStatusCode.OK)]
    [InlineData(40, HttpStatusCode.OK)]
    [InlineData(41, HttpStatusCode.BadRequest)]
    public async Task Cadastro_respeita_limites_sem_exigir_complexidade(int tamanho, HttpStatusCode esperado)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var senha = new string('a', tamanho);
        using var resposta = await servidor.CadastrarAsync(senha);
        Assert.Equal(esperado, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(senha, corpo);
        if (esperado == HttpStatusCode.OK)
        {
            var usuario = await servidor.ObterAsync();
            AssertHash(usuario, senha);
            using var json = JsonDocument.Parse(corpo);
            Assert.Equal(["email", "id", "nome"], json.RootElement.EnumerateObject().Select(x => x.Name).Order());
        }
    }

    [Theory]
    [InlineData("divergente")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Cadastro_exige_confirmacao_coincidente(string? confirmar)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var resposta = await servidor.Cliente.PostAsJsonAsync("/Usuario", new
        {
            nome = "Pessoa de teste", email = "novo@example.test", senha = SenhaInicial, confirmarSenha = confirmar
        });
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.DoesNotContain(SenhaInicial, await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Cadastro_hash_tem_salt_individual_e_nenhuma_credencial_em_respostas_ou_logs()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var primeira = await servidor.CadastrarAsync(SenhaInicial);
        using var segunda = await servidor.CadastrarAsync(SenhaInicial, email: "segunda@example.test");
        Assert.Equal(HttpStatusCode.OK, primeira.StatusCode);
        Assert.Equal(HttpStatusCode.OK, segunda.StatusCode);
        var usuario = await servidor.ObterAsync();
        var outro = await servidor.ObterAsync("segunda@example.test");
        AssertHash(usuario, SenhaInicial);
        AssertHash(outro, SenhaInicial);
        Assert.NotEqual(usuario.Senha, outro.Senha);
        foreach (var resposta in new[] { primeira, segunda })
        {
            var corpo = await resposta.Content.ReadAsStringAsync();
            Assert.DoesNotContain(SenhaInicial, corpo);
            Assert.DoesNotContain(usuario.Senha, corpo);
            Assert.DoesNotContain(outro.Senha, corpo);
            Assert.DoesNotContain("senha", corpo, StringComparison.OrdinalIgnoreCase);
        }
        AssertLogsSemCredenciais(servidor, SenhaInicial, usuario.Senha, outro.Senha);
    }

    [Fact]
    public async Task Login_novo_preserva_token_claims_e_expiracao_utilizados_pelo_frontend()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var cadastro = await servidor.CadastrarAsync(SenhaInicial);
        Assert.Equal(HttpStatusCode.OK, cadastro.StatusCode);
        var usuario = await servidor.ObterAsync();
        var token = await servidor.EntrarAsync(SenhaInicial);
        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Equal(usuario.Id.ToString(), jwt.Claims.Single(x => x.Type == ClaimTypes.NameIdentifier).Value);
        Assert.Equal(usuario.Nome, jwt.Claims.Single(x => x.Type == ClaimTypes.Name).Value);
        Assert.Equal(usuario.Email, jwt.Claims.Single(x => x.Type == ClaimTypes.Email).Value);
        Assert.Equal(ServidorSenhas.Emissor, jwt.Issuer);
        Assert.Contains(ServidorSenhas.Audiencia, jwt.Audiences);
        Assert.Equal("HS256", jwt.Header.Alg);
        Assert.InRange(jwt.ValidTo, DateTime.UtcNow.AddHours(8).AddMinutes(-1), DateTime.UtcNow.AddHours(8).AddMinutes(1));
        Assert.DoesNotContain(SenhaInicial, jwt.Payload.SerializeToJson());
        Assert.DoesNotContain(usuario.Senha, jwt.Payload.SerializeToJson());
        AssertLogsSemCredenciais(servidor, SenhaInicial, usuario.Senha, token);
    }

    [Theory]
    [InlineData("incorreta")]
    [InlineData("inexistente")]
    [InlineData("inativa")]
    [InlineData("legada")]
    [InlineData("malformada")]
    public async Task Login_rejeita_credenciais_invalidas_com_mesma_mensagem_sem_token(string caso)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        if (caso != "inexistente")
            await servidor.SemearAsync(caso == "malformada" ? "hash!!!invalido" : SenhaInicial,
                ativo: caso != "inativa", textoPuro: caso is "legada" or "malformada");
        var antes = caso == "inexistente" ? null : await servidor.ObterAsync();
        using var resposta = await servidor.LoginAsync(caso == "incorreta" ? "erradamasvalida" : SenhaInicial);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.Equal("Email ou senha inválidos.", corpo.Trim('"'));
        Assert.DoesNotContain("token", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(SenhaInicial, corpo);
        if (antes != null)
            Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
        AssertLogsSemCredenciais(servidor, SenhaInicial);
    }

    [Fact]
    public async Task Alteracao_rejeita_senha_atual_incorreta_sem_modificar_hash()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var cadastro = await servidor.CadastrarAsync(SenhaInicial);
        var token = await servidor.EntrarAsync(SenhaInicial);
        var antes = await servidor.ObterAsync();
        using var resposta = await servidor.AlterarAsync(token, "erradamasvalida", SenhaNova);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
        Assert.DoesNotContain(SenhaNova, await resposta.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Alteracao_persiste_novo_hash_revoga_senha_antiga_e_aceita_nova()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var cadastro = await servidor.CadastrarAsync(SenhaInicial);
        var token = await servidor.EntrarAsync(SenhaInicial);
        var antes = await servidor.ObterAsync();
        using var resposta = await servidor.AlterarAsync(token, SenhaInicial, SenhaNova);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var depois = await servidor.ObterAsync();
        AssertHash(depois, SenhaNova);
        Assert.NotEqual(antes.Senha, depois.Senha);
        Assert.Equal(antes.Id, depois.Id);
        Assert.Equal(antes.Nome, depois.Nome);
        Assert.Equal(antes.Email, depois.Email);
        Assert.Equal(antes.Ativo, depois.Ativo);
        using var antigo = await servidor.LoginAsync(SenhaInicial);
        Assert.Equal(HttpStatusCode.BadRequest, antigo.StatusCode);
        await servidor.EntrarAsync(SenhaNova);
        var corpo = await resposta.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(corpo);
        Assert.Equal(["mensagem"], json.RootElement.EnumerateObject().Select(x => x.Name));
        Assert.DoesNotContain(SenhaInicial, corpo);
        Assert.DoesNotContain(SenhaNova, corpo);
        Assert.DoesNotContain(depois.Senha, corpo);
        AssertLogsSemCredenciais(servidor, SenhaInicial, SenhaNova, antes.Senha, depois.Senha, token);
    }

    [Fact]
    public async Task Alteracao_ignora_id_do_corpo_e_so_altera_usuario_do_JWT_validado()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var cadastro = await servidor.CadastrarAsync(SenhaInicial);
        using var segundoCadastro = await servidor.CadastrarAsync(SenhaInicial, email: "segunda@example.test");
        var outroAntes = await servidor.ObterAsync("segunda@example.test");
        var token = await servidor.EntrarAsync(SenhaInicial);
        using var resposta = await servidor.AlterarAsync(token, SenhaInicial, SenhaNova, idForjado: outroAntes.Id);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        AssertHash(await servidor.ObterAsync(), SenhaNova);
        Assert.Equal(outroAntes.Senha, (await servidor.ObterAsync("segunda@example.test")).Senha);
        await servidor.EntrarAsync(SenhaInicial, "segunda@example.test");
        using var loginOutra = await servidor.LoginAsync(SenhaNova, "segunda@example.test");
        Assert.Equal(HttpStatusCode.BadRequest, loginOutra.StatusCode);
    }

    [Theory]
    [InlineData(5, HttpStatusCode.BadRequest)]
    [InlineData(6, HttpStatusCode.OK)]
    [InlineData(40, HttpStatusCode.OK)]
    [InlineData(41, HttpStatusCode.BadRequest)]
    public async Task Alteracao_aplica_limites_no_servidor(int tamanho, HttpStatusCode esperado)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync(SenhaInicial);
        var antes = await servidor.ObterAsync();
        var nova = new string('b', tamanho);
        using var resposta = await servidor.AlterarAsync(servidor.TokenPara(id.ToString()), SenhaInicial, nova);
        Assert.Equal(esperado, resposta.StatusCode);
        if (esperado == HttpStatusCode.OK)
            AssertHash(await servidor.ObterAsync(), nova);
        else
            Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
    }

    [Theory]
    [InlineData("reuso")]
    [InlineData("divergente")]
    [InlineData("confirmacao_ausente")]
    [InlineData("atual_ausente")]
    public async Task Alteracao_rejeita_reuso_e_campos_invalidos_sem_mutacao(string caso)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync(SenhaInicial);
        var antes = await servidor.ObterAsync();
        var nova = caso == "reuso" ? SenhaInicial : SenhaNova;
        var confirmar = caso == "divergente" ? "divergente" : caso == "confirmacao_ausente" ? "" : nova;
        using var resposta = await servidor.AlterarAsync(servidor.TokenPara(id.ToString()),
            caso == "atual_ausente" ? "" : SenhaInicial, nova, confirmar);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
    }

    [Theory]
    [InlineData("ausente")]
    [InlineData("malformado")]
    [InlineData("expirado")]
    [InlineData("assinatura")]
    [InlineData("sem_id")]
    [InlineData("id_invalido")]
    [InlineData("id_zero")]
    [InlineData("id_negativo")]
    public async Task Alteracao_exige_JWT_valido_com_identificador_positivo(string caso)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = await servidor.SemearAsync(SenhaInicial);
        var antes = await servidor.ObterAsync();
        var token = caso switch
        {
            "ausente" => null,
            "malformado" => "token-invalido",
            "expirado" => servidor.TokenPara(id.ToString(), expirado: true),
            "assinatura" => servidor.TokenPara(id.ToString(), assinaturaInvalida: true),
            "sem_id" => servidor.TokenPara(null),
            "id_invalido" => servidor.TokenPara("abc"),
            "id_zero" => servidor.TokenPara("0"),
            _ => servidor.TokenPara("-1")
        };
        using var resposta = await servidor.AlterarAsync(token, SenhaInicial, SenhaNova);
        Assert.Equal(HttpStatusCode.Unauthorized, resposta.StatusCode);
        Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Alteracao_rejeita_conta_inativa_ou_inexistente(bool inexistente)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        var id = inexistente ? 100 : await servidor.SemearAsync(SenhaInicial, ativo: false);
        using var resposta = await servidor.AlterarAsync(servidor.TokenPara(id.ToString()), SenhaInicial, SenhaNova);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
    }

    [Fact]
    public async Task Login_com_hash_valido_antigo_faz_rehash_antes_de_emitir_token()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        await servidor.SemearAsync(SenhaInicial, hashAntigo: true);
        var antes = await servidor.ObterAsync();
        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded,
            new PasswordHasher<Usuario>().VerifyHashedPassword(antes, antes.Senha, SenhaInicial));
        await servidor.EntrarAsync(SenhaInicial);
        var depois = await servidor.ObterAsync();
        Assert.NotEqual(antes.Senha, depois.Senha);
        AssertHash(depois, SenhaInicial);
    }

    [Theory]
    [InlineData("cadastro")]
    [InlineData("alteracao")]
    [InlineData("rehash")]
    public async Task Falha_de_persistencia_retorna_erro_generico_sem_token_nem_mutacao(string operacao)
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        Usuario? antes = null;
        if (operacao != "cadastro")
        {
            await servidor.SemearAsync(SenhaInicial, hashAntigo: operacao == "rehash");
            antes = await servidor.ObterAsync();
        }
        servidor.Falha.Ativa = true;
        using var resposta = operacao switch
        {
            "cadastro" => await servidor.CadastrarAsync(SenhaInicial),
            "alteracao" => await servidor.AlterarAsync(servidor.TokenPara(antes!.Id.ToString()), SenhaInicial, SenhaNova),
            _ => await servidor.LoginAsync(SenhaInicial)
        };
        Assert.Equal(HttpStatusCode.InternalServerError, resposta.StatusCode);
        var corpo = await resposta.Content.ReadAsStringAsync();
        Assert.DoesNotContain(servidor.Falha.Detalhe, corpo);
        Assert.DoesNotContain("token", corpo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Exception", corpo);
        Assert.DoesNotContain(SenhaInicial, corpo);
        Assert.DoesNotContain(SenhaNova, corpo);
        if (antes != null)
            Assert.Equal(antes.Senha, (await servidor.ObterAsync()).Senha);
    }

    [Fact]
    public async Task Erro_de_binding_nao_reflete_valor_enviado_em_campo_sensivel()
    {
        await using var servidor = await ServidorSenhas.CriarAsync();
        using var resposta = await servidor.Cliente.PostAsJsonAsync("/Usuario", new
        {
            nome = "Pessoa de teste", email = "novo@example.test", senha = new { segredo = SenhaInicial }, confirmarSenha = SenhaInicial
        });
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        Assert.DoesNotContain(SenhaInicial, await resposta.Content.ReadAsStringAsync());
        AssertLogsSemCredenciais(servidor, SenhaInicial);
    }

    private static void AssertHash(Usuario usuario, string senha)
    {
        Assert.NotEqual(senha, usuario.Senha);
        Assert.Equal(PasswordVerificationResult.Success,
            new PasswordHasher<Usuario>().VerifyHashedPassword(usuario, usuario.Senha, senha));
    }

    private static void AssertLogsSemCredenciais(ServidorSenhas servidor, params string[] credenciais)
    {
        var logs = string.Join(Environment.NewLine, servidor.Logs.Mensagens);
        foreach (var credencial in credenciais)
            Assert.DoesNotContain(credencial, logs);
    }
}
