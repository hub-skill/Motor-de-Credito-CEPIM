using FluentValidation;
using MotorCredito.Cepim.Api.Contracts;
using MotorCredito.Cepim.Api.Documentos;

namespace MotorCredito.Cepim.Api.Validation;

public sealed class EntradaCepimValidator : AbstractValidator<EntradaCepim>
{
    public EntradaCepimValidator()
    {
        // A entrada já chega sem espaços nas pontas (ver CepimEndpoints). O CEPIM só tem CNPJ: CPF é recusado.
        RuleFor(x => x.Documento)
            .NotEmpty()
            .Length(14, 20) // cabe a máscara (CNPJ formatado tem 18)
            .Must(d => ValidadorDeDocumento.TentarNormalizar(d, out var doc) && doc.Tipo == TipoDocumento.Cnpj)
            .WithMessage("documento não é um CNPJ válido");
    }
}
