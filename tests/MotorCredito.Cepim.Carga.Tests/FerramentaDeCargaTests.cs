using MotorCredito.Cepim.Carga;
using Xunit;

namespace MotorCredito.Cepim.Carga.Tests;

public sealed class LeitorCsvTests
{
    private static List<string[]> Ler(string texto) => [.. LeitorCsv.Ler(new StringReader(texto))];

    [Fact]
    public void Separa_por_ponto_e_virgula_e_aceita_ultima_linha_sem_quebra()
    {
        var linhas = Ler("a;b;c\r\n1;2;3");

        Assert.Equal(2, linhas.Count);
        Assert.Equal(["a", "b", "c"], linhas[0]);
        Assert.Equal(["1", "2", "3"], linhas[1]);
    }

    [Fact]
    public void Campo_entre_aspas_pode_ter_separador_aspas_duplas_e_quebra_de_linha()
    {
        var linha = Assert.Single(Ler("\"a;b\";\"disse \"\"oi\"\"\";\"linha1\nlinha2\"\n"));

        Assert.Equal(["a;b", "disse \"oi\"", "linha1\nlinha2"], linha);
    }

    [Fact]
    public void Campos_vazios_e_linhas_em_branco()
    {
        var linhas = Ler("a;;c\n\n;;\n");

        Assert.Equal(2, linhas.Count);
        Assert.Equal(["a", "", "c"], linhas[0]);
        Assert.Equal(["", "", ""], linhas[1]);
    }
}

public sealed class ArquivoCepimTests
{
    private const string Cabecalho = "\"CNPJ ENTIDADE\";\"NOME ENTIDADE\";\"NÚMERO CONVÊNIO\";\"ÓRGÃO CONCEDENTE\";\"MOTIVO DO IMPEDIMENTO\"";

    private static IReadOnlyList<LinhaCepim> Ler(string csv) => ArquivoCepim.Ler(new StringReader(csv), "teste.csv");

    [Fact]
    public void Le_o_layout_real_e_normaliza_o_cnpj()
    {
        var linhas = Ler($"{Cabecalho}\n\"11.222.333/0001-81\";\"ENTIDADE X\";\"633984\";\"Ministério A - Unidades\";\"INSTAURACAO DE TOMADA DE CONTAS ESPECIAL\"\n\"12abc34501de35\";\"ENTIDADE ALFA\";\"1\";\"Órgão B\";\"MOTIVO NÃO ESPECIFICADO\"\n");

        Assert.Equal(2, linhas.Count);
        Assert.Equal(new LinhaCepim("11222333000181", "ENTIDADE X", "633984", "Ministério A - Unidades", "INSTAURACAO DE TOMADA DE CONTAS ESPECIAL"), linhas[0]);
        Assert.Equal("12ABC34501DE35", linhas[1].Cnpj);
    }

    [Fact]
    public void Campos_opcionais_vazios_viram_nulo_e_cabecalho_com_bom_e_aceito()
    {
        var linhas = Ler($"﻿{Cabecalho}\n\"11222333000181\";\"ENTIDADE X\";\"\";\"\";\"\"\n");

        var linha = Assert.Single(linhas);
        Assert.Null(linha.NumeroConvenio);
        Assert.Null(linha.OrgaoConcedente);
        Assert.Null(linha.MotivoDoImpedimento);
    }

    [Fact]
    public void Coluna_ausente_rejeita_o_arquivo_inteiro()
    {
        var erro = Assert.Throws<ArquivoInvalidoException>(() => Ler("\"CNPJ ENTIDADE\";\"OUTRA\"\n\"11222333000181\";\"x\"\n"));

        Assert.Contains("colunas ausentes", erro.Problemas[0], StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("\"123\";\"ENTIDADE X\";\"1\";\"O\";\"M\"", "CNPJ ENTIDADE")]
    [InlineData("\"52998224725\";\"ENTIDADE X\";\"1\";\"O\";\"M\"", "CNPJ ENTIDADE")] // CPF: o CEPIM só tem CNPJ
    [InlineData("\"11222333000181\";\"\";\"1\";\"O\";\"M\"", "NOME ENTIDADE")]
    public void Linha_com_problema_rejeita_tudo_e_informa_a_linha_e_a_coluna(string linhaRuim, string coluna)
    {
        var erro = Assert.Throws<ArquivoInvalidoException>(
            () => Ler($"{Cabecalho}\n\"11222333000181\";\"BOA\";\"1\";\"O\";\"M\"\n{linhaRuim}\n"));

        var problema = Assert.Single(erro.Problemas);
        Assert.StartsWith($"linha 3, coluna {coluna}", problema, StringComparison.Ordinal);
    }

    [Fact]
    public void Texto_acima_do_tamanho_da_coluna_e_recusado()
    {
        var erro = Assert.Throws<ArquivoInvalidoException>(
            () => Ler($"{Cabecalho}\n\"11222333000181\";\"{new string('a', 301)}\";\"1\";\"O\";\"M\"\n"));

        Assert.Contains("NOME ENTIDADE", erro.Problemas[0], StringComparison.Ordinal);
    }

    [Fact]
    public void Arquivo_vazio_ou_so_com_cabecalho_e_rejeitado()
    {
        Assert.Throws<ArquivoInvalidoException>(() => Ler(string.Empty));
        Assert.Throws<ArquivoInvalidoException>(() => Ler($"{Cabecalho}\n"));
    }
}

public sealed class MigradorTests
{
    [Fact]
    public void Separa_os_comandos_pelas_linhas_com_barra_e_descarta_comentarios()
    {
        const string Script = """
            -- comentário do script
            CREATE TABLE a (x NUMBER)
            /
            -- outro comentário
            CREATE OR REPLACE PROCEDURE p AS
            BEGIN
                -- comentário dentro do bloco
                NULL;
            END;
            /
            COMMENT ON TABLE a IS 'texto com / no meio'
            /
            """;

        var comandos = Migrador.SepararComandos(Script);

        Assert.Equal(3, comandos.Count);
        Assert.StartsWith("CREATE TABLE a", comandos[0], StringComparison.Ordinal);
        Assert.Contains("END;", comandos[1], StringComparison.Ordinal);
        Assert.DoesNotContain("--", comandos[1], StringComparison.Ordinal);
        Assert.Contains("'texto com / no meio'", comandos[2], StringComparison.Ordinal);
    }
}
