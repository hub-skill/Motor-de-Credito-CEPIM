namespace MotorCredito.Cepim.Api.Cepim;

/// <summary>A ordem dos valores é a gravidade: a decisão mais grave prevalece.</summary>
public enum Decisao
{
    Liberar = 0,
    Revisar = 1,
}

public sealed record Motivo(string Codigo, string Texto);

public sealed record ResultadoDoCepim(Decisao Decisao, IReadOnlyList<Motivo> Motivos);

/// <summary>
/// Regras da consulta ao CEPIM. Funções puras, sem banco. Política PROVISÓRIA: validar com a área de risco/compliance.
/// C01: entidade com impedimento em vigor no CEPIM = revisar (o impedimento proíbe receber repasses federais; não é, por si
/// só, motivo para recusar crédito). OK: nenhum registro.
/// </summary>
public static class MotorDeCepim
{
    public static ResultadoDoCepim Consultar(IReadOnlyList<Impedimento> impedimentos)
    {
        ArgumentNullException.ThrowIfNull(impedimentos);
        if (impedimentos.Count == 0)
        {
            return new ResultadoDoCepim(Decisao.Liberar, [new Motivo("OK", "Nenhum impedimento no CEPIM.")]);
        }

        var texto = impedimentos.Count == 1
            ? "Entidade impedida de receber repasses federais (CEPIM): 1 impedimento."
            : $"Entidade impedida de receber repasses federais (CEPIM): {impedimentos.Count} impedimentos.";
        return new ResultadoDoCepim(Decisao.Revisar, [new Motivo("C01", texto)]);
    }
}
