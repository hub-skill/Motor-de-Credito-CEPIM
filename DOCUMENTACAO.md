# Motor de Crédito CEPIM – Documentação de uso

API que consulta o **CEPIM** (Cadastro de Entidades Privadas Sem Fins Lucrativos Impedidas, da CGU/Portal da Transparência) a partir
do CNPJ. Versão 0.1.0. As decisões e as dúvidas guardadas estão em `DECISOES-CEPIM.md`.

> Os exemplos são respostas reais de um container desta API, com os dados carregados em 12/11/2026 (arquivo `20261111_CEPIM.csv`, 3.516
> registros), nos dois bancos.

## 1. Para que serve

Recebe o **CNPJ** de uma entidade privada sem fins lucrativos e informa se ela consta no CEPIM, ou seja, se está impedida de receber
repasses federais (convênios, termos de parceria). Responde `liberar` (sem registro) ou `revisar` (consta), com os motivos. Usos:
cadastro de fornecedores e parceiros, análise de crédito de entidades, PLD/KYC. Fonte: arquivo diário do Portal da Transparência
(`<aaaammdd>_CEPIM.csv`; em 11/11/2026: 3.516 registros de 1.934 entidades).

## 2. Como funciona

```
cliente --POST /api/v1/cepim--> API (.NET 10) --consulta--> PostgreSQL ou Oracle (tabela impedimentos)
                                    |
                                    +--grava--> movimentacao e log_erros (mesmo banco)
```

A API **não chama o governo**: lê a tabela local. A ferramenta `MotorCredito.Cepim.Carga` baixa o CSV e substitui a tabela.
O banco é escolhido por `Bancos__Provedor` (`postgres` ou `oracle`).

## 3. Começando em 5 minutos

```bash
# o .env precisa de Admin__Postgres__Senha e Admin__Oracle__Senha (só no arquivo, nunca no chat)
dotnet run --project src/MotorCredito.Cepim.Carga -- migrar
dotnet run --project src/MotorCredito.Cepim.Carga -- carregar
dotnet run --project src/MotorCredito.Cepim.Carga -- comparar
dotnet run --project src/MotorCredito.Cepim.Api --urls http://localhost:5080
dotnet run --project src/MotorCredito.Cepim.Carga -- testar-api --url http://localhost:5080
```

```bash
curl -X POST http://localhost:5080/api/v1/cepim \
  -H "X-API-Key: $Api__ApiKey" -H "Content-Type: application/json" -d '{"documento":"11.222.333/0001-81"}'
```

## 4. Referência da API

### Autenticação
Header `X-API-Key` (`Api__ApiKey`, mínimo 32 caracteres), comparada em tempo constante. Sem chave ou chave errada: 401 com
`WWW-Authenticate: APIKey`. Sem chave, até rota inexistente e método errado respondem 401.

### `POST /api/v1/cepim`
Corpo JSON de até **4 KB**:

| Campo | Tipo | Obrigatório | Regras |
|---|---|---|---|
| `documento` | texto | sim | **CNPJ**, com ou sem `.` `-` `/`; 14 a 20 caracteres; dígitos verificadores conferidos; aceita o CNPJ alfanumérico (maiúsculas ou minúsculas). CPF é recusado (422) |

Campo desconhecido no JSON é aceito e ignorado. Resposta 200 (snake_case), real (entidade impedida, dado público da CGU):

```json
{"documento":"35.745.520/0001-08","tipo":"CNPJ","resultado":"revisar",
 "motivos":[{"codigo":"C01","texto":"Entidade impedida de receber repasses federais (CEPIM): 1 impedimento."}],
 "entidade":"CENTRAL DAS ASSOCIACOES DE AGRICULTURA FAMILIAR - CEAPA",
 "impedimentos":[{"numero_convenio":"633984","orgao_concedente":"Ministério do Desenvolvimento Agrário e Agricultura Familiar - Unidades com vínculo direto","motivo_do_impedimento":"DESCUMPRIMENTO DE CLAUSULA PELO CONVENENTE"}],
 "consultado_em":"2026-11-12",
 "fonte":{"banco":"oracle","arquivos":["20261111_CEPIM.csv"],"carregado_em":"2026-11-12T21:35:00.127-03:00"}}
```

Entidade que não consta (real):

```json
{"documento":"11.222.333/0001-81","tipo":"CNPJ","resultado":"liberar","motivos":[{"codigo":"OK","texto":"Nenhum impedimento no CEPIM."}],
 "entidade":null,"impedimentos":[],"consultado_em":"2026-11-12",
 "fonte":{"banco":"postgres","arquivos":["20261111_CEPIM.csv"],"carregado_em":"2026-11-13T00:30:13.013244+00:00"}}
```

