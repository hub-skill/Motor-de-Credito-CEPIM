using Microsoft.Extensions.Time.Testing;
using MotorCredito.Cepim.Api.Cepim;
using MotorCredito.Cepim.Api.Configuration;
using MotorCredito.Cepim.Api.Contracts;
using MotorCredito.Cepim.Api.Documentos;
using MotorCredito.Cepim.Api.Services;
using MotorCredito.Cepim.Api.Validation;
using Xunit;

namespace MotorCredito.Cepim.Api.UnitTests;

public sealed class MotorDeCepimTests
{
    private static Impedimento Impedimento(string convenio = "633984") =>
        new("ENTIDADE X", convenio, "Ministério Y", "INSTAURACAO DE TOMADA DE CONTAS ESPECIAL");

    [Fact]
    public void Sem_registro_libera_com_motivo_OK()
    {
        var r = MotorDeCepim.Consultar([]);

        Assert.Equal(Decisao.Liberar, r.Decisao);
        var motivo = Assert.Single(r.Motivos);
        Assert.Equal(("OK", "Nenhum impedimento no CEPIM."), (motivo.Codigo, motivo.Texto));
    }

    [Fact]
    public void Um_impedimento_manda_revisar_C01_no_singular()
    {
        var r = MotorDeCepim.Consultar([Impedimento()]);

        Assert.Equal(Decisao.Revisar, r.Decisao);
        Assert.Equal("C01", r.Motivos.Single().Codigo);
        Assert.Equal("Entidade impedida de receber repasses federais (CEPIM): 1 impedimento.", r.Motivos.Single().Texto);
    }

    [Fact]
    public void Varios_impedimentos_geram_um_motivo_com_a_contagem()
    {
        var r = MotorDeCepim.Consultar([Impedimento("1"), Impedimento("2"), Impedimento("3")]);

        Assert.Equal(Decisao.Revisar, r.Decisao);
        Assert.Equal("Entidade impedida de receber repasses federais (CEPIM): 3 impedimentos.", r.Motivos.Single().Texto);
    }
}

public sealed class CepimServiceTests
{
    private static readonly DocumentoNormalizado Cnpj = Normalizar("11.222.333/0001-81");

    private static DocumentoNormalizado Normalizar(string texto)
    {
        Assert.True(ValidadorDeDocumento.TentarNormalizar(texto, out var documento));
        return documento;
    }

    private sealed class Repositorio : IRepositorioDeImpedimentos
    {
        public int ConsultasDeFonte { get; private set; }

        public List<Impedimento> Impedimentos { get; } = [];

        public Task<IReadOnlyList<Impedimento>> ImpedimentosDoCnpjAsync(string cnpj, CancellationToken cancellationToken)
        {
            Assert.Equal("11222333000181", cnpj); // só dígitos e letras vão ao banco
            return Task.FromResult<IReadOnlyList<Impedimento>>(Impedimentos);
        }

        public Task<FonteDosDados> FonteAsync(CancellationToken cancellationToken)
        {
            ConsultasDeFonte++;
            return Task.FromResult(new FonteDosDados("postgres", ["20261111_CEPIM.csv"], null));
        }
    }

