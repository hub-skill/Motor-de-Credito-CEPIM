using System.Globalization;
using Npgsql;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Cepim.Api.Bancos;
using MotorCredito.Cepim.Api.Cepim;
using MotorCredito.Cepim.Api.Configuration;
using MotorCredito.Cepim.Api.Movimentacao;
using MotorCredito.Cepim.Carga;
using Xunit;

namespace MotorCredito.Cepim.Api.BancosTests;

/// <summary>
/// Testes contra os bancos REAIS (PostgreSQL e Oracle locais, schemas CEPIM já migrados e carregados: ver README).
/// Filtro: <c>dotnet test --filter Categoria=Bancos</c>. Cada teste apaga o que gravou.
/// </summary>
[CollectionDefinition("Bancos", DisableParallelization = true)]
public sealed class ColecaoDosBancos : ICollectionFixture<AmbienteDosBancos>;

public sealed class AmbienteDosBancos : IAsyncLifetime
{
    public const string RotaDeTeste = "/teste-bancos";

    public AmbienteDosBancos()
    {
        (Bancos, Admin) = Ambiente.Carregar();
    }

    public static string[] Provedores => ["postgres", "oracle"];

    public BancosSettings Bancos { get; }

    public Admins Admin { get; }

    public IRepositorioDeImpedimentos Impedimentos(string banco) => banco == "oracle"
        ? new ImpedimentosOracle(ConexaoAppOracle())
        : new ImpedimentosPostgres(FonteApp(somenteLeitura: true), Bancos.Postgres.Esquema);

    public IRepositorioDeMovimentacao Movimentacao(string banco) => banco == "oracle"
        ? new MovimentacaoOracle(ConexaoAppOracle())
        : new MovimentacaoPostgres(FonteApp(somenteLeitura: true), Bancos.Postgres.Esquema);

    public string ConexaoAppOracle() => ConexoesOracle.Montar(Bancos.Oracle.DataSource, Bancos.Oracle.Usuario, Bancos.Oracle.Senha);

    public NpgsqlDataSource FonteApp(bool somenteLeitura) => NpgsqlDataSource.Create(ConexoesPostgres.Montar(
        Bancos.Postgres.Host, Bancos.Postgres.Porta, Bancos.Postgres.Banco, Bancos.Postgres.Usuario, Bancos.Postgres.Senha,
        somenteLeitura));

    /// <summary>Executa SQL como administrador (para conferir e limpar). {s} vira o schema ou o dono.</summary>
    public async Task<long> AdminEscalarAsync(string banco, string sql)
    {
        if (banco == "oracle")
        {
            using var con = new OracleConnection(ConexaoAdminOracle());
            await con.OpenAsync();
            using var cmd = new OracleCommand(sql.Replace("{s}", Esquemas.OracleDono, StringComparison.Ordinal), con);
            return Convert.ToInt64(await cmd.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
        }

        await using var fonte = FonteAdmin();
        await using var comando = fonte.CreateCommand(sql.Replace("{s}", Bancos.Postgres.Esquema, StringComparison.Ordinal));
        return Convert.ToInt64(await comando.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
    }

    public async Task AdminExecutarAsync(string banco, string sql)
    {
        if (banco == "oracle")
        {
            using var con = new OracleConnection(ConexaoAdminOracle());
            await con.OpenAsync();
            using var cmd = new OracleCommand(sql.Replace("{s}", Esquemas.OracleDono, StringComparison.Ordinal), con);
            await cmd.ExecuteNonQueryAsync();
            return;
        }

        await using var fonte = FonteAdmin();
        await using var comando = fonte.CreateCommand(sql.Replace("{s}", Bancos.Postgres.Esquema, StringComparison.Ordinal));
        await comando.ExecuteNonQueryAsync();
    }

    /// <summary>Apaga o que os testes gravaram (erros antes da movimentação, por causa da chave estrangeira).</summary>
    public async Task LimparAsync(string banco)
    {
        var testes = $"rota like '{RotaDeTeste}%' or rota = '/api/v1/cepim'";
        await AdminExecutarAsync(banco, $"delete from {{s}}.log_erros where requisicao_id in (select requisicao_id from {{s}}.movimentacao where {testes})");
        await AdminExecutarAsync(banco, $"delete from {{s}}.movimentacao where {testes}");
    }

    public async Task InitializeAsync()
    {
        foreach (var banco in Provedores)
        {
            await LimparAsync(banco);
        }
    }

    public async Task DisposeAsync()
    {
        foreach (var banco in Provedores)
        {
            await LimparAsync(banco);
        }
    }

    /// <summary>Um CNPJ e um nome de entidade que existem de verdade nos dados carregados.</summary>
    public async Task<(string Cnpj, string Nome)> AmostraAsync(string banco)
    {
        var linhas = await Consultas.LerAsync(banco, Bancos, Admin, CancellationToken.None);
        return (linhas[0][0]!, linhas[0][1]!);
    }

    private NpgsqlDataSource FonteAdmin() => NpgsqlDataSource.Create(ConexoesPostgres.Montar(
        Bancos.Postgres.Host, Bancos.Postgres.Porta, Bancos.Postgres.Banco, Admin.PostgresUsuario, Admin.PostgresSenha, somenteLeitura: false));

    private string ConexaoAdminOracle() => ConexoesOracle.Montar(Bancos.Oracle.DataSource, Admin.OracleUsuario, Admin.OracleSenha);
}
