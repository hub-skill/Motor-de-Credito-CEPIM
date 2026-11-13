# Decisões da implementação do CEPIM (12/11/2026)

Tudo o que foi decidido sem perguntar (como combinado) e as dúvidas guardadas para você. Nomes novos levam o sufixo **CEPIM**.

## 1. Dúvidas guardadas para você

| # | Assunto | O que fiz | Pergunta |
|---|---|---|---|
| 1 | Política de decisão | CNPJ listado no CEPIM = `revisar` (código `C01`); não listado = `liberar` (`OK`). Marcada como **provisória** | Concorda, ou o CEPIM deve `bloquear`? Quem é o dono do risco? |
| 2 | Vigência | O CSV da CGU **não tem datas**: considerei todo registro listado como impedimento em vigor (a CGU retira a entidade quando ela se regulariza). Não existe "encerrado" | Há outra fonte com datas (a API do Portal tem `cepim`)? |
| 3 | Só CNPJ | CPF é recusado com 422 (o CEPIM só tem entidades) | Aceitar CPF e responder `liberar`? |
| 4 | Sem chave única | `impedimentos` não tem `unique`: nos dados de hoje (cnpj, convênio) não repete, mas a CGU não garante | Manter assim? |
| 5 | Movimentação | Incluí o módulo (movimentação e log de erros), como nas APIs anteriores | Quer sem? |
| 6 | Quem carrega | A carga roda como **administrador**; a aplicação só lê | Quando aplicarmos a sua resposta "sim" ao item 6 da `DotNet_2` (aplicação gravando), aplica-se aqui também? |
| 7 | Campo desconhecido no JSON | Aceito e ignorado (mesma decisão das outras APIs) | Manter? |
| 8 | Pasta e nomes | Pasta `DotNet_CEPIM`; PostgreSQL schema `credito_cepim`, usuário `motor_app_cepim` (banco `motor_credito`); Oracle `MOTOR_CREDITO_CEPIM` (dono) e `MOTOR_APP_CEPIM`; imagem `motor-credito-cepim`; projetos `MotorCredito.Cepim.*` | Ok? |
| 9 | Resposta | `entidade` (nome registrado) no topo e lista `impedimentos[]` com convênio, órgão e motivo; nome da entidade e convênios são dados públicos da CGU | Ok expor o nome da entidade? |
| 10 | Atualização | O arquivo é diário; o do dia pode não estar publicado (403): a carga recua até 7 dias. Carga diária **não agendada** | Agendar? |
| 11 | Git | Não rodei `git init` nem commit | Quer o repositório? |

## 2. Credenciais do administrador
O `.env` novo nasceu com as senhas da aplicação e a chave de API geradas. As quatro linhas `Admin__*` foram copiadas do `.env` da `DotNet_2` (a
pedido seu: "faça como fez na api nova"; depois você liberou o acesso a arquivos de configuração). Nenhum valor foi impresso. Código de outras
APIs continua sem acesso.

## 3. O que foi verificado (12/11/2026)
| Verificação | Resultado |
|---|---|
| `dotnet format --verify-no-changes` | sem mudanças |
| `dotnet build` | 0 avisos, 0 erros |
| Pacotes vulneráveis | nenhum nos 6 projetos |
| Testes sem banco | 165 de 165 (88 unitários, 65 de integração, 12 da ferramenta) |
| Testes com PostgreSQL e Oracle reais | 10 de 10 |
| Migração | repetível nos dois bancos |
| Carga | 3.516 impedimentos; PostgreSQL 0,6 s, Oracle 1,0 s; `comparar`: iguais |
| `testar-api` (8 casos reais: impedido, formatado, vários convênios, fora do CEPIM, CPF, inválido, sem documento, sem chave) | 8 de 8 com PostgreSQL e 8 de 8 com Oracle |
| Docker | imagem 258 MB, usuário 1654, hadolint só com informação, trivy HIGH/CRITICAL = 0; container com PostgreSQL e com Oracle: `ready` 200 e chamada real 200; com o banco fora: `ready` 503 e `503 banco_indisponivel` sem cair |
| Kubernetes | kustomize e kubeconform: 6 recursos válidos; **não aplicado em cluster** |
| Layout do CSV | 3.516 linhas, 1.934 CNPJ, 5 colunas, sem campos vazios, todos os CNPJ com 14 dígitos |

## 4. Achado que vale para as outras APIs
O Oracle guardava `carregado_em` como `TIMESTAMP` sem fuso; num container (UTC) a API mostrava a hora 3 horas errada (21:30 em vez de 00:30).
Corrigido aqui com `TIMESTAMP WITH TIME ZONE` e leitura como `DateTimeOffset`. **A mesma coluna existe na `DotNet_2` e nas APIs anteriores**: não mexi nelas.