    [Fact]
    public async Task Sem_registro_a_resposta_libera_e_formata_o_cnpj()
    {
        var servico = new CepimService(new Repositorio(), new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 15, 0, 0, TimeSpan.Zero)));

        var saida = await servico.ConsultarAsync(Cnpj, CancellationToken.None);

        Assert.Equal("11.222.333/0001-81", saida.Documento);
        Assert.Equal("CNPJ", saida.Tipo);
        Assert.Equal("liberar", saida.Resultado);
        Assert.Null(saida.Entidade);
        Assert.Empty(saida.Impedimentos);
        Assert.Equal(new DateOnly(2026, 11, 12), saida.ConsultadoEm);
        Assert.Equal(["20261111_CEPIM.csv"], saida.Fonte.Arquivos);
    }

    [Fact]
    public async Task Com_registro_a_resposta_manda_revisar_e_lista_os_impedimentos()
    {
        var repositorio = new Repositorio();
        repositorio.Impedimentos.AddRange([new Impedimento("ENTIDADE X", "10", "Órgão A", "MOTIVO A"), new Impedimento("ENTIDADE X", "20", "Órgão B", null)]);

        var saida = await new CepimService(repositorio, TimeProvider.System).ConsultarAsync(Cnpj, CancellationToken.None);

        Assert.Equal("revisar", saida.Resultado);
        Assert.Equal("ENTIDADE X", saida.Entidade);
        Assert.Equal(["10", "20"], saida.Impedimentos.Select(i => i.NumeroConvenio));
        Assert.Null(saida.Impedimentos[1].MotivoDoImpedimento);
    }

    [Fact]
    public async Task Data_da_consulta_e_a_de_brasilia_nao_a_do_utc()
    {
        // 01:00 UTC de 13/11 ainda é 22:00 de 12/11 em Brasília.
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 11, 13, 1, 0, 0, TimeSpan.Zero));

        var saida = await new CepimService(new Repositorio(), relogio).ConsultarAsync(Cnpj, CancellationToken.None);

        Assert.Equal(new DateOnly(2026, 11, 12), saida.ConsultadoEm);
    }

    [Fact]
    public async Task Fonte_fica_em_cache_por_5_minutos()
    {
        var repositorio = new Repositorio();
        var relogio = new FakeTimeProvider(new DateTimeOffset(2026, 11, 12, 12, 0, 0, TimeSpan.Zero));
        var servico = new CepimService(repositorio, relogio);

        await servico.ConsultarAsync(Cnpj, CancellationToken.None);
        await servico.ConsultarAsync(Cnpj, CancellationToken.None);
        Assert.Equal(1, repositorio.ConsultasDeFonte);

        relogio.Advance(TimeSpan.FromMinutes(5) + TimeSpan.FromSeconds(1));
        await servico.ConsultarAsync(Cnpj, CancellationToken.None);
        Assert.Equal(2, repositorio.ConsultasDeFonte);
    }
}

public sealed class EntradaCepimValidatorTests
{
    private static readonly EntradaCepimValidator Validador = new();

    [Theory]
    [InlineData("11222333000181")]
    [InlineData("11.222.333/0001-81")]
    [InlineData("12.ABC.345/01DE-35")] // CNPJ alfanumérico da Receita
    [InlineData("12.abc.345/01de-35")]
    public void Cnpj_valido_passa(string documento) =>
        Assert.True(Validador.Validate(new EntradaCepim(documento)).IsValid);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123")]
    [InlineData("52998224725")] // CPF válido: o CEPIM só tem CNPJ
    [InlineData("529.982.247-25")]
    [InlineData("11222333000182")] // dígito verificador errado
    [InlineData("00000000000000")] // sequência repetida
    [InlineData("11.222.333/0001-81 e mais texto")]
    [InlineData("123456789012345678901")] // acima de 20 caracteres
    public void Documento_que_nao_e_cnpj_valido_nao_passa(string? documento) =>
        Assert.False(Validador.Validate(new EntradaCepim(documento)).IsValid);
}

public sealed class ArquivoDotEnvTests
{
    [Fact]
    public void Le_pares_ignora_comentarios_linhas_vazias_e_tira_aspas()
    {
        var pares = ArquivoDotEnv.Ler(
        [
            "# comentário",
            "",
            "Api__ApiKey=abc123",
            "  Bancos__Provedor = oracle  ",
            "Texto=\"com espaço\"",
            "SemValor=",
            "linha sem igual",
            "Senha=a=b=c",
        ]).ToList();

        Assert.Equal(
        [
            ("Api__ApiKey", "abc123"),
            ("Bancos__Provedor", "oracle"),
            ("Texto", "com espaço"),
            ("SemValor", ""),
            ("Senha", "a=b=c"),
        ], pares);
    }
}
