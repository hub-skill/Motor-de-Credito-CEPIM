using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using MotorCredito.Cepim.Api.Bancos;
using MotorCredito.Cepim.Api.Cepim;
using Xunit;
using static MotorCredito.Cepim.Api.IntegrationTests.ContratoDeErro;

namespace MotorCredito.Cepim.Api.IntegrationTests;

/// <summary>A rota de negócio: POST /api/v1/cepim com impedimentos falsos (sem banco).</summary>
public sealed class CepimApiTests
{
    private const string Url = "/api/v1/cepim";
    private const string Cnpj = "11222333000181";

    private static HttpClient Cliente(ApiFactoryConfiguravel fabrica)
    {
        var cliente = fabrica.CreateClient();
        cliente.DefaultRequestHeaders.Add("X-API-Key", ApiFactory.ChaveDeTeste);
        return cliente;
    }

    private static ApiFactoryConfiguravel Fabrica(ImpedimentosFalsos impedimentos) => new(new Dictionary<string, string>(), impedimentos: impedimentos);

    private static async Task<JsonElement> Corpo(HttpResponseMessage resposta)
    {
        using var documento = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync());
        return documento.RootElement.Clone();
    }

    private static async Task<HttpResponseMessage> EnviarAsync(HttpClient cliente, string json)
    {
        using var conteudo = new StringContent(json, Encoding.UTF8, "application/json");
        return await cliente.PostAsync(new Uri(Url, UriKind.Relative), conteudo);
    }

    [Fact]
    public async Task Sem_registro_libera_e_a_resposta_tem_todos_os_campos_em_snake_case()
    {
        using var fabrica = Fabrica(new ImpedimentosFalsos());

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = Cnpj });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        var corpo = await Corpo(resposta);
        Assert.Equal("11.222.333/0001-81", corpo.GetProperty("documento").GetString());
        Assert.Equal("CNPJ", corpo.GetProperty("tipo").GetString());
        Assert.Equal("liberar", corpo.GetProperty("resultado").GetString());
        Assert.Equal("OK", corpo.GetProperty("motivos")[0].GetProperty("codigo").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("entidade").ValueKind);
        Assert.Empty(corpo.GetProperty("impedimentos").EnumerateArray());
        Assert.Matches(@"^\d{4}-\d{2}-\d{2}$", corpo.GetProperty("consultado_em").GetString());
        Assert.Equal("postgres", corpo.GetProperty("fonte").GetProperty("banco").GetString());
        Assert.Equal(["20261111_CEPIM.csv"], corpo.GetProperty("fonte").GetProperty("arquivos").EnumerateArray().Select(a => a.GetString()));
        Assert.NotEqual(JsonValueKind.Null, corpo.GetProperty("fonte").GetProperty("carregado_em").ValueKind);
    }

    [Fact]
    public async Task Entidade_impedida_manda_revisar_com_os_impedimentos()
    {
        var impedimentos = new ImpedimentosFalsos()
            .Com(Cnpj, new Impedimento("ENTIDADE X", "633984", "Ministério A", "NAO APRESENTACAO DA PRESTACAO DE CONTAS"))
            .Com(Cnpj, new Impedimento("ENTIDADE X", "100", "Ministério B", null));
        using var fabrica = Fabrica(impedimentos);

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = "11.222.333/0001-81" });

        var corpo = await Corpo(resposta);
        Assert.Equal("revisar", corpo.GetProperty("resultado").GetString());
        Assert.Equal("C01", corpo.GetProperty("motivos")[0].GetProperty("codigo").GetString());
        Assert.Equal("ENTIDADE X", corpo.GetProperty("entidade").GetString());
        Assert.Equal(2, corpo.GetProperty("impedimentos").GetArrayLength());
        var primeiro = corpo.GetProperty("impedimentos")[0];
        Assert.Equal("633984", primeiro.GetProperty("numero_convenio").GetString());
        Assert.Equal("Ministério A", primeiro.GetProperty("orgao_concedente").GetString());
        Assert.Equal("NAO APRESENTACAO DA PRESTACAO DE CONTAS", primeiro.GetProperty("motivo_do_impedimento").GetString());
        Assert.Equal(JsonValueKind.Null, corpo.GetProperty("impedimentos")[1].GetProperty("motivo_do_impedimento").ValueKind);
    }

    [Fact]
    public async Task Cnpj_alfanumerico_e_aceito_mesmo_em_minusculas()
    {
        var impedimentos = new ImpedimentosFalsos().Com("12ABC34501DE35", new Impedimento("ENTIDADE ALFA", "1", null, null));
        using var fabrica = Fabrica(impedimentos);

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = "12.abc.345/01de-35" });

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
        Assert.Equal("revisar", (await Corpo(resposta)).GetProperty("resultado").GetString());
    }

    [Fact]
    public async Task Campo_desconhecido_e_aceito_e_ignorado()
    {
        using var fabrica = Fabrica(new ImpedimentosFalsos());

        var resposta = await EnviarAsync(Cliente(fabrica), "{\"documento\": \"" + Cnpj + "\", \"nome\": \"ignorado\", \"extra\": {\"a\": [1, 2]}}");

        Assert.Equal(HttpStatusCode.OK, resposta.StatusCode);
    }

    [Theory]
    [InlineData("""{"documento": "123"}""")]
    [InlineData("""{"documento": "52998224725"}""")] // CPF válido: o CEPIM só tem CNPJ
    [InlineData("""{"documento": "529.982.247-25"}""")]
    [InlineData("""{"documento": "11222333000182"}""")]
    [InlineData("""{"documento": 11222333000181}""")]
    [InlineData("""{"nome": "Maria"}""")]
    [InlineData("""{"documento": null}""")]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("[1]")]
    [InlineData("")]
    public async Task Entrada_invalida_responde_422_e_nao_ecoa_nada(string json)
    {
        using var fabrica = Fabrica(new ImpedimentosFalsos());

        var resposta = await EnviarAsync(Cliente(fabrica), json);
        var texto = await resposta.Content.ReadAsStringAsync();

        await AfirmarProblema(resposta, HttpStatusCode.UnprocessableEntity, "entrada_invalida");
        Assert.DoesNotContain("52998224725", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("529.982.247-25", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("11222333000182", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Banco_indisponivel_responde_503_sem_vazar_detalhes()
    {
        var impedimentos = new ImpedimentosFalsos { Falha = new BancoIndisponivelException(new InvalidOperationException("senha=abc host=interno")) };
        using var fabrica = Fabrica(impedimentos);

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = Cnpj });
        var texto = await resposta.Content.ReadAsStringAsync();

        await AfirmarProblema(resposta, HttpStatusCode.ServiceUnavailable, "banco_indisponivel");
        Assert.DoesNotContain("senha", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("interno", texto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Banco_lento_responde_504_banco_timeout()
    {
        using var fabrica = Fabrica(new ImpedimentosFalsos { Falha = new BancoExpirouException() });

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = Cnpj });

        await AfirmarProblema(resposta, HttpStatusCode.GatewayTimeout, "banco_timeout");
    }

    [Fact]
    public async Task Health_ready_com_o_banco_fora_responde_503_banco_indisponivel_e_live_segue_200()
    {
        using var fabrica = Fabrica(new ImpedimentosFalsos { Falha = new BancoIndisponivelException() });
        var cliente = fabrica.CreateClient();

        var pronto = await cliente.GetAsync(new Uri("/health/ready", UriKind.Relative));
        var vivo = await cliente.GetAsync(new Uri("/health/live", UriKind.Relative));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, pronto.StatusCode);
        Assert.Equal("""{"status":"banco_indisponivel"}""", (await Corpo(pronto)).GetRawText());
        Assert.Equal(HttpStatusCode.OK, vivo.StatusCode);
    }

    [Fact]
    public async Task Resposta_de_erro_do_banco_leva_request_id_e_cabecalhos()
    {
        using var fabrica = Fabrica(new ImpedimentosFalsos { Falha = new BancoIndisponivelException() });

        var resposta = await Cliente(fabrica).PostAsJsonAsync(Url, new { documento = Cnpj });

        Assert.Equal("nosniff", resposta.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("no-store", resposta.Headers.CacheControl?.ToString());
        Assert.Equal((await Corpo(resposta)).GetProperty("request_id").GetString(), resposta.Headers.GetValues("X-Request-ID").Single());
    }
}
