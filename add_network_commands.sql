-- ============================================================================
-- Adicionar novos tipos de comando para Network Commands (Suntech Protocol)
-- Execute no PostgreSQL: psql -h 127.0.0.1 -U blt_aplicacao -d blt_rastro_pg -f add_network_commands.sql
-- ============================================================================

-- Verificar schema (localize ou public)
DO $$
DECLARE
    schema_name TEXT;
BEGIN
    -- Detectar qual schema contém a tabela tipo_comando
    SELECT table_schema INTO schema_name
    FROM information_schema.tables
    WHERE table_name = 'tipo_comando'
    LIMIT 1;

    IF schema_name IS NULL THEN
        RAISE EXCEPTION 'Tabela tipo_comando não encontrada';
    END IF;

    RAISE NOTICE 'Usando schema: %', schema_name;

    -- Inserir novos tipos de comando se não existirem
    EXECUTE format('
        INSERT INTO %I.tipo_comando (id, descricao) VALUES
        (5, ''Check-In Maintenance Server''),
        (6, ''Request IMSI''),
        (7, ''Request ICCID''),
        (8, ''Check Network Type''),
        (9, ''Request Phone Number'')
        ON CONFLICT (id) DO UPDATE SET descricao = EXCLUDED.descricao;
    ', schema_name);

    RAISE NOTICE 'Novos tipos de comando adicionados com sucesso!';

    -- Exibir todos os tipos de comando
    EXECUTE format('SELECT id, descricao FROM %I.tipo_comando ORDER BY id', schema_name);
END $$;
