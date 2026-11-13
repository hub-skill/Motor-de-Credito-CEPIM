# Motor de Crédito CEPIM

API em ASP.NET Core (.NET 10) que recebe o **CNPJ** de uma entidade e consulta o **CEPIM** (Cadastro de Entidades Privadas Sem
Fins Lucrativos Impedidas, da CGU) gravado em PostgreSQL ou Oracle. Responde `liberar` (sem registro) ou `revisar` (entidade
impedida de receber repasses federais), sempre com os motivos. Gerada a partir das skills `criar-api`, `docker` e `kubernetes`.

| Arquivo | O que é |
|---|---|
| `DOCUMENTACAO.md` | uso, referência, operação, configuração |
| `DECISOES-CEPIM.md` | decisões tomadas sem perguntar e dúvidas guardadas para você |
| `PROBLEMAS-E-CORRECOES.md` | o que deu errado nesta implementação e a correção |
| `ARQUIVOS-LIDOS.md` | tudo o que foi aberto, copiado ou baixado |

```
src/MotorCredito.Cepim.Api     a API (POST /api/v1/cepim, health, métricas) com documentos, bancos e movimentação
src/MotorCredito.Cepim.Carga   ferramenta: migrar, carregar, comparar, testar-api
banco/                         scripts SQL (PostgreSQL e Oracle), embutidos na ferramenta
tests/                         UnitTests, IntegrationTests, Carga.Tests, BancosTests (bancos reais)
k8s/  Dockerfile  postman/  .github/workflows/ci.yml
```

Começo rápido (o `.env` precisa de `Admin__Postgres__Senha` e `Admin__Oracle__Senha`; só no arquivo):

```bash
dotnet run --project src/MotorCredito.Cepim.Carga -- migrar
dotnet run --project src/MotorCredito.Cepim.Carga -- carregar
dotnet run --project src/MotorCredito.Cepim.Api --urls http://localhost:5080
dotnet test MotorCredito.Cepim.slnx --filter "Categoria!=Bancos"
```
