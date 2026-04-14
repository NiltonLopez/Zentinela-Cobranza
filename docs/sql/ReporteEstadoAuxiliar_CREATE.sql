-- ============================================================
-- Tabla staging unificada para cargue de reportes.
-- Usada tanto por la UI (Zentinela) como por SSIS.
-- Sin PKs ni FKs — landing zone sin restricciones.
-- Ejecutar en: DBCallCenter
-- ============================================================
IF OBJECT_ID('ReporteEstadoAuxiliar', 'U') IS NULL
BEGIN
    CREATE TABLE ReporteEstadoAuxiliar (
        TitularID          bigint       NULL,
        Nombre             varchar(70)  NULL,   -- para insertar titulares nuevos
        CarteraID          int          NULL,   -- inyectado por la UI o SSIS
        AgenteID           int          NULL,
        UltimoPerfil       varchar(50)  NULL,
        MejorPerfil        varchar(50)  NULL,
        FechaMejorPerfil   date         NULL,
        SaldoTotal         money        NULL,
        NumeroMarcado      varchar(10)  NULL,
        Rotacion           int          NULL,
        FechaUltimaGestion date         NULL,
        FechaPromesa       date         NULL,
        ValorPromesa       money        NULL,
        MotivoNoPago       varchar(100) NULL,
        FechaActualizacion datetime     NULL
    );
END;
