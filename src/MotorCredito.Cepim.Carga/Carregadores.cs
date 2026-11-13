using Npgsql;
using NpgsqlTypes;
using Oracle.ManagedDataAccess.Client;
using MotorCredito.Cepim.Api.Bancos;
using MotorCredito.Cepim.Api.Configuration;

namespace MotorCredito.Cepim.Carga;

/// <summary>
/// Substitui o conteúdo da tabela impedimentos dentro de UMA transação por banco: se algo falhar, nada é gravado e os
/// dados anteriores continuam valendo (a API não precisa reiniciar).
/// </summary>
public static class Carregadores
{
    private const int LoteOracle = 2000;

    public static async Task PostgresAsync(BancosSettings bancos, Admins admin, string arquivo, IReadOnlyList<LinhaCepim> linhas, CancellationToken ct)
    {
        var pg = bancos.Postgres;
        await using var con = new NpgsqlConnection(ConexoesPostgres.Montar(
            pg.Host, pg.Porta, pg.Banco, admin.PostgresUsuario, admin.PostgresSenha, somenteLeitura: false,
            60, "motor-credito-cepim-carga", Enum.Parse<SslMode>(pg.SslMode, true)));
        await con.OpenAsync(ct);
        await using var tx = await con.BeginTransactionAsync(ct);

#pragma warning disable CA2100 // o schema vem da configuração e é validado no migrador; o resto do SQL é fixo
        await using (var apagar = new NpgsqlCommand($"delete from {pg.Esquema}.impedimentos", con, tx))
        {
            await apagar.ExecuteNonQueryAsync(ct);
        }

        // A importação em massa precisa ser fechada (descartada) ANTES do commit: com ela aberta, a conexão está em estado "Copy".
        await using (var importador = await con.BeginBinaryImportAsync(
            $"copy {pg.Esquema}.impedimentos (cnpj, nome_entidade, numero_convenio, orgao_concedente, motivo_impedimento, arquivo_origem, origem_carga) from stdin (format binary)", ct))
        {
#pragma warning restore CA2100
            foreach (var l in linhas)
            {
                await importador.StartRowAsync(ct);
                await importador.WriteAsync(l.Cnpj, NpgsqlDbType.Varchar, ct);
                await importador.WriteAsync(l.NomeEntidade, NpgsqlDbType.Varchar, ct);
                await Texto(importador, l.NumeroConvenio, ct);
                await Texto(importador, l.OrgaoConcedente, ct);
                await Texto(importador, l.MotivoDoImpedimento, ct);
                await importador.WriteAsync(arquivo, NpgsqlDbType.Varchar, ct);
                await importador.WriteAsync(ArquivoCepim.Marca, NpgsqlDbType.Varchar, ct);
            }

            await importador.CompleteAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static async Task Texto(NpgsqlBinaryImporter importador, string? valor, CancellationToken ct)
    {
        if (valor is null)
        {
            await importador.WriteNullAsync(ct);
        }
        else
        {
            await importador.WriteAsync(valor, NpgsqlDbType.Varchar, ct);
        }
    }

    public static async Task OracleAsync(BancosSettings bancos, Admins admin, string arquivo, IReadOnlyList<LinhaCepim> linhas, CancellationToken ct)
    {
        var o = bancos.Oracle;
        using var con = new OracleConnection(ConexoesOracle.Montar(o.DataSource, admin.OracleUsuario, admin.OracleSenha));
        await con.OpenAsync(ct);
        using var tx = con.BeginTransaction();

#pragma warning disable CA2100
        using (var apagar = new OracleCommand($"delete from {Esquemas.OracleDono}.impedimentos", con) { Transaction = tx })
#pragma warning restore CA2100
        {
            await apagar.ExecuteNonQueryAsync(ct);
        }

        const string Sql = $"insert into {Esquemas.OracleDono}.impedimentos (cnpj, nome_entidade, numero_convenio, orgao_concedente, motivo_impedimento, arquivo_origem, origem_carga) values (:cnpj, :nome, :convenio, :orgao, :motivo, :arquivo, :origem)";
        for (var inicio = 0; inicio < linhas.Count; inicio += LoteOracle)
        {
            var lote = linhas.Skip(inicio).Take(LoteOracle).ToList();
            using var comando = new OracleCommand(Sql, con) { Transaction = tx, BindByName = true, ArrayBindCount = lote.Count };
            Parametro(comando, "cnpj", lote.Select(l => (object)l.Cnpj));
            Parametro(comando, "nome", lote.Select(l => (object)l.NomeEntidade));
            Parametro(comando, "convenio", lote.Select(l => (object?)l.NumeroConvenio ?? DBNull.Value));
            Parametro(comando, "orgao", lote.Select(l => (object?)l.OrgaoConcedente ?? DBNull.Value));
            Parametro(comando, "motivo", lote.Select(l => (object?)l.MotivoDoImpedimento ?? DBNull.Value));
            Parametro(comando, "arquivo", lote.Select(_ => (object)arquivo));
            Parametro(comando, "origem", lote.Select(_ => (object)ArquivoCepim.Marca));
            await comando.ExecuteNonQueryAsync(ct);
        }

        await tx.CommitAsync(ct);
    }

    private static void Parametro(OracleCommand comando, string nome, IEnumerable<object> valores) =>
        comando.Parameters.Add(nome, OracleDbType.Varchar2).Value = valores.ToArray();
}
