using Npgsql;
using MotorCredito.Cepim.Api.Bancos;
using MotorCredito.Cepim.Api.Movimentacao;

namespace MotorCredito.Cepim.Api.Cepim;

/// <summary>Leitura do CEPIM no PostgreSQL. SQL fixo e parametrizado; falhas do banco viram 503/504.</summary>
public sealed class ImpedimentosPostgres : IRepositorioDeImpedimentos
{
    private readonly NpgsqlDataSource _fonte;
    private readonly string _sqlImpedimentos;
    private readonly string _sqlArquivos;

    public ImpedimentosPostgres(NpgsqlDataSource fonte, string esquema)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        MovimentacaoPostgres.ValidarEsquema(esquema); // o schema vem da configuração e entra no SQL
        _fonte = fonte;
        _sqlImpedimentos =
            $"select nome_entidade, numero_convenio, orgao_concedente, motivo_impedimento from {esquema}.impedimentos where cnpj = $1 order by numero_convenio, id";
        _sqlArquivos =
            $"select arquivo_origem, max(carregado_em) from {esquema}.impedimentos group by arquivo_origem order by arquivo_origem";
    }

    public Task<IReadOnlyList<Impedimento>> ImpedimentosDoCnpjAsync(string cnpj, CancellationToken cancellationToken) =>
        ErrosPostgres.TraduzirAsync<IReadOnlyList<Impedimento>>(async () =>
        {
            await using var comando = _fonte.CreateCommand(_sqlImpedimentos);
            comando.Parameters.Add(new NpgsqlParameter { Value = cnpj });
            var lista = new List<Impedimento>();
            await using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                lista.Add(new Impedimento(leitor.GetString(0), Texto(leitor, 1), Texto(leitor, 2), Texto(leitor, 3)));
            }

            return lista;
        });

    public Task<FonteDosDados> FonteAsync(CancellationToken cancellationToken) =>
        ErrosPostgres.TraduzirAsync(async () =>
        {
            await using var comando = _fonte.CreateCommand(_sqlArquivos);
            var arquivos = new List<string>();
            DateTimeOffset? maisRecente = null;
            await using var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            while (await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
            {
                arquivos.Add(leitor.GetString(0));
                var momento = leitor.GetFieldValue<DateTimeOffset>(1);
                maisRecente = maisRecente is null || momento > maisRecente ? momento : maisRecente;
            }

            return new FonteDosDados("postgres", arquivos, maisRecente);
        });

    private static string? Texto(NpgsqlDataReader leitor, int coluna) =>
        leitor.IsDBNull(coluna) ? null : leitor.GetString(coluna);
}
