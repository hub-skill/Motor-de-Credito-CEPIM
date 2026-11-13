-- =============================================================================
-- Motor de Crédito CEPIM: lista de entidades impedidas, PostgreSQL 15+
-- Executado pela ferramenta (dotnet run --project src/MotorCredito.Cepim.Carga -- migrar) como administrador, no banco
-- motor_credito. <schema> = credito_cepim e <usuario_app> = motor_app_cepim são trocados pela ferramenta.
-- Pode ser executado de novo: o que existe é mantido. A aplicação só LÊ esta tabela (select).
-- =============================================================================

create schema if not exists <schema>;
comment on schema <schema> is 'Motor de crédito CEPIM: entidades impedidas (CGU) e movimentação da API.';

-- Sem restrição de unicidade: o arquivo da CGU não traz chave garantida (a mesma entidade tem vários convênios).
create table if not exists <schema>.impedimentos (
    id                  bigint generated always as identity,
    cnpj                varchar(20)   not null,
    nome_entidade       varchar(300)  not null,
    numero_convenio     varchar(30),
    orgao_concedente    varchar(300),
    motivo_impedimento  varchar(300),
    arquivo_origem      varchar(60)   not null,
    origem_carga        varchar(10)   not null default 'CEPIM',
    carregado_em        timestamptz   not null default now(),

    constraint pk_impedimentos primary key (id)
);

create index if not exists idx_impedimentos_cnpj on <schema>.impedimentos (cnpj);

comment on table  <schema>.impedimentos                    is 'CEPIM: entidades privadas sem fins lucrativos impedidas de receber repasses federais (CGU/Portal da Transparência). Recarregada a cada arquivo diário. Todo registro listado é um impedimento em vigor.';
comment on column <schema>.impedimentos.id                 is 'Identificador interno.';
comment on column <schema>.impedimentos.cnpj               is 'CNPJ da entidade, só dígitos e letras maiúsculas (como veio no arquivo, sem pontuação).';
comment on column <schema>.impedimentos.nome_entidade      is 'Nome da entidade.';
comment on column <schema>.impedimentos.numero_convenio    is 'Número do convênio que originou o impedimento.';
comment on column <schema>.impedimentos.orgao_concedente   is 'Órgão concedente do convênio.';
comment on column <schema>.impedimentos.motivo_impedimento is 'Motivo do impedimento informado pelo órgão.';
comment on column <schema>.impedimentos.arquivo_origem     is 'Nome do arquivo de onde a linha foi carregada (ex.: 20261111_CEPIM.csv).';
comment on column <schema>.impedimentos.origem_carga       is 'Aplicação que gravou a linha (CEPIM).';
comment on column <schema>.impedimentos.carregado_em       is 'Momento da carga.';

-- Acesso da aplicação: só leitura (sem criar objetos, sem gravar).
grant usage on schema <schema> to <usuario_app>;
grant select on <schema>.impedimentos to <usuario_app>;
