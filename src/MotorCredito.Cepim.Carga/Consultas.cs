using System.Globalization;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Cepim.Api.Bancos;
using MotorCredito.Cepim.Api.Configuration;

namespace MotorCredito.Cepim.Carga;

/// <summary>Leitura da tabela (como administrador) para a comparação entre bancos e para escolher casos de teste.</summary>
public static class Consultas
{
    public static readonly string[] Colunas =
        ["cnpj", "nome_entidade", "numero_convenio", "orgao_concedente", "motivo_impedimento", "arquivo_origem", "origem_carga"];

    /// <summary>Devolve as linhas com cada valor normalizado em texto (nulo = null), para comparar bancos diferentes.</summary>
    public static async Task<IReadOnlyList<string?[]>> LerAsync(string banco, BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var lista = string.Join(", ", Colunas);
        var linhas = new List<string?[]>();
        if (banco == "oracle")
        {
            using var con = new OracleConnection(ConexoesOracle.Montar(bancos.Oracle.DataSource, admin.OracleUsuario, admin.OracleSenha));
            await con.OpenAsync(ct);
#pragma warning disable CA2100
            using var comando = new OracleCommand($"select {lista} from {Esquemas.OracleDono}.impedimentos", con) { CommandTimeout = 120 };
#pragma warning restore CA2100
            using var leitor = await comando.ExecuteReaderAsync(ct);
            while (await leitor.ReadAsync(ct))
            {
                linhas.Add(Normalizar(leitor.FieldCount, i => leitor.IsDBNull(i) ? null : leitor.GetValue(i)));
            }

            return linhas;
        }

        var pg = bancos.Postgres;
        await using var conexao = new NpgsqlConnection(ConexoesPostgres.Montar(
            pg.Host, pg.Porta, pg.Banco, admin.PostgresUsuario, admin.PostgresSenha, somenteLeitura: false, 120,
            "motor-credito-cepim-consulta", Enum.Parse<SslMode>(pg.SslMode, true)));
        await conexao.OpenAsync(ct);
#pragma warning disable CA2100
        await using var cmd = new NpgsqlCommand($"select {lista} from {pg.Esquema}.impedimentos", conexao);
#pragma warning restore CA2100
        await using var rd = await cmd.ExecuteReaderAsync(ct);
        while (await rd.ReadAsync(ct))
        {
            linhas.Add(Normalizar(rd.FieldCount, i => rd.IsDBNull(i) ? null : rd.GetValue(i)));
        }

        return linhas;
    }

    private static string?[] Normalizar(int quantidade, Func<int, object?> valor)
    {
        var linha = new string?[quantidade];
        for (var i = 0; i < quantidade; i++)
        {
            linha[i] = valor(i) switch
            {
                null => null,
                string s => s.TrimEnd().Length == 0 ? null : s.TrimEnd(), // Oracle trata texto vazio como nulo
                var outro => Convert.ToString(outro, CultureInfo.InvariantCulture),
            };
        }

        return linha;
    }
}

/// <summary>Compara o conteúdo dos dois bancos como multiconjuntos de linhas inteiras (não há chave única garantida).</summary>
public static class Comparador
{
    public static async Task<bool> CompararAsync(BancosSettings bancos, Admins admin, CancellationToken ct)
    {
        var pg = Contar(await Consultas.LerAsync("postgres", bancos, admin, ct));
        var ora = Contar(await Consultas.LerAsync("oracle", bancos, admin, ct));
        var soPg = pg.Sum(p => Math.Max(0, p.Value - ora.GetValueOrDefault(p.Key)));
        var soOra = ora.Sum(p => Math.Max(0, p.Value - pg.GetValueOrDefault(p.Key)));
        var igual = soPg == 0 && soOra == 0;
        Console.WriteLine($"  impedimentos: PostgreSQL {pg.Values.Sum()}, Oracle {ora.Values.Sum()}, linhas só no PostgreSQL {soPg}, só no Oracle {soOra} -> {(igual ? "IGUAIS" : "DIFERENTES")}");
        return igual;
    }

    private static Dictionary<string, int> Contar(IReadOnlyList<string?[]> linhas)
    {
        var mapa = new Dictionary<string, int>(linhas.Count, StringComparer.Ordinal);
        foreach (var l in linhas)
        {
            var chave = string.Join('\u0001', l.Select(v => v ?? "\u0002"));
            mapa[chave] = mapa.GetValueOrDefault(chave) + 1;
        }

        return mapa;
    }
}
