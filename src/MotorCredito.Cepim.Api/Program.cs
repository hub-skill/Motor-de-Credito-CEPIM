using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Npgsql;
using OpenTelemetry.Metrics;
using MotorCredito.Cepim.Api.Bancos;
using MotorCredito.Cepim.Api.Cepim;
using MotorCredito.Cepim.Api.Configuration;
using MotorCredito.Cepim.Api.Endpoints.V1;
using MotorCredito.Cepim.Api.Errors;
using MotorCredito.Cepim.Api.Health;
using MotorCredito.Cepim.Api.Middleware;
using MotorCredito.Cepim.Api.Movimentacao;
using MotorCredito.Cepim.Api.Security;
using MotorCredito.Cepim.Api.Services;
using MotorCredito.Cepim.Api.Validation;
using Scalar.AspNetCore;

ArquivoDotEnv.Carregar(); // só desenvolvimento local: variáveis de ambiente têm prioridade

var builder = WebApplication.CreateBuilder(args);

// Configuração tipada e validada na inicialização.
builder.Services
    .AddOptions<ApiSettings>()
    .Bind(builder.Configuration.GetSection(ApiSettings.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton(sp => sp.GetRequiredService<IOptions<ApiSettings>>().Value);

var settings = builder.Configuration.GetSection(ApiSettings.SectionName).Get<ApiSettings>()!;
var bancos = builder.Configuration.GetSection(BancosSettings.SectionName).Get<BancosSettings>() ?? new BancosSettings();
builder.Services.AddSingleton(bancos);

const long TamanhoMaximoDoCorpo = 4 * 1024; // a consulta recebe só um CNPJ; o proxy deve limitar também

builder.Logging.AddJsonConsole();
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = TamanhoMaximoDoCorpo);

// Serviços e validação
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<ICepimService, CepimService>();
builder.Services.AddValidatorsFromAssemblyContaining<EntradaCepimValidator>();

// Banco (PostgreSQL ou Oracle, conforme Bancos__Provedor): leitura do CEPIM, movimentação e log de erros.
RegistrarBanco(builder.Services, bancos);

// JSON respeita os tipos do contrato: null em campo não anulável ou campo obrigatório ausente vira erro de
// leitura (422 entrada_invalida) em vez de chegar null ao endpoint.
builder.Services.ConfigureHttpJsonOptions(opcoes =>
{
    opcoes.SerializerOptions.RespectNullableAnnotations = true;
    opcoes.SerializerOptions.RespectRequiredConstructorParameters = true;
    opcoes.SerializerOptions.NumberHandling = JsonNumberHandling.Strict; // "2" não vira 2 (o padrão Web aceita)
    opcoes.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
    // Decisão do projeto: campo desconhecido no JSON é aceito e ignorado (UnmappedMemberHandling.Skip, o padrão).
});

// Autenticação por API key. Por padrão, todas as rotas exigem autenticação.
builder.Services
    .AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(
        ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization(opcoes =>
    opcoes.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

// Rate limit por IP.
builder.Services.AddRateLimiter(opcoes =>
{
    opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    opcoes.OnRejected = async (contexto, ct) =>
    {
        contexto.HttpContext.Response.Headers.RetryAfter = "60";
        await RespostaDeErro.EscreverAsync(contexto.HttpContext, StatusCodes.Status429TooManyRequests,
            "limite_excedido", "Muitas requisições. Tente mais tarde.", ct);
    };
    opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
        RateLimitPartition.GetFixedWindowLimiter(
            contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = settings.RateLimitPorMinuto,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));
});

// Erros no formato único (RFC 9457, ver RespostaDeErro). AddProblemDetails fica só porque o UseExceptionHandler exige.
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ErroDeDominioHandler>();
// JSON malformado vira BadHttpRequestException (400 requisicao_invalida) em qualquer ambiente.
builder.Services.Configure<RouteHandlerOptions>(opcoes => opcoes.ThrowOnBadRequest = true);

// Proxies: adicione os IPs confiáveis em KnownProxies. Sem isso, X-Forwarded-* é ignorado.
builder.Services.Configure<ForwardedHeadersOptions>(opcoes =>
    opcoes.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto);

// CORS e filtro de hosts
builder.Services.AddCors(opcoes => opcoes.AddDefaultPolicy(politica =>
    politica.WithOrigins(settings.AllowedOrigins)
            .WithMethods("POST")
            .WithHeaders("Content-Type", ApiKeyAuthenticationHandler.Header)));
builder.Services.AddHostFiltering(opcoes => opcoes.AllowedHosts = settings.AllowedHosts);

// Observabilidade e documentação (o /health/ready consulta o banco)
builder.Services.AddHealthChecks().Add(new HealthCheckRegistration(
    "banco", sp => new BancoHealthCheck(ct => sp.GetRequiredService<IRepositorioDeImpedimentos>().FonteAsync(ct)),
    failureStatus: null, tags: null));
builder.Services.AddOpenApi();
builder.Services.AddOpenTelemetry()
    .WithMetrics(metricas => metricas
        .AddAspNetCoreInstrumentation()
        .AddPrometheusExporter());

var app = builder.Build();

// O primeiro: numera a requisição (com chave válida), guarda a resposta e devolve o header X-Requisicao.
app.UseMiddleware<MovimentacaoMiddleware>();
app.UseExceptionHandler();
app.UseStatusCodePages(contexto => RespostaDeErro.EscreverGenericoAsync(contexto.HttpContext)); // 401, 404, 405
app.UseHostFiltering();
app.UseMiddleware<SecurityHeadersMiddleware>();
app.Use(async (contexto, proximo) =>
{
    // O Kestrel também limita; esta checagem responde 413 no formato padrão de erro
    // (e vale no TestServer dos testes, que não aplica o limite do Kestrel).
    if (contexto.Request.ContentLength > TamanhoMaximoDoCorpo)
    {
        throw new CorpoMuitoGrandeException();
    }

    await proximo(contexto);
});
app.UseForwardedHeaders();
app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<ProfundidadeDoJsonMiddleware>(); // JSON com mais de 64 níveis = 400

// Documentação apenas quando habilitada (desligada em produção por padrão).
if (settings.EnableDocs)
{
    app.MapOpenApi().AllowAnonymous();
    app.MapScalarApiReference().AllowAnonymous();
}

// Health checks anônimos e fora do rate limit (as probes do Kubernetes saem do IP do nó); métricas só na rede interna.
app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false, ResponseWriter = RespostaDeSaude.Vivo })
    .AllowAnonymous().DisableRateLimiting();
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    ResponseWriter = (contexto, relatorio) => RespostaDeSaudeComBanco.Pronto(contexto, relatorio, bancos.NomeDoBanco),
    ResultStatusCodes = { [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable }
}).AllowAnonymous().DisableRateLimiting();
app.MapPrometheusScrapingEndpoint("/metrics").AllowAnonymous().DisableRateLimiting();

