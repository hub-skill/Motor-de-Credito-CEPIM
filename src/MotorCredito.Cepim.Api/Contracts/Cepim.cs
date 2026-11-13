namespace MotorCredito.Cepim.Api.Contracts;

/// <summary>Entrada: o CNPJ da entidade (com ou sem pontuação). Pode ser nulo no JSON; a validação responde 422.</summary>
public sealed record EntradaCepim(string? Documento = null);

public sealed record MotivoSaida(string Codigo, string Texto);

public sealed record ImpedimentoSaida(string? NumeroConvenio, string? OrgaoConcedente, string? MotivoDoImpedimento);

public sealed record FonteSaida(string Banco, IReadOnlyList<string> Arquivos, DateTimeOffset? CarregadoEm);

/// <summary>Saída em snake_case. O CNPJ sai formatado; <c>entidade</c> é o nome registrado no CEPIM (nulo se não há registro).</summary>
public sealed record SaidaCepim(
    string Documento,
    string Tipo,
    string Resultado,
    IReadOnlyList<MotivoSaida> Motivos,
    string? Entidade,
    IReadOnlyList<ImpedimentoSaida> Impedimentos,
    DateOnly ConsultadoEm,
    FonteSaida Fonte);
