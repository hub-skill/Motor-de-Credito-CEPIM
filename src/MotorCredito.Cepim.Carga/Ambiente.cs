using Microsoft.Extensions.Configuration;
using MotorCredito.Cepim.Api.Configuration;

namespace MotorCredito.Cepim.Carga;

public sealed record Admins(string PostgresUsuario, string PostgresSenha, string OracleUsuario, string OracleSenha);

/// <summary>Configuração da ferramenta: .env e variáveis de ambiente (as variáveis têm prioridade).</summary>
public static class Ambiente
{
    public static (BancosSettings Bancos, Admins Admins) Carregar()
    {
        ArquivoDotEnv.Carregar();
        var configuracao = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var bancos = configuracao.GetSection(BancosSettings.SectionName).Get<BancosSettings>() ?? new BancosSettings();
        var admin = configuracao.GetSection("Admin");
        return (bancos, new Admins(
            admin["Postgres:Usuario"] is { Length: > 0 } pu ? pu : "postgres",
            admin["Postgres:Senha"] ?? string.Empty,
            admin["Oracle:Usuario"] is { Length: > 0 } ou ? ou : "SYSTEM",
            admin["Oracle:Senha"] ?? string.Empty));
    }
}
