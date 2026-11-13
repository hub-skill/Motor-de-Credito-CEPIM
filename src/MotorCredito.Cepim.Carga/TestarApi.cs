using System.Net.Http.Json;
using System.Text.Json;
using MotorCredito.Cepim.Api.Configuration;
using MotorCredito.Cepim.Api.Documentos;

namespace MotorCredito.Cepim.Carga;

/// <summary>
/// Teste de ponta a ponta contra a API no ar: escolhe, nos dados carregados, CNPJ impedidos e confere o resultado.
/// Imprime só o CNPJ já formatado devolvido pela API (dado público do CEPIM).
/// </summary>
public static class TestarApi
{
    // CNPJ válidos que (quase certamente) não estão no CEPIM; o teste usa o primeiro que não estiver nos dados carregados.
    private static readonly string[] CnpjsLivres = ["11222333000181", "19182685000162", "45997418000153"];

    public static async Task<bool> ExecutarAsync(string url, string chave, string banco, BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var linhas = await Consultas.LerAsync(banco, bancos, admin, ct);
        var impedido = linhas.Select(l => l[0]!).FirstOrDefault(c => ValidadorDeDocumento.TentarNormalizar(c, out _));
        var comVariosConvenios = linhas.GroupBy(l => l[0]!).Where(g => g.Count() > 1).Select(g => g.Key).FirstOrDefault();
        var existentes = linhas.Select(l => l[0]!).ToHashSet(StringComparer.Ordinal);
        var livre = CnpjsLivres.First(c => !existentes.Contains(c));

        using var http = new HttpClient { BaseAddress = new Uri(url), Timeout = TimeSpan.FromSeconds(30) };
        http.DefaultRequestHeaders.Add("X-API-Key", chave);
        var falhas = 0;

        async Task Caso(string nome, object corpo, int status, string? resultado, string? codigo, int? minimoDeImpedimentos = null)
        {
            using var resposta = await http.PostAsJsonAsync("/api/v1/cepim", corpo, ct);
            using var doc = JsonDocument.Parse(await resposta.Content.ReadAsStringAsync(ct));
            var raiz = doc.RootElement;
            var obtido = resposta.IsSuccessStatusCode ? raiz.GetProperty("resultado").GetString() : null;
            var codigos = resposta.IsSuccessStatusCode
                ? string.Join(",", raiz.GetProperty("motivos").EnumerateArray().Select(m => m.GetProperty("codigo").GetString()))
                : raiz.TryGetProperty("code", out var c) ? c.GetString() : null;
            var quantidade = resposta.IsSuccessStatusCode ? raiz.GetProperty("impedimentos").GetArrayLength() : 0;
            var ok = (int)resposta.StatusCode == status && obtido == resultado && (codigo is null || codigos == codigo)
                && (minimoDeImpedimentos is null || quantidade >= minimoDeImpedimentos);
            falhas += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "OK" : "FALHOU")}] {nome}: HTTP {(int)resposta.StatusCode}, resultado {obtido ?? "-"}, código {codigos ?? "-"}, impedimentos {quantidade}");
        }

        if (impedido is not null)
        {
            await Caso("CNPJ impedido no CEPIM", new { documento = impedido }, 200, "revisar", "C01", 1);
            await Caso("o mesmo CNPJ formatado", new { documento = ValidadorDeDocumento.Mascarar(new DocumentoNormalizado(impedido, TipoDocumento.Cnpj)) }, 200, "revisar", "C01", 1);
        }

        if (comVariosConvenios is not null)
        {
            await Caso("entidade com vários convênios", new { documento = comVariosConvenios }, 200, "revisar", "C01", 2);
        }

        await Caso("CNPJ fora do CEPIM", new { documento = livre }, 200, "liberar", "OK", 0);
        await Caso("CPF (o CEPIM só tem CNPJ)", new { documento = "52998224725" }, 422, null, "entrada_invalida");
        await Caso("documento inválido", new { documento = "123" }, 422, null, "entrada_invalida");
        await Caso("corpo sem documento", new { nome = "x" }, 422, null, "entrada_invalida");

        using (var sem = new HttpClient { BaseAddress = new Uri(url) })
        {
            using var r = await sem.PostAsJsonAsync("/api/v1/cepim", new { documento = livre }, ct);
            var ok = (int)r.StatusCode == 401;
            falhas += ok ? 0 : 1;
            Console.WriteLine($"  [{(ok ? "OK" : "FALHOU")}] sem chave de API: HTTP {(int)r.StatusCode}");
        }

        Console.WriteLine(falhas == 0 ? "Todos os casos passaram." : $"{falhas} caso(s) falharam.");
        return falhas == 0;
    }
}
