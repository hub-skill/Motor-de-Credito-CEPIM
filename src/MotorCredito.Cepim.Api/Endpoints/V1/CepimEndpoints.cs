using FluentValidation;
using Microsoft.AspNetCore.Http.HttpResults;
using MotorCredito.Cepim.Api.Configuration;
using MotorCredito.Cepim.Api.Contracts;
using MotorCredito.Cepim.Api.Documentos;
using MotorCredito.Cepim.Api.Errors;
using MotorCredito.Cepim.Api.Services;

namespace MotorCredito.Cepim.Api.Endpoints.V1;

public static class CepimEndpoints
{
    public static IEndpointRouteBuilder MapCepimV1(this IEndpointRouteBuilder app)
    {
        var grupo = app.MapGroup("/api/v1")
            .RequireAuthorization()
            .WithTags("cepim");

        grupo.MapPost("/cepim", ConsultarAsync)
            .WithName("ConsultarCepim")
            .Produces<SaidaCepim>(StatusCodes.Status200OK)
            .ProducesProblem(StatusCodes.Status401Unauthorized)
            .ProducesProblem(StatusCodes.Status422UnprocessableEntity)
            .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
            .ProducesProblem(StatusCodes.Status504GatewayTimeout);

        return app;
    }

    private static async Task<Ok<SaidaCepim>> ConsultarAsync(
        EntradaCepim? entrada,
        IValidator<EntradaCepim> validador,
        ICepimService servico,
        ApiSettings settings,
        CancellationToken cancellationToken)
    {
        // Corpo vazio ou "null": 422 (sem o "?" o ASP.NET responderia 400 antes de chegar aqui).
        if (entrada is null)
        {
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        // Espaços nas pontas saem antes de validar.
        var normalizada = new EntradaCepim(entrada.Documento?.Trim());
        var validacao = await validador.ValidateAsync(normalizada, cancellationToken);
        if (!validacao.IsValid || !ValidadorDeDocumento.TentarNormalizar(normalizada.Documento, out var documento))
        {
            // Mensagem genérica: não ecoa o documento na resposta.
            throw new EntradaInvalidaException("Dados de entrada inválidos.");
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(settings.RequestTimeoutSeconds));
        try
        {
            return TypedResults.Ok(await servico.ConsultarAsync(documento, cts.Token));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ProcessamentoExpirouException(); // expirou o nosso prazo (não foi o cliente que cancelou)
        }
    }
}