Sem registro: `resultado` `liberar`, motivo `OK`, `entidade` nulo e `impedimentos` vazio. `carregado_em` sai com o fuso (PostgreSQL em UTC, Oracle no fuso do banco).

### Outros endpoints
`GET /health/live` (`{"status":"ok"}`, sem consultar o banco); `GET /health/ready` (`{"status":"ok","banco":"postgres"}` ou 503
`{"status":"banco_indisponivel"}`); `GET /metrics` (Prometheus, só na rede interna); `/openapi/v1.json` e `/scalar` só com `Api__EnableDocs=true`.

### Limites e headers
60 requisições por minuto por IP (health e metrics ficam de fora); 10 s por requisição; 5 s por consulta ao banco; corpo de 4 KB. Em toda
resposta, inclusive as de erro: `X-Request-ID`, `Cache-Control: no-store`, `X-Content-Type-Options`, `X-Frame-Options`, `Strict-Transport-Security`.
Nas rotas com movimentação, `X-Requisicao` (ULID, ex.: `01JC4M7Z9Q8T2X5V6B3N0WYKHD`): informe-o ao suporte.

## 5. Regras de negócio
A política é **provisória**: validar com risco/compliance.

| Código | Situação | Efeito |
|---|---|---|
| `C01` | CNPJ consta no CEPIM | `revisar` |
| `OK` | CNPJ não consta | `liberar` |

O arquivo da CGU não traz datas: todo registro listado é impedimento em vigor (a CGU retira a entidade quando ela se regulariza). Várias linhas
do mesmo CNPJ (um convênio por linha) geram um motivo com a contagem e todas aparecem em `impedimentos[]`.

## 6. Erros
Formato RFC 9457 (`application/problem+json`) com `title`, `status`, `code` e `request_id`. Resposta real do container:

```json
{"title":"Dados de entrada inválidos.","status":422,"code":"entrada_invalida","request_id":"0HNQ1337EULSQ:00000001"}
```

| HTTP | `code` | Quando |
|---|---|---|
| 400 | `requisicao_invalida` | JSON ilegível (sintaxe, `NaN`, UTF-8 inválido, mais de 64 níveis) |
| 401 | `http_401` | sem chave, chave errada, rota/método inexistente sem chave |
| 404 / 405 | `http_404` / `http_405` | rota inexistente / método errado (com chave) |
| 413 | `corpo_muito_grande` | corpo acima de 4 KB |
| 422 | `entrada_invalida` | documento inválido, **CPF**, forma errada (tipo, `null`, lista, corpo vazio) |
| 429 | `limite_excedido` | limite por IP; `Retry-After: 60` |
| 500 | `erro_interno` | erro inesperado (detalhes só no log) |
| 503 | `banco_indisponivel` | banco fora, usuário ou senha errados, schema não migrado |
| 504 | `banco_timeout` / `timeout` | consulta ou requisição lenta |

Repita só 429, 503 e 504, com espera crescente. Toda resposta 4xx/5xx de requisição com chave válida vai para `log_erros` (só metadados).

## 7. Exemplos de integração
Postman: `postman/Motor-de-Credito-CEPIM.postman_collection.json` (preencha `api_key`, que vem vazia e secreta; `base_url` padrão `http://localhost:5080`).
C#: `http.DefaultRequestHeaders.Add("X-API-Key", chave); await http.PostAsJsonAsync("/api/v1/cepim", new { documento = "11222333000181" });`

## 8. Operação

- **Ferramenta de carga** (`MotorCredito.Cepim.Carga`, C#): `migrar` (usuário, schema, tabela, funções e permissões; repetível), `carregar` (baixa o CSV e substitui a
  tabela numa transação por banco; `--pasta` usa um CSV já baixado), `comparar` (PostgreSQL x Oracle, linhas inteiras), `testar-api`. O Portal só mantém o arquivo mais recente
  e o do dia pode não estar publicado (403): a ferramenta recua até 7 dias. **Carga diária não agendada.** Precisa de `Admin__Postgres__Senha` e `Admin__Oracle__Senha`
  no `.env` (a ferramenta avisa quando faltam). Carga medida em 12/11/2026: PostgreSQL 0,6 s e Oracle 1,0 s (3.516 linhas).
