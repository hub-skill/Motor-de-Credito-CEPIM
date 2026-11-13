using Oracle.ManagedDataAccess.Client;
using MotorCredito.Cepim.Api.Bancos;

namespace MotorCredito.Cepim.Api.Cepim;

/// <summary>
/// Leitura do CEPIM no Oracle, pelo sinônimo que o script de migração cria no usuário da aplicação (nenhum nome de schema
/// no SQL). SQL fixo, com variável de ligação; falhas do banco viram 503/504.
/// </summary>
public sealed class ImpedimentosOracle(string conexaoOracle, int timeoutSegundos = 5) : IRepositorioDeImpedimentos
{
    private const string SqlImpedimentos =
        "select nome_entidade, numero_convenio, orgao_concedente, motivo_impedimento from impedimentos where cnpj = :cnpj order by numero_convenio, id";

    private const string SqlArquivos =
        "select arquivo_origem, max(carregado_em) from impedimentos group by arquivo_origem order by arquivo_origem";

    public Task<IReadOnlyList<Impedimento>> ImpedimentosDoCnpjAsync(string cnpj, CancellationToken cancellationToken) =>
        ErrosOracle.TraduzirAsync<IReadOnlyList<Impedimento>>(async () =>
        {
            using var conexao = new OracleConnection(conexaoOracle);
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var comando = Comando(conexao, SqlImpedimentos);
            comando.Parameters.Add("cnpj", OracleDbType.Varchar2).Value = cnpj;
            var lista = new List<Impedimento>();
            using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lista.Add(new Impedimento(leitor.GetString(0), Texto(leitor, 1), Texto(leitor, 2), Texto(leitor, 3)));
            }

            return lista;
        });

    public Task<FonteDosDados> FonteAsync(CancellationToken cancellationToken) =>
        ErrosOracle.TraduzirAsync(async () =>
        {
            using var conexao = new OracleConnection(conexaoOracle);
            await conexao.OpenAsync(cancellationToken).ConfigureAwait(false);
            using var comando = Comando(conexao, SqlArquivos);
            var arquivos = new List<string>();
            DateTimeOffset? maisRecente = null;
            using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                arquivos.Add(leitor.GetString(0));
                var momento = leitor.GetFieldValue<DateTimeOffset>(1); // TIMESTAMP WITH TIME ZONE: sem depender do fuso de quem lê
                maisRecente = maisRecente is null || momento > maisRecente ? momento : maisRecente;
            }

            return new FonteDosDados("oracle", arquivos, maisRecente);
        });

    private OracleCommand Comando(OracleConnection conexao, string sql)
    {
        var comando = conexao.CreateCommand();
        comando.BindByName = true;
        comando.CommandTimeout = timeoutSegundos;
        comando.CommandText = sql;
        return comando;
    }

    private static string? Texto(OracleDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : leitor.GetString(coluna);
}
