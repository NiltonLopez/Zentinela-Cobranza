-- ============================================================================
-- Migracion: limpiar IDs de Cartera a partir de 9004   [APLICADA 2026-06-12]
--
-- Contexto: desde la cartera 26 (LUMA) los IDs saltaron a 9004..9014 (insertados
-- manualmente con IDs externos; Cartera.ID NO es IDENTITY). Se normaliza:
--   - Se BORRAN 3 carteras internacionales vacias (sin ninguna referencia):
--       9004 DELMAR BOLIVIA, 9005 DELMAR ECUADOR, 9006 DELMAR GUATEMALA
--   - Se RENUMERAN 9007..9014 -> 27..34 (offset constante -8980). Los IDs 27..34
--     no existian, por lo que no hay colisiones en ninguna PK/indice.
--
-- Cadena de FKs que cuelga de Cartera (mapeada con sys.foreign_keys):
--   Cartera(ID) <- Lider_Cartera(CarteraID), Pago(CarteraID)
--   Lider_Cartera(LiderID,CarteraID) <- Cartera_Agente, Meta_Lider_Cartera_Mes,
--                                        Titular_Cartera, IVREnviados, MensajesEnviados
--   HistoricoEstadoTitular, UltimoEstadoTitular: CarteraID informal (sin FK).
--
-- Tablas con filas afectadas (las demas tenian 0 filas en el rango):
--   Cartera 8 | Lider_Cartera 9 | Cartera_Agente 20 | Meta 4
--   Titular_Cartera 315657 | HistoricoEstadoTitular 100480 | UltimoEstadoTitular 5215
--   (Pago: 0 filas en el rango)
--
-- EXCLUIDAS a proposito: tablas de respaldo *_bak / *_BAK_* (snapshots historicos).
--
-- METODO REAL DE EJECUCION: se aplico via MCP (solo DML, sin ALTER) con la tecnica
--   "insertar-nuevas -> repuntar hijos -> borrar-viejas", manteniendo las FKs
--   ACTIVAS todo el tiempo (la integridad la garantizan las propias FKs). Las dos
--   tablas grandes se actualizaron en lotes (UPDATE TOP) por el limite de 15s del MCP.
--   Este archivo deja el script equivalente, idempotente para referencia/replica.
--
-- REVERSION (si hiciera falta):
--   Re-insertar 9004-9006 (estaban vacias):
--     INSERT INTO dbo.Cartera (ID,NombreCartera,Activa,NombreArchivo) VALUES
--       (9004,'DELMAR BOLIVIA',0,NULL),(9005,'DELMAR ECUADOR',0,NULL),(9006,'DELMAR GUATEMALA',0,NULL);
--   Deshacer renumerado: en cada tabla SET CarteraID = CarteraID + 8980 WHERE CarteraID BETWEEN 27 AND 34
--   (y Cartera: SET ID = ID + 8980 WHERE ID BETWEEN 27 AND 34), respetando el mismo
--   orden insertar-nuevas -> repuntar -> borrar-viejas.
-- ============================================================================

-- 1. Insertar carteras nuevas 27..34 (copia de 9007..9014)
INSERT INTO dbo.Cartera (ID, NombreCartera, Activa, NombreArchivo)
SELECT ID - 8980, NombreCartera, Activa, NombreArchivo FROM dbo.Cartera WHERE ID >= 9007;

-- 2. Insertar Lider_Cartera nuevas (LiderID, 27..34) copia de (LiderID, 9007..9014)
INSERT INTO dbo.Lider_Cartera (LiderID, CarteraID, EsExterna, Activo)
SELECT LiderID, CarteraID - 8980, EsExterna, Activo FROM dbo.Lider_Cartera WHERE CarteraID >= 9007;

-- 3. Repuntar hijos de Lider_Cartera (FK satisfecha: las filas nuevas ya existen)
UPDATE dbo.Cartera_Agente         SET CarteraID = CarteraID - 8980 WHERE CarteraID >= 9007;
UPDATE dbo.Meta_Lider_Cartera_Mes SET CarteraID = CarteraID - 8980 WHERE CarteraID >= 9007;
UPDATE dbo.UltimoEstadoTitular    SET CarteraID = CarteraID - 8980 WHERE CarteraID >= 9007;
-- Tablas grandes: ejecutadas en lotes via MCP. Equivalente en bloque:
UPDATE dbo.Titular_Cartera        SET CarteraID = CarteraID - 8980 WHERE CarteraID >= 9007;
UPDATE dbo.HistoricoEstadoTitular SET CarteraID = CarteraID - 8980 WHERE CarteraID >= 9007;

-- 4. Borrar Lider_Cartera viejas (ya nadie las referencia)
DELETE FROM dbo.Lider_Cartera WHERE CarteraID >= 9007;

-- 5. Borrar carteras viejas: 9007..9014 (renumeradas) + 9004..9006 (internacionales vacias)
DELETE FROM dbo.Cartera WHERE ID >= 9004;

-- 6. Verificacion: 0 referencias >= 9004 y sin huerfanos
--   (validado 2026-06-12: todos los conteos en 0, orphan_LC_sinCartera = 0)
