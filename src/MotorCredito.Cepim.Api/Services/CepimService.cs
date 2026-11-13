using MotorCredito.Cepim.Api.Cepim;
using MotorCredito.Cepim.Api.Contracts;
using MotorCredito.Cepim.Api.Documentos;

namespace MotorCredito.Cepim.Api.Services;

public interface ICepimService
{
    Task<SaidaCepim> ConsultarAsync(DocumentoNormalizado cnpj, CancellationToken cancellationToken);
}

/// <summary>Consulta o CEPIM, aplica a regra e monta a resposta (CNPJ formatado).</summary>
public sealed class CepimService(IRepositorioDeImpedimentos impedimentos, TimeProvider relogio) : ICepimService, IDisposable
{
    // A origem dos dados (arquivos e data da carga) muda no máximo uma vez por dia: 5 minutos de cache.
    private static readonly TimeSpan ValidadeDaFonte = TimeSpan.FromMinutes(5);

    private readonly SemaphoreSlim _trava = new(1, 1);
    private FonteDosDados? _fonte;
    private DateTimeOffset _fonteValidaAte;

    private static TimeZoneInfo FusoDeBrasilia { get; } = ObterFuso();

    public async Task<SaidaCepim> ConsultarAsync(DocumentoNormalizado cnpj, CancellationToken cancellationToken)
    {
        var encontrados = await impedimentos.ImpedimentosDoCnpjAsync(cnpj.Valor, cancellationToken).ConfigureAwait(false);
        var resultado = MotorDeCepim.Consultar(encontrados);
        var fonte = await FonteAsync(cancellationToken).ConfigureAwait(false);
        var hoje = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(relogio.GetUtcNow(), FusoDeBrasilia).DateTime);

        return new SaidaCepim(
            ValidadorDeDocumento.Mascarar(cnpj),
            "CNPJ",
            resultado.Decisao.ToString().ToLowerInvariant(),
            [.. resultado.Motivos.Select(m => new MotivoSaida(m.Codigo, m.Texto))],
            encontrados.Count > 0 ? encontrados[0].NomeEntidade : null,
            [.. encontrados.Select(i => new ImpedimentoSaida(i.NumeroConvenio, i.OrgaoConcedente, i.MotivoDoImpedimento))],
            hoje,
            new FonteSaida(fonte.Banco, fonte.Arquivos, fonte.CarregadoEm));
    }

    public void Dispose() => _trava.Dispose();

    private static TimeZoneInfo ObterFuso()
    {
        foreach (var id in new[] { "America/Sao_Paulo", "E. South America Standard Time" })
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(id);
            }
            catch (TimeZoneNotFoundException)
            {
                // tenta o próximo nome (Linux x Windows)
            }
        }

        return TimeZoneInfo.CreateCustomTimeZone("UTC-03", TimeSpan.FromHours(-3), "UTC-03", "UTC-03");
    }

    private async Task<FonteDosDados> FonteAsync(CancellationToken cancellationToken)
    {
        if (_fonte is { } atual && relogio.GetUtcNow() < _fonteValidaAte)
        {
            return atual;
        }

        await _trava.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_fonte is { } outra && relogio.GetUtcNow() < _fonteValidaAte)
            {
                return outra;
            }

            _fonte = await impedimentos.FonteAsync(cancellationToken).ConfigureAwait(false);
            _fonteValidaAte = relogio.GetUtcNow().Add(ValidadeDaFonte);
            return _fonte;
        }
        finally
        {
            _trava.Release();
        }
    }
}
