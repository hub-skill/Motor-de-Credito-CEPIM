using System.Globalization;
using MotorCredito.Cepim.Carga;

// Ferramenta do Motor de Crédito CEPIM: migrar, carregar, comparar e testar-api. Ver "ajuda".
var comando = args.Length > 0 ? args[0].ToLowerInvariant() : "ajuda";
var opcoes = args.Skip(1).Chunk(2).Where(p => p.Length == 2 && p[0].StartsWith("--", StringComparison.Ordinal))
    .ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
var banco = opcoes.GetValueOrDefault("--banco", "todos").ToLowerInvariant();
var bancosAlvo = banco == "todos" ? new[] { "postgres", "oracle" } : [banco];
if (bancosAlvo.Any(b => b is not ("postgres" or "oracle")))
{
    Console.Error.WriteLine("--banco deve ser postgres, oracle ou todos.");
    return 2;
}

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };
var ct = cts.Token;

try
{
    var (bancos, admin) = Ambiente.Carregar();
    switch (comando)
    {
        case "migrar":
            foreach (var b in bancosAlvo)
            {
                Console.WriteLine($"Migrando {b}...");
                await (b == "postgres" ? Migrador.PostgresAsync(bancos, admin, ct) : Migrador.OracleAsync(bancos, admin, ct));
            }

            return 0;

        case "carregar":
            await CarregarAsync(bancos, admin, bancosAlvo, opcoes.GetValueOrDefault("--pasta"), ct);
            return 0;

        case "comparar":
            return await Comparador.CompararAsync(bancos, admin, ct) ? 0 : 1;

        case "testar-api":
            var url = opcoes.GetValueOrDefault("--url", "http://localhost:5080");
            var chave = opcoes.GetValueOrDefault("--chave") ?? Environment.GetEnvironmentVariable("Api__ApiKey") ?? string.Empty;
            return await TestarApi.ExecutarAsync(url, chave, bancosAlvo[0], bancos, admin, ct) ? 0 : 1;

        default:
            Console.WriteLine("""
                Uso: dotnet run --project src/MotorCredito.Cepim.Carga -- <comando> [opções]
                  migrar      [--banco postgres|oracle|todos]   cria usuário, schema, tabela e permissões
                  carregar    [--banco ...] [--pasta <csv>]     baixa o CEPIM da CGU (ou usa o CSV da pasta) e grava
                  comparar                                      confere se PostgreSQL e Oracle têm os mesmos dados
                  testar-api  [--url ...] [--chave ...] [--banco postgres|oracle]   casos reais contra a API no ar
                """);
            return comando == "ajuda" ? 0 : 2;
    }
}
catch (ArquivoInvalidoException e)
{
    Console.Error.WriteLine($"Arquivo inválido: {e.Message}");
    foreach (var problema in e.Problemas)
    {
        Console.Error.WriteLine($"  - {problema}");
    }

    return 1;
}
catch (Exception e) when (e is not OperationCanceledException)
{
    // Só o tipo e a mensagem: nunca a exceção inteira (a do driver pode trazer detalhes da conexão).
    Console.Error.WriteLine($"Falhou: {e.GetType().Name}: {e.Message}");
    return 1;
}

static async Task CarregarAsync(MotorCredito.Cepim.Api.Configuration.BancosSettings bancos, Admins admin, string[] bancosAlvo, string? pasta, CancellationToken ct)
{
    var baixar = pasta is null;
    pasta ??= Path.Combine(Directory.GetCurrentDirectory(), "dados-cgu");
    if (baixar)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("MotorCredito-Cepim-Carga/1.0");
        Console.WriteLine("Baixando CEPIM...");
        Console.WriteLine($"  {Path.GetFileName(await new Baixador(http).BaixarAsync(pasta, DateOnly.FromDateTime(DateTime.Today), ct))}");
    }

    var csv = Directory.GetFiles(pasta, "*_CEPIM.csv").OrderDescending().FirstOrDefault()
        ?? throw new FileNotFoundException($"Nenhum arquivo *_CEPIM.csv em {pasta}.");

    // Lê e valida tudo ANTES de tocar em qualquer banco.
    Console.WriteLine("Lendo e validando o arquivo...");
    var linhas = ArquivoCepim.Ler(csv);
    var nome = Path.GetFileName(csv);
    Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  impedimentos: {linhas.Count}"));

    foreach (var b in bancosAlvo)
    {
        var relogio = System.Diagnostics.Stopwatch.StartNew();
        Console.WriteLine($"Gravando em {b}...");
        await (b == "postgres" ? Carregadores.PostgresAsync(bancos, admin, nome, linhas, ct) : Carregadores.OracleAsync(bancos, admin, nome, linhas, ct));
        Console.WriteLine(string.Create(CultureInfo.InvariantCulture, $"  {b}: ok em {relogio.Elapsed.TotalSeconds:F1} s"));
    }
}
