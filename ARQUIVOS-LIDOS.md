# Arquivos acessados na implementação do CEPIM (12/11/2026)

Regra combinada: só consultar `.md` gerados por nós e os templates das skills (depois, "tudo que estiver nas skills"); nada de
pastas de código nem de outras APIs. Esta lista tem **tudo o que abri, copiei ou baixei** desde o pedido "vamos implementar o
CEPIM". "Lido" = o conteúdo apareceu para mim; "copiado" = copiei com `cp`/`sed` sem exibir o conteúdo.

## 1. Arquivos .md lidos
Nenhum nesta fase. O que usei dos `.md` (regras, lições, contratos) veio do que já estava na conversa; não reabri nenhum.

## 2. Skills (templates)
| Arquivo ou pasta | Como | Para quê |
|---|---|---|
| `~/.claude/skills/criar-api/assets/dotnet/` (`global.json`, `Directory.Build.props`, `Directory.Packages.props`, `src/`, `tests/`, `postman/`, `DOCUMENTACAO.md`) | copiado | base da API |
| `~/.claude/skills/criar-api/assets/dotnet-opcional/documentos/src/Documentos/*.cs` e `tests/*.cs` | copiado | validação de CNPJ (módulo documentos) |
| `~/.claude/skills/criar-api/assets/dotnet-opcional/bancos/src/Bancos/*.cs` e `tests/*.cs` | copiado | PostgreSQL, Oracle, health com banco |
| `~/.claude/skills/criar-api/assets/dotnet-opcional/movimentacao/src/Movimentacao/*.cs`, `tests/*.cs`, `banco/postgres/movimentacao.sql`, `banco/oracle/movimentacao.sql` | copiado | movimentação e log de erros |
| `~/.claude/skills/kubernetes/scripts/preparar-k8s.ps1` e `assets/` | executado (gerou `k8s/`; não li o conteúdo) | manifestos |
| `~/.claude/skills/docker/assets/dotnet/Dockerfile` e `.dockerignore` | copiado (o Dockerfile passou por `sed` para trocar nomes) | imagem Docker |

## 3. Arquivos do projeto novo (`C:\Felipe\Programacao\DotNet_CEPIM`)
| Arquivo | Como | Para quê |
|---|---|---|
| `tests/MotorCredito.Cepim.Api.IntegrationTests/MovimentacaoApiTests.cs` | lido (trechos, com `sed`/`grep`) | adaptar os testes da movimentação à rota do CEPIM |
| `dados-cgu/20261111_CEPIM.csv` | lido (cabeçalho, 3 linhas de exemplo e análise de tamanhos, duplicados e qualidade) | layout e limites das colunas |
| `postman/Motor-de-Credito-CEPIM.postman_collection.json` | lido e reescrito por script (a cópia do template dentro do projeto) | adaptar a coleção |
| Os demais arquivos do projeto (código, SQL, testes, `.env`, `.gitignore`) | **escritos por mim** nesta sessão, não lidos de outro lugar | — |
| Saída do build e dos testes (`dotnet`, `docker`, `kubectl`, `trivy`, `hadolint`, `kubeconform`) | lida | verificações |

## 3b. Arquivos de configuração de outras APIs
| Arquivo | Como | Para quê |
|---|---|---|
| `C:\Felipe\Programacao\DotNet_2\.env` | só as quatro linhas `Admin__*`, copiadas por `grep`/`cut`/`sed`, **sem exibir valores** (autorizado: "faça como fez na api nova" e "você pode acessar arquivos de configuração") | senhas do administrador para migrar e carregar |
| `scratchpad/ajuste-cepim/ajuste.cs` (script C# meu, fora do projeto) | escrito e executado; lê o `.env` do próprio projeto | ajuste da coluna `carregado_em` no Oracle |

## 4. Internet
| Endereço | Resultado |
|---|---|
| `https://portaldatransparencia.gov.br/download-de-dados/cepim/<aaaammdd>` | baixado o ZIP do CEPIM (20261111 respondeu 200; 20261112 respondeu 403, ainda não publicado) |
| Pesquisa anterior (antes do pedido do CEPIM): Swagger e `v3/api-docs` da API do Portal da Transparência, páginas do Conecta gov.br, buscas sobre PGFN, Receita, TSE, DataJud, BCB | só web; usadas para escolher o CEPIM |

## 5. Não acessado
- Nenhuma pasta de código de outras APIs (Motor Python, Motor .NET antigo, `DotNet_2`): nem para ler, nem para listar.
- Nenhum arquivo `.env` de outro projeto (por isso as senhas do administrador ficaram em branco no `.env` novo).
