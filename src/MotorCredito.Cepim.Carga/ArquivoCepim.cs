using System.Text;
using MotorCredito.Cepim.Api.Configuration;
using MotorCredito.Cepim.Api.Documentos;

namespace MotorCredito.Cepim.Carga;

public sealed class ArquivoInvalidoException(string arquivo, IReadOnlyList<string> problemas)
    : Exception($"{arquivo}: {problemas.Count} problema(s); nada será gravado.")
{
    public string Arquivo { get; } = arquivo;

    public IReadOnlyList<string> Problemas { get; } = problemas;
}

/// <summary>Uma linha do CEPIM já convertida, na ordem das colunas da tabela impedimentos.</summary>
public sealed record LinhaCepim(string Cnpj, string NomeEntidade, string? NumeroConvenio, string? OrgaoConcedente, string? MotivoDoImpedimento);

/// <summary>
/// Lê e valida o CSV do CEPIM (ISO-8859-1, separador ';'). Colunas: CNPJ ENTIDADE, NOME ENTIDADE, NÚMERO CONVÊNIO,
/// ÓRGÃO CONCEDENTE, MOTIVO DO IMPEDIMENTO. Um problema em qualquer linha rejeita o arquivo inteiro.
/// </summary>
public static class ArquivoCepim
{
    public const string Marca = Esquemas.MarcaDeOrigem;
    public const int MaximoDeProblemasRelatados = 20;

    private static readonly (string Nome, int Maximo)[] Limites =
    [
        ("CNPJ ENTIDADE", 20), ("NOME ENTIDADE", 300), ("NÚMERO CONVÊNIO", 30), ("ÓRGÃO CONCEDENTE", 300), ("MOTIVO DO IMPEDIMENTO", 300),
    ];

    /// <summary>Os CSV da CGU vêm em ISO-8859-1 (latin-1), não em UTF-8.</summary>
    public static IReadOnlyList<LinhaCepim> Ler(string caminho)
    {
        using var leitor = new StreamReader(caminho, Encoding.Latin1);
        return Ler(leitor, Path.GetFileName(caminho));
    }

    public static IReadOnlyList<LinhaCepim> Ler(TextReader leitor, string nomeDoArquivo)
    {
        ArgumentNullException.ThrowIfNull(leitor);
        using var registros = LeitorCsv.Ler(leitor).GetEnumerator();
        if (!registros.MoveNext())
        {
            throw new ArquivoInvalidoException(nomeDoArquivo, ["arquivo vazio"]);
        }

        var cabecalho = registros.Current.Select(c => c.Trim().TrimStart('﻿')).ToArray();
        var indices = Limites.Select(l => Array.IndexOf(cabecalho, l.Nome)).ToArray();
        var ausentes = Limites.Where((_, i) => indices[i] < 0).Select(l => l.Nome).ToList();
        if (ausentes.Count > 0)
        {
            throw new ArquivoInvalidoException(
                nomeDoArquivo, [$"colunas ausentes no CSV (o layout mudou?): {string.Join(", ", ausentes)}"]);
        }

        var linhas = new List<LinhaCepim>();
        var problemas = new List<string>();
        var numero = 1;
        while (registros.MoveNext())
        {
            numero++;
            var campos = registros.Current;
            string Campo(int i) => indices[i] < campos.Length ? campos[indices[i]].Trim() : string.Empty;
            var erros = new List<string>();
            for (var i = 0; i < Limites.Length; i++)
            {
                if (Campo(i).Length > Limites[i].Maximo)
                {
                    erros.Add($"linha {numero}, coluna {Limites[i].Nome}: texto com {Campo(i).Length} caracteres (máximo {Limites[i].Maximo})");
                }
            }

            var cnpj = new string([.. Campo(0).Where(char.IsAsciiLetterOrDigit).Select(char.ToUpperInvariant)]);
            if (!ValidadorDeDocumento.TentarNormalizar(cnpj, out var doc) || doc.Tipo != TipoDocumento.Cnpj)
            {
                erros.Add($"linha {numero}, coluna CNPJ ENTIDADE: não é um CNPJ válido");
            }

            if (Campo(1).Length == 0)
            {
                erros.Add($"linha {numero}, coluna NOME ENTIDADE: vazio");
            }

            if (erros.Count > 0)
            {
                problemas.AddRange(erros.Take(Math.Max(0, MaximoDeProblemasRelatados - problemas.Count)));
                continue;
            }

            linhas.Add(new LinhaCepim(doc.Valor, Campo(1), Vazio(Campo(2)), Vazio(Campo(3)), Vazio(Campo(4))));
        }

        if (problemas.Count > 0)
        {
            throw new ArquivoInvalidoException(nomeDoArquivo, problemas);
        }

        if (linhas.Count == 0)
        {
            throw new ArquivoInvalidoException(nomeDoArquivo, ["arquivo sem nenhuma linha de dados"]);
        }

        return linhas;
    }

    private static string? Vazio(string texto) => texto.Length == 0 ? null : texto;
}
