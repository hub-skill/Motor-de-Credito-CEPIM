# Problemas e correções (implementação do CEPIM, 12/11/2026)

| # | Problema | Causa | Correção |
|---|---|---|---|
| 1 | Download do CEPIM respondeu 302 e arquivo vazio | O portal redireciona; `curl` sem `-L` não segue | Seguir o redirecionamento (a ferramenta C# usa o `HttpClient`, que segue) |
| 2 | Download do dia respondeu 403 | O arquivo de hoje ainda não foi publicado (o de ontem, 20261111, respondeu 200) | O baixador recua até 7 dias |
| 3 | Build falhou: CA1861 (matriz constante em chamada repetida) em `TestarApi` | Analisadores com avisos como erro (lição já registrada na skill) | Campo `static readonly` |
| 4 | Build dos testes falhou: CS9007 em texto bruto interpolado | `}}` do JSON dentro de `$$"""` conta como fechamento de interpolação | Montar o JSON por concatenação |
| 5 | Build dos testes de banco falhou: `CultureInfo` sem `using` | Esqueci o `using System.Globalization` | Acrescentei o `using` |
| 6 | Não consigo migrar nem carregar | Faltavam as senhas do administrador dos bancos e eu não podia abrir o `.env` de outras APIs | `.env` novo com as quatro linhas `Admin__*` em branco; depois você mandou "fazer como na api nova" e liberou arquivos de configuração: copiei só essas linhas, sem imprimir. As mensagens da ferramenta pedem `Admin__*__Senha` quando faltam |
| 7 | Carga no PostgreSQL falhou: `NpgsqlOperationInProgressException: The connection is already in state 'Copy'` | O `commit` rodava com a importação em massa ainda aberta (o `await using` estava no escopo do método) | A importação ficou num bloco `await using (...) { ... }`, descartada antes do `commit` |
| 8 | `carregado_em` do Oracle saía com 3 horas de erro no container | Coluna `TIMESTAMP` sem fuso, lida como hora local (o container está em UTC) | Coluna `TIMESTAMP WITH TIME ZONE`, leitura como `DateTimeOffset`; ajustei a tabela existente (vazia) por um script C# e recarreguei |

Já evitado desde o início por causa das lições da skill: `.env` desligado nos testes, todos os repositórios trocados por falsos nas
fábricas, corpo do teste de profundidade dentro do limite de 4 KB, senha do Oracle com até 24 caracteres, comparação por multiconjunto,
nome do recurso embutido com barra invertida normalizado, `MSB3021` (parar a API antes de compilar).
