-- =============================================================================
-- Motor de Crédito CEPIM: lista de entidades impedidas, Oracle 21c (banco plugável XEPDB1)
-- Executado pela ferramenta (dotnet run --project src/MotorCredito.Cepim.Carga -- migrar) como administrador.
-- <dono> = MOTOR_CREDITO_CEPIM (sem login) e <usuario_app> = MOTOR_APP_CEPIM são trocados pela ferramenta.
-- Cada comando termina numa linha com "/". Reexecutar é seguro: "já existe" é ignorado.
-- A aplicação só LÊ esta tabela (select), pelo sinônimo.
-- =============================================================================

CREATE TABLE <dono>.impedimentos (
    id                  NUMBER GENERATED ALWAYS AS IDENTITY,
    cnpj                VARCHAR2(20)   NOT NULL,
    nome_entidade       VARCHAR2(300)  NOT NULL,
    numero_convenio     VARCHAR2(30),
    orgao_concedente    VARCHAR2(300),
    motivo_impedimento  VARCHAR2(300),
    arquivo_origem      VARCHAR2(60)   NOT NULL,
    origem_carga        VARCHAR2(10)   DEFAULT 'CEPIM' NOT NULL,
    carregado_em        TIMESTAMP WITH TIME ZONE DEFAULT SYSTIMESTAMP NOT NULL,

    CONSTRAINT pk_impedimentos PRIMARY KEY (id)
)
/
CREATE INDEX <dono>.idx_impedimentos_cnpj ON <dono>.impedimentos (cnpj)
/
COMMENT ON TABLE <dono>.impedimentos IS 'CEPIM: entidades privadas sem fins lucrativos impedidas de receber repasses federais (CGU/Portal da Transparência). Recarregada a cada arquivo diário. Todo registro listado é um impedimento em vigor.'
/
COMMENT ON COLUMN <dono>.impedimentos.id IS 'Identificador interno.'
/
COMMENT ON COLUMN <dono>.impedimentos.cnpj IS 'CNPJ da entidade, só dígitos e letras maiúsculas (como veio no arquivo, sem pontuação).'
/
COMMENT ON COLUMN <dono>.impedimentos.nome_entidade IS 'Nome da entidade.'
/
COMMENT ON COLUMN <dono>.impedimentos.numero_convenio IS 'Número do convênio que originou o impedimento.'
/
COMMENT ON COLUMN <dono>.impedimentos.orgao_concedente IS 'Órgão concedente do convênio.'
/
COMMENT ON COLUMN <dono>.impedimentos.motivo_impedimento IS 'Motivo do impedimento informado pelo órgão.'
/
COMMENT ON COLUMN <dono>.impedimentos.arquivo_origem IS 'Nome do arquivo de onde a linha foi carregada (ex.: 20261111_CEPIM.csv).'
/
COMMENT ON COLUMN <dono>.impedimentos.origem_carga IS 'Aplicação que gravou a linha (CEPIM).'
/
COMMENT ON COLUMN <dono>.impedimentos.carregado_em IS 'Momento da carga.'
/
GRANT SELECT ON <dono>.impedimentos TO <usuario_app>
/
CREATE OR REPLACE SYNONYM <usuario_app>.impedimentos FOR <dono>.impedimentos
/