app.MapCepimV1();

app.Run();

static void RegistrarBanco(IServiceCollection servicos, BancosSettings bancos)
{
    servicos.AddSingleton(new MovimentacaoOpcoes(TamanhoMaximoDoCorpo));
    servicos.AddSingleton<FilaDeMovimentacao>();
    servicos.AddHostedService<GravadorDeMovimentacao>();

    if (bancos.UsaOracle)
    {
        var oracle = bancos.Oracle;
        var conexao = ConexoesOracle.Montar(oracle.DataSource, oracle.Usuario, oracle.Senha);
        servicos.AddSingleton<IRepositorioDeImpedimentos>(new ImpedimentosOracle(conexao, oracle.TimeoutSegundos));
        servicos.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoOracle(conexao, oracle.TimeoutSegundos));
        return;
    }

    var pg = bancos.Postgres;
    var ssl = Enum.Parse<SslMode>(pg.SslMode, ignoreCase: true);
    var fonte = NpgsqlDataSource.Create(ConexoesPostgres.Montar(
        pg.Host, pg.Porta, pg.Banco, pg.Usuario, pg.Senha, somenteLeitura: true, pg.TimeoutSegundos,
        "motor-credito-cepim", ssl));
    servicos.AddSingleton(fonte);
    servicos.AddSingleton<IRepositorioDeImpedimentos>(new ImpedimentosPostgres(fonte, pg.Esquema));
    servicos.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoPostgres(fonte, pg.Esquema));
}

// Torna a classe Program acessível aos testes de integração.
public partial class Program { }
