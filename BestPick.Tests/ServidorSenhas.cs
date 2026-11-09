using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using BestPick.Api.Controllers;
using BestPick.Aplicacao.Aplicacoes;
using BestPick.Aplicacao.Interfaces;
using BestPick.Dominio.Entidades;
using BestPick.Repositorio.Interfaces;
using BestPick.Repositorio.Repositorios;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace BestPick.Tests;

// A API real não é iniciada: nenhuma configuração, segredo ou banco de desenvolvimento
// é carregado. Cada teste usa somente Kestrel em loopback e um banco novo em memória.
internal sealed class ServidorSenhas : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly SqliteConnection _conexao;
    private readonly string _chave;
    public HttpClient Cliente { get; }
    public CapturaLogs Logs { get; }
    public FalhaPersistencia Falha { get; }
    public IServiceProvider Servicos => _app.Services;
    public const string Emissor = "bestpick-testes-isolados";
    public const string Audiencia = "bestpick-testes-cliente";

    private ServidorSenhas(WebApplication app, SqliteConnection conexao,
        string chave, CapturaLogs logs, FalhaPersistencia falha, HttpClient cliente)
    {
        _app = app;
        _conexao = conexao;
        _chave = chave;
        Logs = logs;
        Falha = falha;
        Cliente = cliente;
    }

    public static async Task<ServidorSenhas> CriarAsync(Action<IServiceCollection>? configurar = null)
    {
        var conexao = new SqliteConnection("Data Source=:memory:");
        await conexao.OpenAsync();
        var chave = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        var logs = new CapturaLogs();
        var falha = new FalhaPersistencia();
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            EnvironmentName = Environments.Production,
            ApplicationName = typeof(UsuarioController).Assembly.GetName().Name,
            Args = []
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = chave,
            ["Jwt:Issuer"] = Emissor,
            ["Jwt:Audience"] = Audiencia
        });
        builder.Logging.ClearProviders();
        builder.Logging.AddProvider(logs);
        builder.WebHost.UseKestrel(options => options.Listen(IPAddress.Loopback, 0));
        builder.Services.AddDbContext<BestPickContexto>(options =>
            options.UseSqlite(conexao).AddInterceptors(falha));
        builder.Services.AddScoped<IUsuarioRepositorio, UsuarioRepositorio>();
        builder.Services.AddScoped<IUsuarioAplicacao, UsuarioAplicacao>();
        builder.Services.AddScoped<IPasswordHasher<Usuario>, PasswordHasher<Usuario>>();
        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                ValidIssuer = Emissor,
                ValidAudience = Audiencia,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave))
            });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(UsuarioController).Assembly);
        configurar?.Invoke(builder.Services);
        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapControllers();
        using (var escopo = app.Services.CreateScope())
            await escopo.ServiceProvider.GetRequiredService<BestPickContexto>().Database.EnsureCreatedAsync();
        await app.StartAsync();
        var servidor = app.Services.GetRequiredService<IServer>();
        var endereco = servidor.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
        return new ServidorSenhas(app, conexao, chave, logs, falha,
            new HttpClient { BaseAddress = new Uri(endereco) });
    }

    public Task<HttpResponseMessage> CadastrarAsync(string senha, string? confirmar = null, string email = "novo@example.test") =>
        Cliente.PostAsJsonAsync("/Usuario", new
        {
            nome = "Pessoa de teste",
            email,
            senha,
            confirmarSenha = confirmar ?? senha
        });

    public Task<HttpResponseMessage> LoginAsync(string senha, string email = "novo@example.test") =>
        Cliente.PostAsJsonAsync("/Usuario/login", new { email, senha });

    public async Task<string> EntrarAsync(string senha, string email = "novo@example.test")
    {
        using var resposta = await LoginAsync(senha, email);
        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        using var json = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        Assert.Equal(["token"], json.RootElement.EnumerateObject().Select(x => x.Name));
        return json.RootElement.GetProperty("token").GetString()!;
    }

    public Task<HttpResponseMessage> AlterarAsync(string? token, string atual, string nova, string? confirmar = null,
        int? idForjado = null)
    {
        var pedido = new HttpRequestMessage(HttpMethod.Put, "/Usuario/senha")
        {
            Content = JsonContent.Create(new
            {
                senhaAtual = atual,
                senhaNova = nova,
                confirmarSenha = confirmar ?? nova,
                usuarioId = idForjado,
                id = idForjado
            })
        };
        if (token != null)
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return Cliente.SendAsync(pedido);
    }

    public async Task<Usuario> ObterAsync(string email = "novo@example.test")
    {
        using var escopo = Servicos.CreateScope();
        return await escopo.ServiceProvider.GetRequiredService<BestPickContexto>()
            .Usuarios.AsNoTracking().SingleAsync(x => x.Email == email);
    }

    public async Task<int> SemearAsync(string senha, bool ativo = true,
        bool hashAntigo = false, bool textoPuro = false, string email = "novo@example.test")
    {
        var usuario = new Usuario { Nome = "Pessoa de teste", Email = email, Ativo = ativo };
        var hasher = hashAntigo
            ? new PasswordHasher<Usuario>(Options.Create(new PasswordHasherOptions { IterationCount = 1000 }))
            : new PasswordHasher<Usuario>();
        usuario.Senha = textoPuro ? senha : hasher.HashPassword(usuario, senha);
        using var escopo = Servicos.CreateScope();
        var contexto = escopo.ServiceProvider.GetRequiredService<BestPickContexto>();
        contexto.Usuarios.Add(usuario);
        await contexto.SaveChangesAsync();
        return usuario.Id;
    }

    public string TokenPara(string? id, bool expirado = false, bool assinaturaInvalida = false)
    {
        var chave = assinaturaInvalida
            ? Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)) : _chave;
        var claims = id == null ? Array.Empty<Claim>() : [new Claim(ClaimTypes.NameIdentifier, id)];
        var jwt = new JwtSecurityToken(Emissor, Audiencia, claims,
            expires: DateTime.UtcNow.AddHours(expirado ? -1 : 1),
            signingCredentials: new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(chave)), SecurityAlgorithms.HmacSha256));
        return new JwtSecurityTokenHandler().WriteToken(jwt);
    }

    public async ValueTask DisposeAsync()
    {
        Cliente.Dispose();
        await _app.StopAsync();
        await _app.DisposeAsync();
        await _conexao.DisposeAsync();
    }
}

internal sealed class FalhaPersistencia : SaveChangesInterceptor
{
    public bool Ativa { get; set; }
    public string Detalhe { get; } = "detalhe-interno-" + Guid.NewGuid().ToString("N");

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (Ativa)
            throw new DbUpdateException(Detalhe);
        return ValueTask.FromResult(result);
    }
}

internal sealed class CapturaLogs : ILoggerProvider
{
    public ConcurrentQueue<string> Mensagens { get; } = new();
    public ILogger CreateLogger(string categoryName) => new CapturaLogger(Mensagens);
    public void Dispose() { }

    private sealed class CapturaLogger(ConcurrentQueue<string> mensagens) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter) => mensagens.Enqueue(formatter(state, exception));
    }
}