- **Container:** `docker build -t motor-credito-cepim:0.1.0 .` e `docker run -d -p 8080:8080 --env-file .env.docker motor-credito-cepim:0.1.0`. Imagem `aspnet:10.0-noble-chiseled-extra`
  de 258 MB, usuário não root (1654), porta 8080. Verificado em 12/11/2026: hadolint só com informação (DL3066), trivy HIGH/CRITICAL = 0; `ready` 200 e chamada real 200 com PostgreSQL e com Oracle; com o banco
  ainda não migrado, `live` 200, `ready` 503, chamada 503 `banco_indisponivel`, CPF 422 e sem chave 401, e o container segue de pé.
- **Kubernetes:** `k8s/base` e overlays `local` e `producao`; o overlay local passa no kustomize e no kubeconform (6 recursos válidos). **Não aplicado em cluster.**
  O rate limit é por pod.
- **Movimentação:** tabelas `movimentacao` (chave `requisicao_id`, o ULID gerado na API) e `log_erros` (gravado na mesma transação), com CPF mascarado; sem contador nem numeração sequencial por ano. Defina retenção e acesso. Para consulta a dados públicos como esta, o módulo é opcional.
- **Testes:** `dotnet test MotorCredito.Cepim.slnx --filter "Categoria!=Bancos"` (165 testes) e `dotnet test tests/MotorCredito.Cepim.Api.BancosTests` (10 testes com PostgreSQL e Oracle reais: leitura, permissões negadas, numeração da movimentação, chave estrangeira do log e a API de ponta a ponta).

## 9. Configuração
Variáveis de ambiente ou `.env` (`__` separa seções; variável já definida tem prioridade):

| Chave | Padrão | Uso |
|---|---|---|
| `Api__ApiKey` | (obrigatória) | chave do `X-API-Key`, mínimo 32 caracteres |
| `Api__AllowedHosts__0..` | `localhost` | hosts aceitos (inclua `localhost` para as probes) |
| `Api__EnableDocs` | `false` | OpenAPI e Scalar |
| `Api__RequestTimeoutSeconds` / `Api__RateLimitPorMinuto` | `10` / `60` | prazo e limite por IP |
| `Bancos__Provedor` | `postgres` | `postgres` ou `oracle` |
| `Bancos__Postgres__Host/Porta/Banco/Usuario/Senha/SslMode/Esquema` | `127.0.0.1`, `5432`, `motor_credito`, `motor_app_cepim`, vazio, `Prefer`, `credito_cepim` | conexão da aplicação |
| `Bancos__Oracle__DataSource/Usuario/Senha` | `localhost:1521/XEPDB1`, `MOTOR_APP_CEPIM`, vazio | conexão (senha de até 30 caracteres) |
| `Admin__Postgres__Usuario/Senha`, `Admin__Oracle__Usuario/Senha` | `postgres`, `SYSTEM` | só a ferramenta de carga |

Objetos: PostgreSQL schema `credito_cepim`, usuário `motor_app_cepim`; Oracle dono `MOTOR_CREDITO_CEPIM` (sem login), usuário `MOTOR_APP_CEPIM` (por sinônimos).
A aplicação só **lê** `impedimentos`.

## 10. Segurança e LGPD
O CNPJ e o nome da entidade são dados públicos da CGU; mesmo assim, entrada e erros não ecoam o documento, e o CPF recebido por engano é mascarado na movimentação
(`***.982.247-**`). A chave de API nunca é gravada; sem chave válida, 401 e nada é gravado. Privilégio mínimo no banco (leitura da lista, execução das rotinas de movimentação,
só `INSERT` em `log_erros`); sessão de leitura; consultas parametrizadas; segredos só no `.env`.

## 11. Limitações
Política provisória; sem datas de vigência; dados tão novos quanto a última carga (sem agendamento); rate limit por pod e em memória; Kubernetes não aplicado em cluster;
custo da movimentação não medido.

## 12. Solução de problemas
| Sintoma | Causa provável |
|---|---|
| 401 em tudo | `X-API-Key` ausente ou diferente de `Api__ApiKey` |
| 503 `banco_indisponivel` | banco fora, usuário/senha errados, schema não migrado (é o que acontece antes de `migrar`) |
| `Falta Admin__...__Senha no .env` | preencha a senha do administrador no arquivo |
| `ORA-00972` ao migrar | senha do Oracle com mais de 30 caracteres |
| Carga falha com "colunas ausentes" | a CGU mudou o layout do CSV |
| Build falha com `MSB3021` | a API ainda está rodando e trava o executável |
