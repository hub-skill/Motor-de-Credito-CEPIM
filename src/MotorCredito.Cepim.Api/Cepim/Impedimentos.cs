namespace MotorCredito.Cepim.Api.Cepim;

/// <summary>
/// Um impedimento do CEPIM (uma linha da tabela impedimentos). O arquivo da CGU não traz datas: todo registro listado é
/// um impedimento em vigor (a CGU retira a entidade da lista quando ela se regulariza).
/// </summary>
public sealed record Impedimento(
    string NomeEntidade,
    string? NumeroConvenio,
    string? OrgaoConcedente,
    string? MotivoDoImpedimento);

/// <summary>De onde vieram os dados: arquivos da CGU carregados e o momento da carga mais recente.</summary>
public sealed record FonteDosDados(string Banco, IReadOnlyList<string> Arquivos, DateTimeOffset? CarregadoEm);

/// <summary>Leitura do CEPIM gravado no banco. As implementações traduzem falhas do banco em 503/504.</summary>
public interface IRepositorioDeImpedimentos
{
    /// <summary>Impedimentos do CNPJ (só dígitos e letras maiúsculas, como foi gravado).</summary>
    Task<IReadOnlyList<Impedimento>> ImpedimentosDoCnpjAsync(string cnpj, CancellationToken cancellationToken);

    Task<FonteDosDados> FonteAsync(CancellationToken cancellationToken);
}
