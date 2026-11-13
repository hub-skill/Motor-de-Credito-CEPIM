using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using MotorCredito.Cepim.Api.Cepim;
using MotorCredito.Cepim.Api.Contracts;
using MotorCredito.Cepim.Api.Documentos;
using MotorCredito.Cepim.Api.Movimentacao;
using MotorCredito.Cepim.Api.Services;

namespace MotorCredito.Cepim.Api.IntegrationTests;

/// <summary>Desliga o .env antes de qualquer teste: os testes nunca dependem da máquina nem tocam em banco real.</summary>
internal static class SemDotEnv
{
#pragma warning disable CA2255 // inicializador de módulo: precisa rodar antes da primeira fábrica
    [System.Runtime.CompilerServices.ModuleInitializer]
    internal static void Desligar() => Environment.SetEnvironmentVariable("MOTORCREDITO_SEM_DOTENV", "1");
#pragma warning restore CA2255
}

/// <summary>Configuração comum das fábricas: Production, chave de teste e TODOS os repositórios falsos (nenhum banco é tocado).</summary>
internal static class ConfiguracaoDeTeste
{
    public static void Aplicar(IWebHostBuilder builder, ICepimService? servico = null, IRepositorioDeImpedimentos? impedimentos = null)
    {
        // Production: o WebApplicationFactory usa Development por padrão, e alguns comportamentos mudam com o ambiente.
        builder.UseEnvironment("Production");
        builder.UseSetting("Api:ApiKey", ApiFactory.ChaveDeTeste);
        builder.ConfigureTestServices(servicos =>
        {
            servicos.RemoveAll<IRepositorioDeImpedimentos>();
            servicos.AddSingleton(impedimentos ?? new ImpedimentosFalsos());
            servicos.RemoveAll<IRepositorioDeMovimentacao>();
            servicos.AddSingleton<IRepositorioDeMovimentacao>(new MovimentacaoFalsa());
            if (servico is not null)
            {
                servicos.RemoveAll<ICepimService>();
                servicos.AddSingleton(servico);
            }
        });
    }
}

public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string ChaveDeTeste = "chave-de-teste-com-pelo-menos-32-caracteres";

    protected override void ConfigureWebHost(IWebHostBuilder builder) => ConfiguracaoDeTeste.Aplicar(builder);
}

/// <summary>
/// Sobe a API com configurações próprias (rate limit baixo, docs ligadas, timeout curto) e, se informado, troca o
/// serviço ou o repositório por falsos (para provocar timeout, erro inesperado ou falha de banco).
/// </summary>
public sealed class ApiFactoryConfiguravel(
    IReadOnlyDictionary<string, string> configuracao, ICepimService? servico = null, IRepositorioDeImpedimentos? impedimentos = null)
    : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ConfiguracaoDeTeste.Aplicar(builder, servico, impedimentos);
        foreach (var (chave, valor) in configuracao)
        {
            builder.UseSetting(chave, valor);
        }
    }
}

/// <summary>Serviço falso: atrasa (para o timeout) ou lança uma exceção (para o erro 500).</summary>
public sealed class ServicoFalso(TimeSpan atraso = default, Exception? lancar = null) : ICepimService
{
    public async Task<SaidaCepim> ConsultarAsync(DocumentoNormalizado cnpj, CancellationToken cancellationToken)
    {
        await Task.Delay(atraso, cancellationToken);
        if (lancar is not null)
        {
            throw lancar;
        }

        return new SaidaCepim(
            ValidadorDeDocumento.Mascarar(cnpj), "CNPJ", "liberar", [new MotivoSaida("OK", "ok")], null, [],
            new DateOnly(2026, 11, 12), new FonteSaida("postgres", [], null));
    }
}

/// <summary>Impedimentos falsos, na memória: o que cada teste precisa, sem banco.</summary>
public sealed class ImpedimentosFalsos : IRepositorioDeImpedimentos
{
    private readonly Dictionary<string, List<Impedimento>> _impedimentos = [];

    /// <summary>Exceção a lançar em toda leitura (simula o banco fora do ar ou lento).</summary>
    public Exception? Falha { get; set; }

    public ImpedimentosFalsos Com(string cnpj, Impedimento impedimento)
    {
        _impedimentos.TryAdd(cnpj, []);
        _impedimentos[cnpj].Add(impedimento);
        return this;
    }

    public Task<IReadOnlyList<Impedimento>> ImpedimentosDoCnpjAsync(string cnpj, CancellationToken cancellationToken) =>
        Falha is not null
            ? Task.FromException<IReadOnlyList<Impedimento>>(Falha)
            : Task.FromResult<IReadOnlyList<Impedimento>>(_impedimentos.GetValueOrDefault(cnpj) ?? []);

    public Task<FonteDosDados> FonteAsync(CancellationToken cancellationToken) =>
        Falha is not null
            ? Task.FromException<FonteDosDados>(Falha)
            : Task.FromResult(new FonteDosDados("postgres", ["20261111_CEPIM.csv"], new DateTimeOffset(2026, 11, 12, 7, 0, 0, TimeSpan.FromHours(-3))));
}
