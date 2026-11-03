/* =====================================================================================
   PROYECTO      : Sistema web de compra y reserva de pasajes - Aerolínea VIP
   ARCHIVO       : Script_BD_AerolineaVIP.sql
   MOTOR         : Microsoft SQL Server 2016 o superior
   ORIGEN        : Integra en un solo archivo la estructura de la versión anterior
                   de Script_BD_AerolineaVIP.sql y los datos de Script_DATA.sql,
                   más el módulo de pagos simulados.

   =====================================================================================
   BASES DE DATOS QUE CREA ESTE SCRIPT (ORDEN DE EJECUCIÓN)
   =====================================================================================
   PARTE A  ->  AerolineaVIP           Base de datos de DOMINIO (vuelos, reservas,
                                       pasajeros, pagos simulados, comprobantes).
   PARTE B  ->  AerolineaVIP_Identity  Base de datos INDEPENDIENTE para ASP.NET Identity
                                       (usuarios, roles, claims y logins).

   Ejecute el archivo completo (F5) en SSMS. Las partes se ejecutan en orden: primero A
   y luego B. Cada parte cambia de base de datos con USE, por lo que no se mezclan las
   tablas de identidad con las tablas funcionales.

   =====================================================================================
   INSTRUCCIONES DE EJECUCIÓN
   =====================================================================================
   1. Abra este archivo en SQL Server Management Studio con un usuario que pueda crear
      bases de datos.
   2. Ejecute todo el script (F5). Es re-ejecutable:
        - PARTE A elimina y vuelve a crear solo los objetos de dominio de este script,
          por lo que los datos iniciales no se duplican.
        - PARTE B crea la base y las tablas de Identity únicamente si no existen; nunca
          borra usuarios ni roles ya creados.
   3. Revise la pestaña "Mensajes": todas las líneas de verificación deben iniciar con OK.
   4. En la aplicación ASP.NET use dos cadenas de conexión:
        - DefaultConnection (Identity) -> Initial Catalog=AerolineaVIP_Identity
        - AerolineaVIPEntities (dominio) -> Initial Catalog=AerolineaVIP
   5. Los roles, el superusuario, el administrador y los usuarios iniciales se crean
      MEDIANTE CÓDIGO (Global.asax.cs o el punto de inicialización de Identity), no con
      este script, porque las contraseñas deben generarse con el hash de Identity.

   =====================================================================================
   REGISTRO DE CAMBIOS RESPECTO A LA VERSIÓN ANTERIOR
   =====================================================================================
   + Unificación de estructura y datos en un solo archivo (sin los DELETE de
     Script_DATA, innecesarios porque la estructura se crea desde cero).
   + Nueva base AerolineaVIP_Identity con las tablas estándar de ASP.NET Identity 2.x.
   + Tabla Asiento: distribución de los 7 asientos del Embraer Phenom 300E, basada en el
     dibujo de cabina del documento "Características del proyecto" (propuesta DP-11).
     La vista vw_AsientoAeronave muestra los 7 asientos de cada una de las 3 aeronaves.
   + Vista vw_EstadoAsientoVuelo: estado visual de cada asiento por vuelo
     (Disponible = verde, Reservado = morado, Ocupado = rojo).
   + Módulo de pagos simulados: tablas MetodoPago y MarcaTarjeta (4 marcas) y nuevas
     columnas en Pago (método, marca, últimos 4 dígitos ficticios, titular ficticio,
     código de operación simulada, indicador EsSimulado, mensaje de resultado).
     La columna Pago.ReferenciaExterna se reemplaza por Pago.CodigoOperacion porque no
     existe una pasarela externa.
   + Ruta.TarifaReferencial y ReservaPasajeroVuelo.ImporteTramo: valores de EJEMPLO
     para calcular importes y el detalle del comprobante (tarifas reales: DP-04).
   + Tabla DatosEmisor y vista vw_ComprobanteDetalle: datos necesarios para que la
     aplicación genere el comprobante académico en PDF.
   + Numeración de comprobantes de ejemplo cambiada de "001-001-000000001" a
     "ACAD-000001" para no imitar el formato de un documento tributario autorizado.
   - Se eliminan las columnas ROWVERSION (VersionFila): la concurrencia avanzada está
     fuera del alcance de la primera entrega. Las restricciones de integridad (índices
     únicos, CHECK y triggers de reglas de negocio) se conservan.
   - Se elimina el bloque de pruebas automáticas del script original: la fase de
     pruebas no es entregable en esta etapa. Se reemplaza por una verificación de
     conteos al final de la PARTE A.
   * No se incluyen tablas ni funciones de auditoría.

   =====================================================================================
   ADVERTENCIAS ACADÉMICAS
   =====================================================================================
   * Los pagos son SIMULADOS. No se almacenan números completos de tarjeta, códigos de
     seguridad, fechas de vencimiento ni credenciales financieras. Solo se guardan la
     marca, los últimos 4 dígitos ficticios y un código de operación simulada.
   * Las 4 marcas de tarjeta (Visa, Mastercard, American Express, Diners Club) son una
     configuración académica de presentación que debe confirmarse; no implican acuerdos
     comerciales con emisores.
   * Los comprobantes son ACADÉMICOS: no son facturas tributarias ni documentos
     autorizados. La numeración y los datos fiscales son de ejemplo (DP-05).
   * Aeropuerto de Galápagos (GPS) = supuesto pendiente de decisión (DP-01).
   * Asignación de aeronaves en los datos = EJEMPLO para demostración, no definitiva
     (DP-02).
   * Pasajeros, usuarios y correos son ficticios.
===================================================================================== */

SET NOCOUNT ON;
SET ANSI_NULLS ON;
SET QUOTED_IDENTIFIER ON;
SET ANSI_PADDING ON;
SET ANSI_WARNINGS ON;
SET ARITHABORT ON;
SET CONCAT_NULL_YIELDS_NULL ON;
SET NUMERIC_ROUNDABORT OFF;
GO

/* #####################################################################################
   PARTE A. BASE DE DATOS DE DOMINIO: AerolineaVIP
##################################################################################### */

/* =====================================================================================
   A0. CREACIÓN DE LA BASE DE DATOS
===================================================================================== */
IF DB_ID(N'AerolineaVIP') IS NULL
    CREATE DATABASE AerolineaVIP;
GO

USE AerolineaVIP;
GO

/* =====================================================================================
   A1. ELIMINACIÓN DE OBJETOS PREVIOS (permite re-ejecutar sin duplicar datos)
===================================================================================== */
DROP PROCEDURE IF EXISTS dbo.usp_GenerarVuelos;
DROP VIEW      IF EXISTS dbo.vw_ComprobanteDetalle;
DROP VIEW      IF EXISTS dbo.vw_EstadoAsientoVuelo;
DROP VIEW      IF EXISTS dbo.vw_AsientoAeronave;
DROP VIEW      IF EXISTS dbo.vw_DisponibilidadVuelo;
DROP VIEW      IF EXISTS dbo.vw_VueloDetalle;

DROP TABLE IF EXISTS dbo.Comprobante;
DROP TABLE IF EXISTS dbo.CheckIn;
DROP TABLE IF EXISTS dbo.Pago;
DROP TABLE IF EXISTS dbo.Compra;
DROP TABLE IF EXISTS dbo.ReservaPasajeroVuelo;
DROP TABLE IF EXISTS dbo.Reserva;
DROP TABLE IF EXISTS dbo.Pasajero;
DROP TABLE IF EXISTS dbo.TramoItinerario;
DROP TABLE IF EXISTS dbo.Itinerario;
DROP TABLE IF EXISTS dbo.Vuelo;
DROP TABLE IF EXISTS dbo.Aeronave;
DROP TABLE IF EXISTS dbo.Asiento;
DROP TABLE IF EXISTS dbo.ModeloAeronave;
DROP TABLE IF EXISTS dbo.Frecuencia;
DROP TABLE IF EXISTS dbo.Ruta;
DROP TABLE IF EXISTS dbo.Aeropuerto;
DROP TABLE IF EXISTS dbo.DatosEmisor;

DROP TABLE IF EXISTS dbo.MarcaTarjeta;
DROP TABLE IF EXISTS dbo.MetodoPago;
DROP TABLE IF EXISTS dbo.EstadoComprobante;
DROP TABLE IF EXISTS dbo.TipoComprobante;
DROP TABLE IF EXISTS dbo.EstadoCheckIn;
DROP TABLE IF EXISTS dbo.EstadoPago;
DROP TABLE IF EXISTS dbo.EstadoCompra;
DROP TABLE IF EXISTS dbo.Moneda;
DROP TABLE IF EXISTS dbo.EstadoAsignacion;
DROP TABLE IF EXISTS dbo.EstadoReserva;
DROP TABLE IF EXISTS dbo.TipoDocumentoIdentidad;
DROP TABLE IF EXISTS dbo.EstadoItinerario;
DROP TABLE IF EXISTS dbo.EstadoVuelo;
DROP TABLE IF EXISTS dbo.EstadoAeronave;
GO

/* =====================================================================================
   A2. TABLAS DE CATÁLOGO (código natural como clave primaria)
===================================================================================== */
CREATE TABLE dbo.EstadoAeronave (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoAeronave PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.EstadoVuelo (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoVuelo PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.EstadoItinerario (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoItinerario PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.TipoDocumentoIdentidad (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_TipoDocumentoIdentidad PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.EstadoReserva (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoReserva PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

-- Estado de un asiento dentro de una reserva.
-- PENDIENTE (Reservado, morado) y CONFIRMADA (Ocupado, rojo) ocupan inventario.
-- CANCELADA y LIBERADA no ocupan inventario (el asiento vuelve a estar Disponible, verde).
CREATE TABLE dbo.EstadoAsignacion (
    Codigo           VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoAsignacion PRIMARY KEY,
    Descripcion      NVARCHAR(100) NOT NULL,
    OcupaInventario  BIT           NOT NULL,
    EstadoVisual     VARCHAR(20)   NOT NULL,
    CONSTRAINT CK_EstadoAsignacion_Visual CHECK (EstadoVisual IN ('DISPONIBLE', 'RESERVADO', 'OCUPADO'))
);

CREATE TABLE dbo.Moneda (
    Codigo       CHAR(3)       NOT NULL CONSTRAINT PK_Moneda PRIMARY KEY,   -- ISO 4217
    Descripcion  NVARCHAR(100) NOT NULL,
    CONSTRAINT CK_Moneda_Codigo CHECK (Codigo LIKE '[A-Z][A-Z][A-Z]')
);

CREATE TABLE dbo.EstadoCompra (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoCompra PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.EstadoPago (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoPago PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.EstadoCheckIn (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoCheckIn PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

CREATE TABLE dbo.TipoComprobante (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_TipoComprobante PRIMARY KEY,
    Descripcion  NVARCHAR(150) NOT NULL
);

CREATE TABLE dbo.EstadoComprobante (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_EstadoComprobante PRIMARY KEY,
    Descripcion  NVARCHAR(100) NOT NULL
);

-- NUEVO. Métodos de pago simulados que el usuario puede elegir.
CREATE TABLE dbo.MetodoPago (
    Codigo       VARCHAR(20)   NOT NULL CONSTRAINT PK_MetodoPago PRIMARY KEY,
    Nombre       NVARCHAR(100) NOT NULL,
    Activo       BIT           NOT NULL CONSTRAINT DF_MetodoPago_Activo DEFAULT (1)
);

-- NUEVO. Marcas de tarjeta de la simulación (configuración académica a confirmar).
-- Las longitudes sirven SOLO para validar el formato en la interfaz (jQuery y servidor).
-- RutaLogo apunta al recurso gráfico que cada grupo incorpore en el proyecto.
CREATE TABLE dbo.MarcaTarjeta (
    Codigo                   VARCHAR(20)   NOT NULL CONSTRAINT PK_MarcaTarjeta PRIMARY KEY,
    Nombre                   NVARCHAR(60)  NOT NULL,
    LongitudNumero           TINYINT       NOT NULL,
    LongitudCodigoSeguridad  TINYINT       NOT NULL,
    PrefijosReferencia       VARCHAR(40)   NOT NULL,
    RutaLogo                 VARCHAR(150)  NOT NULL,
    Activa                   BIT           NOT NULL CONSTRAINT DF_MarcaTarjeta_Activa DEFAULT (1),
    CONSTRAINT UQ_MarcaTarjeta_Nombre UNIQUE (Nombre),
    CONSTRAINT CK_MarcaTarjeta_LongitudNumero CHECK (LongitudNumero BETWEEN 12 AND 19),
    CONSTRAINT CK_MarcaTarjeta_LongitudCodigo CHECK (LongitudCodigoSeguridad BETWEEN 3 AND 4)
);
GO

/* =====================================================================================
   A3. OPERACIÓN AÉREA
===================================================================================== */

-- AEROPUERTO
CREATE TABLE dbo.Aeropuerto (
    AeropuertoId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Aeropuerto PRIMARY KEY,
    CodigoIATA    CHAR(3)       NOT NULL,
    Nombre        NVARCHAR(150) NOT NULL,
    Localidad     NVARCHAR(100) NOT NULL,
    Activo        BIT           NOT NULL CONSTRAINT DF_Aeropuerto_Activo DEFAULT (1),
    CONSTRAINT UQ_Aeropuerto_CodigoIATA UNIQUE (CodigoIATA),
    CONSTRAINT CK_Aeropuerto_CodigoIATA CHECK (CodigoIATA LIKE '[A-Z][A-Z][A-Z]'),
    CONSTRAINT CK_Aeropuerto_Nombre     CHECK (LEN(LTRIM(RTRIM(Nombre))) > 0)
);

-- RUTA. TarifaReferencial: valor de EJEMPLO por pasajero y tramo (DP-04 pendiente).
CREATE TABLE dbo.Ruta (
    RutaId               INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Ruta PRIMARY KEY,
    AeropuertoOrigenId   INT           NOT NULL,
    AeropuertoDestinoId  INT           NOT NULL,
    TarifaReferencial    DECIMAL(12,2) NOT NULL,
    Activa               BIT           NOT NULL CONSTRAINT DF_Ruta_Activa DEFAULT (1),
    CONSTRAINT FK_Ruta_Origen  FOREIGN KEY (AeropuertoOrigenId)  REFERENCES dbo.Aeropuerto (AeropuertoId),
    CONSTRAINT FK_Ruta_Destino FOREIGN KEY (AeropuertoDestinoId) REFERENCES dbo.Aeropuerto (AeropuertoId),
    CONSTRAINT UQ_Ruta_OrigenDestino UNIQUE (AeropuertoOrigenId, AeropuertoDestinoId),
    CONSTRAINT CK_Ruta_OrigenDistintoDestino CHECK (AeropuertoOrigenId <> AeropuertoDestinoId),
    CONSTRAINT CK_Ruta_Tarifa CHECK (TarifaReferencial >= 0)
);

-- FRECUENCIA: día de la semana y horario aprobados. DiaSemana: 1 = lunes ... 7 = domingo.
CREATE TABLE dbo.Frecuencia (
    FrecuenciaId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Frecuencia PRIMARY KEY,
    RutaId        INT     NOT NULL,
    DiaSemana     TINYINT NOT NULL,
    HoraSalida    TIME(0) NOT NULL,
    HoraLlegada   TIME(0) NOT NULL,
    Activa        BIT     NOT NULL CONSTRAINT DF_Frecuencia_Activa DEFAULT (1),
    CONSTRAINT FK_Frecuencia_Ruta FOREIGN KEY (RutaId) REFERENCES dbo.Ruta (RutaId),
    CONSTRAINT UQ_Frecuencia UNIQUE (RutaId, DiaSemana, HoraSalida),
    CONSTRAINT CK_Frecuencia_DiaSemana CHECK (DiaSemana BETWEEN 1 AND 7),
    CONSTRAINT CK_Frecuencia_Horario   CHECK (HoraLlegada > HoraSalida)
);

-- MODELO DE AERONAVE (RN-03: máximo 7 asientos).
CREATE TABLE dbo.ModeloAeronave (
    ModeloAeronaveId   INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModeloAeronave PRIMARY KEY,
    Fabricante         NVARCHAR(60) NOT NULL,
    Nombre             NVARCHAR(60) NOT NULL,
    CapacidadAsientos  TINYINT      NOT NULL,
    CONSTRAINT UQ_ModeloAeronave UNIQUE (Fabricante, Nombre),
    CONSTRAINT CK_ModeloAeronave_Capacidad CHECK (CapacidadAsientos BETWEEN 1 AND 7)
);

-- NUEVO. ASIENTO: distribución de cabina por modelo (DP-11, propuesta a confirmar).
-- Código = fila + lado (D = derecho, I = izquierdo). La fila 1 del lado izquierdo
-- corresponde a la zona de la puerta de acceso, por eso no existe el asiento 1I.
-- NumeroAsiento es el valor que se guarda en ReservaPasajeroVuelo.
CREATE TABLE dbo.Asiento (
    ModeloAeronaveId  INT           NOT NULL,
    NumeroAsiento     TINYINT       NOT NULL,
    Codigo            CHAR(2)       NOT NULL,
    Fila              TINYINT       NOT NULL,
    Lado              CHAR(1)       NOT NULL,
    Ubicacion         NVARCHAR(100) NOT NULL,
    CONSTRAINT PK_Asiento PRIMARY KEY (ModeloAeronaveId, NumeroAsiento),
    CONSTRAINT FK_Asiento_Modelo FOREIGN KEY (ModeloAeronaveId) REFERENCES dbo.ModeloAeronave (ModeloAeronaveId),
    CONSTRAINT UQ_Asiento_Codigo UNIQUE (ModeloAeronaveId, Codigo),
    CONSTRAINT UQ_Asiento_Posicion UNIQUE (ModeloAeronaveId, Fila, Lado),
    CONSTRAINT CK_Asiento_Numero CHECK (NumeroAsiento BETWEEN 1 AND 7),
    CONSTRAINT CK_Asiento_Lado   CHECK (Lado IN ('D', 'I')),
    CONSTRAINT CK_Asiento_Fila   CHECK (Fila BETWEEN 1 AND 4)
);

-- AERONAVE
CREATE TABLE dbo.Aeronave (
    AeronaveId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Aeronave PRIMARY KEY,
    Matricula             VARCHAR(15) NOT NULL,
    ModeloAeronaveId      INT         NOT NULL,
    EstadoAeronaveCodigo  VARCHAR(20) NOT NULL CONSTRAINT DF_Aeronave_Estado DEFAULT ('DISPONIBLE'),
    CONSTRAINT UQ_Aeronave_Matricula UNIQUE (Matricula),
    CONSTRAINT FK_Aeronave_Modelo FOREIGN KEY (ModeloAeronaveId)     REFERENCES dbo.ModeloAeronave (ModeloAeronaveId),
    CONSTRAINT FK_Aeronave_Estado FOREIGN KEY (EstadoAeronaveCodigo) REFERENCES dbo.EstadoAeronave (Codigo),
    CONSTRAINT CK_Aeronave_Matricula CHECK (LEN(LTRIM(RTRIM(Matricula))) > 0)
);

-- VUELO: una frecuencia en una fecha concreta. Ruta y horas se obtienen de Frecuencia.
CREATE TABLE dbo.Vuelo (
    VueloId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Vuelo PRIMARY KEY,
    FrecuenciaId       INT         NOT NULL,
    FechaOperacion     DATE        NOT NULL,
    AeronaveId         INT         NULL,
    EstadoVueloCodigo  VARCHAR(20) NOT NULL CONSTRAINT DF_Vuelo_Estado DEFAULT ('PROGRAMADO'),
    CONSTRAINT FK_Vuelo_Frecuencia FOREIGN KEY (FrecuenciaId)      REFERENCES dbo.Frecuencia (FrecuenciaId),
    CONSTRAINT FK_Vuelo_Aeronave   FOREIGN KEY (AeronaveId)        REFERENCES dbo.Aeronave (AeronaveId),
    CONSTRAINT FK_Vuelo_Estado     FOREIGN KEY (EstadoVueloCodigo) REFERENCES dbo.EstadoVuelo (Codigo),
    CONSTRAINT UQ_Vuelo_FrecuenciaFecha UNIQUE (FrecuenciaId, FechaOperacion)
);
CREATE INDEX IX_Vuelo_Fecha    ON dbo.Vuelo (FechaOperacion);
CREATE INDEX IX_Vuelo_Aeronave ON dbo.Vuelo (AeronaveId, FechaOperacion) WHERE AeronaveId IS NOT NULL;
GO

/* =====================================================================================
   A4. ITINERARIOS, PASAJEROS Y RESERVAS
===================================================================================== */
CREATE TABLE dbo.Itinerario (
    ItinerarioId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Itinerario PRIMARY KEY,
    Referencia              VARCHAR(30)   NOT NULL,
    Nombre                  NVARCHAR(150) NOT NULL,
    EstadoItinerarioCodigo  VARCHAR(20)   NOT NULL CONSTRAINT DF_Itinerario_Estado DEFAULT ('ACTIVO'),
    CONSTRAINT UQ_Itinerario_Referencia UNIQUE (Referencia),
    CONSTRAINT FK_Itinerario_Estado FOREIGN KEY (EstadoItinerarioCodigo) REFERENCES dbo.EstadoItinerario (Codigo)
);

CREATE TABLE dbo.TramoItinerario (
    TramoItinerarioId  INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_TramoItinerario PRIMARY KEY,
    ItinerarioId       INT     NOT NULL,
    VueloId            INT     NOT NULL,
    Orden              TINYINT NOT NULL,
    CONSTRAINT FK_Tramo_Itinerario FOREIGN KEY (ItinerarioId) REFERENCES dbo.Itinerario (ItinerarioId),
    CONSTRAINT FK_Tramo_Vuelo      FOREIGN KEY (VueloId)      REFERENCES dbo.Vuelo (VueloId),
    CONSTRAINT UQ_Tramo_Orden UNIQUE (ItinerarioId, Orden),
    CONSTRAINT UQ_Tramo_Vuelo UNIQUE (ItinerarioId, VueloId),
    CONSTRAINT CK_Tramo_Orden CHECK (Orden >= 1)
);
CREATE INDEX IX_Tramo_Vuelo ON dbo.TramoItinerario (VueloId);

-- PASAJERO: persona que viaja (distinta del usuario de Identity que gestiona la reserva).
CREATE TABLE dbo.Pasajero (
    PasajeroId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pasajero PRIMARY KEY,
    TipoDocumentoCodigo   VARCHAR(20)   NOT NULL,
    NumeroDocumento       VARCHAR(20)   NOT NULL,
    Nombres               NVARCHAR(100) NOT NULL,
    Apellidos             NVARCHAR(100) NOT NULL,
    FechaNacimiento       DATE          NULL,
    Correo                NVARCHAR(256) NULL,
    Telefono              VARCHAR(20)   NULL,
    FechaRegistro         DATETIME2(0)  NOT NULL CONSTRAINT DF_Pasajero_FechaRegistro DEFAULT (SYSDATETIME()),
    CONSTRAINT FK_Pasajero_TipoDocumento FOREIGN KEY (TipoDocumentoCodigo) REFERENCES dbo.TipoDocumentoIdentidad (Codigo),
    CONSTRAINT UQ_Pasajero_Documento UNIQUE (TipoDocumentoCodigo, NumeroDocumento),
    CONSTRAINT CK_Pasajero_NumeroDocumento CHECK (LEN(NumeroDocumento) >= 5),
    CONSTRAINT CK_Pasajero_Nombres   CHECK (LEN(LTRIM(RTRIM(Nombres)))   > 0),
    CONSTRAINT CK_Pasajero_Apellidos CHECK (LEN(LTRIM(RTRIM(Apellidos))) > 0),
    CONSTRAINT CK_Pasajero_Correo    CHECK (Correo IS NULL OR Correo LIKE '%_@_%._%'),
    CONSTRAINT CK_Pasajero_Telefono  CHECK (Telefono IS NULL OR Telefono NOT LIKE '%[^0-9+ ]%'),
    CONSTRAINT CK_Pasajero_FechaNacimiento CHECK (FechaNacimiento IS NULL OR FechaNacimiento > '19000101')
);

-- RESERVA. UsuarioId = Id de AspNetUsers en la base AerolineaVIP_Identity.
-- Al estar en otra base de datos no puede existir una FK física: la aplicación valida
-- que el usuario exista antes de registrar la reserva.
CREATE TABLE dbo.Reserva (
    ReservaId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Reserva PRIMARY KEY,
    CodigoReserva         AS ('RV' + RIGHT('00000000' + CONVERT(VARCHAR(10), ReservaId), 8)) PERSISTED,
    UsuarioId             NVARCHAR(128) NOT NULL,
    FechaCreacion         DATETIME2(0)  NOT NULL CONSTRAINT DF_Reserva_FechaCreacion DEFAULT (SYSDATETIME()),
    FechaExpiracion       DATETIME2(0)  NULL,     -- periodo de retención (DP-07 pendiente)
    EstadoReservaCodigo   VARCHAR(20)   NOT NULL CONSTRAINT DF_Reserva_Estado DEFAULT ('PENDIENTE'),
    CONSTRAINT FK_Reserva_Estado FOREIGN KEY (EstadoReservaCodigo) REFERENCES dbo.EstadoReserva (Codigo),
    CONSTRAINT UQ_Reserva_Codigo UNIQUE (CodigoReserva),
    CONSTRAINT CK_Reserva_UsuarioId  CHECK (LEN(UsuarioId) > 0),
    CONSTRAINT CK_Reserva_Expiracion CHECK (FechaExpiracion IS NULL OR FechaExpiracion > FechaCreacion)
);
CREATE INDEX IX_Reserva_Usuario ON dbo.Reserva (UsuarioId);

-- RESERVA_PASAJERO_VUELO: asiento de un pasajero en un vuelo de una reserva.
-- ImporteTramo: valor cobrado por ese tramo (copiado de la tarifa al momento de reservar).
CREATE TABLE dbo.ReservaPasajeroVuelo (
    AsignacionId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ReservaPasajeroVuelo PRIMARY KEY,
    ReservaId               INT           NOT NULL,
    PasajeroId              INT           NOT NULL,
    VueloId                 INT           NOT NULL,
    NumeroAsiento           TINYINT       NOT NULL,
    ImporteTramo            DECIMAL(12,2) NOT NULL,
    EstadoAsignacionCodigo  VARCHAR(20)   NOT NULL CONSTRAINT DF_RPV_Estado DEFAULT ('PENDIENTE'),
    FechaAsignacion         DATETIME2(0)  NOT NULL CONSTRAINT DF_RPV_Fecha DEFAULT (SYSDATETIME()),
    CONSTRAINT FK_RPV_Reserva  FOREIGN KEY (ReservaId)  REFERENCES dbo.Reserva (ReservaId),
    CONSTRAINT FK_RPV_Pasajero FOREIGN KEY (PasajeroId) REFERENCES dbo.Pasajero (PasajeroId),
    CONSTRAINT FK_RPV_Vuelo    FOREIGN KEY (VueloId)    REFERENCES dbo.Vuelo (VueloId),
    CONSTRAINT FK_RPV_Estado   FOREIGN KEY (EstadoAsignacionCodigo) REFERENCES dbo.EstadoAsignacion (Codigo),
    CONSTRAINT CK_RPV_NumeroAsiento CHECK (NumeroAsiento BETWEEN 1 AND 7),   -- RN-03
    CONSTRAINT CK_RPV_Importe CHECK (ImporteTramo >= 0)
);

-- Un asiento no puede tener dos ocupaciones vigentes en el mismo vuelo. Junto con
-- CK_RPV_NumeroAsiento garantiza como máximo 7 ocupaciones vigentes por vuelo.
CREATE UNIQUE INDEX UX_RPV_AsientoActivo
    ON dbo.ReservaPasajeroVuelo (VueloId, NumeroAsiento)
    WHERE EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA');

-- Un pasajero no puede ocupar dos asientos vigentes en el mismo vuelo.
CREATE UNIQUE INDEX UX_RPV_PasajeroActivo
    ON dbo.ReservaPasajeroVuelo (VueloId, PasajeroId)
    WHERE EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA');

CREATE INDEX IX_RPV_Reserva  ON dbo.ReservaPasajeroVuelo (ReservaId);
CREATE INDEX IX_RPV_Pasajero ON dbo.ReservaPasajeroVuelo (PasajeroId);
GO

/* =====================================================================================
   A5. TRANSACCIONES: COMPRA, PAGO SIMULADO, CHECK-IN Y COMPROBANTE
===================================================================================== */
CREATE TABLE dbo.Compra (
    CompraId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Compra PRIMARY KEY,
    ReservaId            INT              NOT NULL,
    FechaCompra          DATETIME2(0)     NOT NULL CONSTRAINT DF_Compra_Fecha DEFAULT (SYSDATETIME()),
    Importe              DECIMAL(12,2)    NOT NULL,
    MonedaCodigo         CHAR(3)          NOT NULL CONSTRAINT DF_Compra_Moneda DEFAULT ('USD'),
    EstadoCompraCodigo   VARCHAR(20)      NOT NULL CONSTRAINT DF_Compra_Estado DEFAULT ('PENDIENTE'),
    ClaveIdempotencia    UNIQUEIDENTIFIER NOT NULL CONSTRAINT DF_Compra_Clave DEFAULT (NEWID()),
    CONSTRAINT FK_Compra_Reserva FOREIGN KEY (ReservaId)          REFERENCES dbo.Reserva (ReservaId),
    CONSTRAINT FK_Compra_Moneda  FOREIGN KEY (MonedaCodigo)       REFERENCES dbo.Moneda (Codigo),
    CONSTRAINT FK_Compra_Estado  FOREIGN KEY (EstadoCompraCodigo) REFERENCES dbo.EstadoCompra (Codigo),
    CONSTRAINT UQ_Compra_ClaveIdempotencia UNIQUE (ClaveIdempotencia),   -- RNP-10
    CONSTRAINT CK_Compra_Importe CHECK (Importe >= 0)
);
-- Una sola compra vigente (pendiente o pagada) por reserva.
CREATE UNIQUE INDEX UX_Compra_ReservaVigente
    ON dbo.Compra (ReservaId)
    WHERE EstadoCompraCodigo IN ('PENDIENTE', 'PAGADA');

-- PAGO SIMULADO. No se guarda el número completo, el código de seguridad ni la fecha de
-- vencimiento de la tarjeta. La moneda es la de la compra (no se repite: 3FN).
-- Al pulsar "Cancelar" en el modal de confirmación NO se registra ningún pago.
CREATE TABLE dbo.Pago (
    PagoId                INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Pago PRIMARY KEY,
    CompraId              INT           NOT NULL,
    FechaPago             DATETIME2(0)  NOT NULL CONSTRAINT DF_Pago_Fecha DEFAULT (SYSDATETIME()),
    Importe               DECIMAL(12,2) NOT NULL,
    EstadoPagoCodigo      VARCHAR(20)   NOT NULL CONSTRAINT DF_Pago_Estado DEFAULT ('PENDIENTE'),
    MetodoPagoCodigo      VARCHAR(20)   NOT NULL,
    MarcaTarjetaCodigo    VARCHAR(20)   NOT NULL,
    UltimosCuatroDigitos  CHAR(4)       NOT NULL,
    NombreTitular         NVARCHAR(100) NULL,
    CodigoOperacion       VARCHAR(40)   NOT NULL,
    EsSimulado            BIT           NOT NULL CONSTRAINT DF_Pago_EsSimulado DEFAULT (1),
    MensajeResultado      NVARCHAR(200) NULL,
    CONSTRAINT FK_Pago_Compra FOREIGN KEY (CompraId)           REFERENCES dbo.Compra (CompraId),
    CONSTRAINT FK_Pago_Estado FOREIGN KEY (EstadoPagoCodigo)   REFERENCES dbo.EstadoPago (Codigo),
    CONSTRAINT FK_Pago_Metodo FOREIGN KEY (MetodoPagoCodigo)   REFERENCES dbo.MetodoPago (Codigo),
    CONSTRAINT FK_Pago_Marca  FOREIGN KEY (MarcaTarjetaCodigo) REFERENCES dbo.MarcaTarjeta (Codigo),
    CONSTRAINT UQ_Pago_CodigoOperacion UNIQUE (CodigoOperacion),
    CONSTRAINT CK_Pago_Importe  CHECK (Importe > 0),
    CONSTRAINT CK_Pago_Ultimos4 CHECK (UltimosCuatroDigitos LIKE '[0-9][0-9][0-9][0-9]'),
    CONSTRAINT CK_Pago_Simulado CHECK (EsSimulado = 1)
);
-- Una compra no puede tener dos pagos aprobados (evita cobro duplicado).
CREATE UNIQUE INDEX UX_Pago_AprobadoPorCompra
    ON dbo.Pago (CompraId)
    WHERE EstadoPagoCodigo = 'APROBADO';

-- CHECK-IN: se vincula a la asignación (de ella se obtienen reserva, pasajero y vuelo).
CREATE TABLE dbo.CheckIn (
    CheckInId            INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_CheckIn PRIMARY KEY,
    AsignacionId         INT          NOT NULL,
    FechaRegistro        DATETIME2(0) NOT NULL CONSTRAINT DF_CheckIn_Fecha DEFAULT (SYSDATETIME()),
    EstadoCheckInCodigo  VARCHAR(20)  NOT NULL CONSTRAINT DF_CheckIn_Estado DEFAULT ('COMPLETADO'),
    CONSTRAINT FK_CheckIn_Asignacion FOREIGN KEY (AsignacionId)        REFERENCES dbo.ReservaPasajeroVuelo (AsignacionId),
    CONSTRAINT FK_CheckIn_Estado     FOREIGN KEY (EstadoCheckInCodigo) REFERENCES dbo.EstadoCheckIn (Codigo)
);
CREATE UNIQUE INDEX UX_CheckIn_Vigente
    ON dbo.CheckIn (AsignacionId)
    WHERE EstadoCheckInCodigo = 'COMPLETADO';

-- COMPROBANTE ACADÉMICO (no tributario).
CREATE TABLE dbo.Comprobante (
    ComprobanteId             INT IDENTITY(1,1) NOT NULL CONSTRAINT PK_Comprobante PRIMARY KEY,
    CompraId                  INT          NOT NULL,
    TipoComprobanteCodigo     VARCHAR(20)  NOT NULL,
    NumeroComprobante         VARCHAR(30)  NOT NULL,
    FechaEmision              DATETIME2(0) NOT NULL CONSTRAINT DF_Comprobante_Fecha DEFAULT (SYSDATETIME()),
    EstadoComprobanteCodigo   VARCHAR(20)  NOT NULL CONSTRAINT DF_Comprobante_Estado DEFAULT ('EMITIDO'),
    CONSTRAINT FK_Comprobante_Compra FOREIGN KEY (CompraId)                REFERENCES dbo.Compra (CompraId),
    CONSTRAINT FK_Comprobante_Tipo   FOREIGN KEY (TipoComprobanteCodigo)   REFERENCES dbo.TipoComprobante (Codigo),
    CONSTRAINT FK_Comprobante_Estado FOREIGN KEY (EstadoComprobanteCodigo) REFERENCES dbo.EstadoComprobante (Codigo),
    CONSTRAINT UQ_Comprobante_Numero UNIQUE (TipoComprobanteCodigo, NumeroComprobante),
    CONSTRAINT CK_Comprobante_Numero CHECK (LEN(LTRIM(RTRIM(NumeroComprobante))) > 0)
);
CREATE INDEX IX_Comprobante_Compra ON dbo.Comprobante (CompraId);

-- NUEVO. DATOS DEL EMISOR para el encabezado del comprobante (una sola fila).
CREATE TABLE dbo.DatosEmisor (
    DatosEmisorId        TINYINT       NOT NULL CONSTRAINT PK_DatosEmisor PRIMARY KEY,
    NombreComercial      NVARCHAR(150) NOT NULL,
    Identificacion       NVARCHAR(60)  NOT NULL,
    Direccion            NVARCHAR(200) NOT NULL,
    Telefono             VARCHAR(20)   NULL,
    Correo               NVARCHAR(256) NULL,
    LeyendaComprobante   NVARCHAR(300) NOT NULL,
    CONSTRAINT CK_DatosEmisor_UnaFila CHECK (DatosEmisorId = 1)
);
GO

/* =====================================================================================
   A6. VISTAS
===================================================================================== */

-- Detalle de cada vuelo: código de vuelo, ruta, aeropuertos y horario completo.
CREATE VIEW dbo.vw_VueloDetalle
AS
SELECT  v.VueloId,
        'VIP' + RIGHT('00000' + CAST(v.VueloId AS VARCHAR(10)), 5) AS CodigoVuelo,
        v.FechaOperacion,
        f.DiaSemana,
        v.FrecuenciaId,
        r.RutaId,
        r.TarifaReferencial,
        r.AeropuertoOrigenId,
        r.AeropuertoDestinoId,
        o.CodigoIATA  AS Origen,
        o.Localidad   AS CiudadOrigen,
        d.CodigoIATA  AS Destino,
        d.Localidad   AS CiudadDestino,
        f.HoraSalida,
        f.HoraLlegada,
        CAST(v.FechaOperacion AS DATETIME) + CAST(f.HoraSalida  AS DATETIME) AS SalidaProgramada,
        CAST(v.FechaOperacion AS DATETIME) + CAST(f.HoraLlegada AS DATETIME) AS LlegadaProgramada,
        v.AeronaveId,
        v.EstadoVueloCodigo
FROM    dbo.Vuelo v
JOIN    dbo.Frecuencia f ON f.FrecuenciaId = v.FrecuenciaId
JOIN    dbo.Ruta r       ON r.RutaId       = f.RutaId
JOIN    dbo.Aeropuerto o ON o.AeropuertoId = r.AeropuertoOrigenId
JOIN    dbo.Aeropuerto d ON d.AeropuertoId = r.AeropuertoDestinoId;
GO

-- Asientos disponibles por vuelo (derivados, no almacenados).
CREATE VIEW dbo.vw_DisponibilidadVuelo
AS
SELECT  vd.VueloId,
        vd.CodigoVuelo,
        vd.FechaOperacion,
        vd.Origen,
        vd.Destino,
        vd.HoraSalida,
        vd.HoraLlegada,
        vd.EstadoVueloCodigo,
        a.Matricula,
        ISNULL(CAST(m.CapacidadAsientos AS INT), 0)                          AS Capacidad,
        ISNULL(oc.Ocupados, 0)                                               AS Ocupados,
        ISNULL(CAST(m.CapacidadAsientos AS INT), 0) - ISNULL(oc.Ocupados, 0) AS Disponibles
FROM    dbo.vw_VueloDetalle vd
LEFT JOIN dbo.Aeronave a       ON a.AeronaveId       = vd.AeronaveId
LEFT JOIN dbo.ModeloAeronave m ON m.ModeloAeronaveId = a.ModeloAeronaveId
LEFT JOIN (SELECT rpv.VueloId, COUNT(*) AS Ocupados
           FROM   dbo.ReservaPasajeroVuelo rpv
           JOIN   dbo.EstadoAsignacion ea ON ea.Codigo = rpv.EstadoAsignacionCodigo
           WHERE  ea.OcupaInventario = 1
           GROUP BY rpv.VueloId) oc ON oc.VueloId = vd.VueloId;
GO

-- NUEVO. Los 7 asientos de cada aeronave de la flota (3 x 7 = 21 filas).
CREATE VIEW dbo.vw_AsientoAeronave
AS
SELECT  a.AeronaveId,
        a.Matricula,
        s.NumeroAsiento,
        s.Codigo AS CodigoAsiento,
        s.Fila,
        s.Lado,
        s.Ubicacion
FROM    dbo.Aeronave a
JOIN    dbo.Asiento s ON s.ModeloAeronaveId = a.ModeloAeronaveId;
GO

-- NUEVO. Estado visual de cada asiento por vuelo, para el mapa de asientos:
-- DISPONIBLE (verde) | RESERVADO (morado) | OCUPADO (rojo).
-- Solo se listan vuelos con aeronave asignada (RNP-01).
CREATE VIEW dbo.vw_EstadoAsientoVuelo
AS
SELECT  v.VueloId,
        v.AeronaveId,
        s.NumeroAsiento,
        s.Codigo     AS CodigoAsiento,
        s.Fila,
        s.Lado,
        s.Ubicacion,
        ISNULL(ea.EstadoVisual, 'DISPONIBLE') AS EstadoVisual,
        CASE ISNULL(ea.EstadoVisual, 'DISPONIBLE')
             WHEN 'DISPONIBLE' THEN 'Verde'
             WHEN 'RESERVADO'  THEN 'Morado'
             ELSE 'Rojo' END                  AS ColorEstado,
        rpv.AsignacionId,
        rpv.ReservaId,
        rpv.PasajeroId
FROM    dbo.Vuelo v
JOIN    dbo.Aeronave a ON a.AeronaveId = v.AeronaveId
JOIN    dbo.Asiento s  ON s.ModeloAeronaveId = a.ModeloAeronaveId
LEFT JOIN dbo.ReservaPasajeroVuelo rpv
       ON rpv.VueloId = v.VueloId
      AND rpv.NumeroAsiento = s.NumeroAsiento
      AND rpv.EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA')
LEFT JOIN dbo.EstadoAsignacion ea ON ea.Codigo = rpv.EstadoAsignacionCodigo;
GO

-- NUEVO. Encabezado del comprobante académico (datos para generar el PDF en ASP.NET).
-- Los datos del cliente se obtienen de AerolineaVIP_Identity con Reserva.UsuarioId.
CREATE VIEW dbo.vw_ComprobanteDetalle
AS
SELECT  cb.ComprobanteId,
        cb.NumeroComprobante,
        cb.TipoComprobanteCodigo,
        tc.Descripcion          AS TipoComprobante,
        cb.FechaEmision,
        cb.EstadoComprobanteCodigo,
        e.NombreComercial       AS EmisorNombre,
        e.Identificacion        AS EmisorIdentificacion,
        e.Direccion             AS EmisorDireccion,
        e.LeyendaComprobante,
        r.ReservaId,
        r.CodigoReserva,
        r.UsuarioId,
        c.CompraId,
        c.FechaCompra,
        c.Importe               AS TotalOperacion,
        c.MonedaCodigo,
        c.EstadoCompraCodigo,
        p.PagoId,
        p.CodigoOperacion,
        p.FechaPago,
        p.EstadoPagoCodigo,
        mp.Nombre               AS MetodoPago,
        mt.Nombre               AS MarcaTarjeta,
        '**** ' + p.UltimosCuatroDigitos AS TarjetaEnmascarada,
        p.EsSimulado
FROM    dbo.Comprobante cb
JOIN    dbo.TipoComprobante tc ON tc.Codigo   = cb.TipoComprobanteCodigo
JOIN    dbo.Compra c           ON c.CompraId  = cb.CompraId
JOIN    dbo.Reserva r          ON r.ReservaId = c.ReservaId
CROSS JOIN dbo.DatosEmisor e
OUTER APPLY (SELECT TOP (1) px.*
             FROM dbo.Pago px
             WHERE px.CompraId = c.CompraId
             ORDER BY CASE px.EstadoPagoCodigo WHEN 'APROBADO' THEN 0 WHEN 'REVERSADO' THEN 1 ELSE 2 END,
                      px.FechaPago DESC) p
LEFT JOIN dbo.MetodoPago mp   ON mp.Codigo = p.MetodoPagoCodigo
LEFT JOIN dbo.MarcaTarjeta mt ON mt.Codigo = p.MarcaTarjetaCodigo;
GO

/* =====================================================================================
   A7. TRIGGERS DE REGLAS DE NEGOCIO (validación en el servidor)
===================================================================================== */

-- VUELO: día de operación, frecuencia activa, aeronave disponible y sin conflicto horario.
CREATE TRIGGER dbo.trg_Vuelo_Validar
ON dbo.Vuelo
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    -- RN-04/05/06: la fecha debe coincidir con el día de la frecuencia
    -- (1900-01-01 fue lunes; el cálculo no depende de SET DATEFIRST).
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Frecuencia f ON f.FrecuenciaId = i.FrecuenciaId
               WHERE (DATEDIFF(DAY, CAST('19000101' AS DATE), i.FechaOperacion) % 7) + 1 <> f.DiaSemana)
        THROW 50001, N'La fecha del vuelo no corresponde al día de operación autorizado para esa frecuencia.', 1;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Frecuencia f ON f.FrecuenciaId = i.FrecuenciaId
               LEFT JOIN deleted d   ON d.VueloId = i.VueloId
               WHERE d.VueloId IS NULL AND f.Activa = 0)
        THROW 50002, N'No se puede programar un vuelo sobre una frecuencia inactiva.', 1;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Aeronave a ON a.AeronaveId = i.AeronaveId
               LEFT JOIN deleted d ON d.VueloId = i.VueloId
               WHERE a.EstadoAeronaveCodigo <> 'DISPONIBLE'
                 AND (d.VueloId IS NULL OR ISNULL(d.AeronaveId, -1) <> i.AeronaveId))
        THROW 50003, N'La aeronave seleccionada no está disponible para nuevas asignaciones.', 1;

    -- RNP-02: una aeronave no puede tener vuelos con horarios superpuestos.
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.vw_VueloDetalle a ON a.VueloId = i.VueloId
               JOIN dbo.vw_VueloDetalle b ON b.AeronaveId = a.AeronaveId
                                         AND b.VueloId   <> a.VueloId
               WHERE a.AeronaveId IS NOT NULL
                 AND a.EstadoVueloCodigo <> 'CANCELADO'
                 AND b.EstadoVueloCodigo <> 'CANCELADO'
                 AND a.SalidaProgramada < b.LlegadaProgramada
                 AND b.SalidaProgramada < a.LlegadaProgramada)
        THROW 50004, N'La aeronave ya tiene asignado otro vuelo en un horario incompatible.', 1;
END;
GO

-- ASIGNACIÓN DE ASIENTO: reserva vigente, vuelo comercializable y asiento existente.
CREATE TRIGGER dbo.trg_ReservaPasajeroVuelo_Validar
ON dbo.ReservaPasajeroVuelo
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) RETURN;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Reserva r ON r.ReservaId = i.ReservaId
               WHERE i.EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA')
                 AND r.EstadoReservaCodigo IN ('CANCELADA', 'EXPIRADA'))
        THROW 50011, N'La reserva está cancelada o expirada; no puede ocupar asientos.', 1;

    -- RNP-01: no se comercializan asientos sin aeronave asignada.
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Vuelo v ON v.VueloId = i.VueloId
               WHERE i.EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA')
                 AND v.AeronaveId IS NULL)
        THROW 50012, N'El vuelo no tiene una aeronave asignada; sus asientos no pueden reservarse.', 1;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Vuelo v ON v.VueloId = i.VueloId
               WHERE i.EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA')
                 AND v.EstadoVueloCodigo NOT IN ('PROGRAMADO', 'CONFIRMADO'))
        THROW 50013, N'El vuelo no se encuentra en un estado que permita reservas.', 1;

    -- RN-03 / DP-11: el asiento debe existir en la distribución del modelo asignado.
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Vuelo v    ON v.VueloId    = i.VueloId
               JOIN dbo.Aeronave a ON a.AeronaveId = v.AeronaveId
               WHERE i.EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA')
                 AND NOT EXISTS (SELECT 1 FROM dbo.Asiento s
                                 WHERE s.ModeloAeronaveId = a.ModeloAeronaveId
                                   AND s.NumeroAsiento    = i.NumeroAsiento))
        THROW 50014, N'El asiento no existe en la distribución de la aeronave asignada.', 1;
END;
GO

-- TRAMOS: continuidad geográfica y horaria (RNP-05).
CREATE TRIGGER dbo.trg_TramoItinerario_Validar
ON dbo.TramoItinerario
AFTER INSERT, UPDATE, DELETE
AS
BEGIN
    SET NOCOUNT ON;

    IF EXISTS (SELECT 1
               FROM (SELECT vd.AeropuertoDestinoId,
                            vd.LlegadaProgramada,
                            LEAD(vd.AeropuertoOrigenId) OVER (PARTITION BY t.ItinerarioId ORDER BY t.Orden) AS SiguienteOrigen,
                            LEAD(vd.SalidaProgramada)   OVER (PARTITION BY t.ItinerarioId ORDER BY t.Orden) AS SiguienteSalida
                     FROM dbo.TramoItinerario t
                     JOIN dbo.vw_VueloDetalle vd ON vd.VueloId = t.VueloId
                     WHERE t.ItinerarioId IN (SELECT ItinerarioId FROM inserted
                                              UNION
                                              SELECT ItinerarioId FROM deleted)) x
               WHERE x.SiguienteOrigen IS NOT NULL
                 AND (x.SiguienteOrigen <> x.AeropuertoDestinoId
                      OR x.SiguienteSalida < x.LlegadaProgramada))
        THROW 50021, N'Los tramos del itinerario no son continuos.', 1;
END;
GO

-- CHECK-IN: solo para asientos confirmados.
CREATE TRIGGER dbo.trg_CheckIn_Validar
ON dbo.CheckIn
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.ReservaPasajeroVuelo rpv ON rpv.AsignacionId = i.AsignacionId
               WHERE i.EstadoCheckInCodigo = 'COMPLETADO'
                 AND rpv.EstadoAsignacionCodigo <> 'CONFIRMADA')
        THROW 50031, N'El check-in solo puede completarse para un asiento confirmado.', 1;
END;
GO

-- PAGO SIMULADO: el total aprobado no puede superar el importe de la compra y no se
-- aprueban pagos de compras anuladas.
CREATE TRIGGER dbo.trg_Pago_Validar
ON dbo.Pago
AFTER INSERT, UPDATE
AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1
               FROM dbo.Compra c
               JOIN (SELECT p.CompraId, SUM(p.Importe) AS TotalAprobado
                     FROM dbo.Pago p
                     WHERE p.EstadoPagoCodigo = 'APROBADO'
                       AND p.CompraId IN (SELECT CompraId FROM inserted)
                     GROUP BY p.CompraId) t ON t.CompraId = c.CompraId
               WHERE t.TotalAprobado > c.Importe)
        THROW 50041, N'El importe aprobado supera el importe de la compra.', 1;

    IF EXISTS (SELECT 1
               FROM inserted i
               JOIN dbo.Compra c ON c.CompraId = i.CompraId
               WHERE c.EstadoCompraCodigo = 'ANULADA'
                 AND i.EstadoPagoCodigo = 'APROBADO')
        THROW 50042, N'No se puede aprobar un pago de una compra anulada.', 1;
END;
GO

/* =====================================================================================
   A8. PROCEDIMIENTO: generación de vuelos a partir de las frecuencias activas
===================================================================================== */
CREATE PROCEDURE dbo.usp_GenerarVuelos
    @FechaInicio DATE,
    @FechaFin    DATE
AS
BEGIN
    SET NOCOUNT ON;

    IF @FechaInicio IS NULL OR @FechaFin IS NULL OR @FechaFin < @FechaInicio
        THROW 50051, N'Rango de fechas inválido.', 1;
    IF DATEDIFF(DAY, @FechaInicio, @FechaFin) > 365
        THROW 50052, N'El rango no puede superar 366 días.', 1;

    WITH Fechas AS (
        SELECT @FechaInicio AS Fecha
        UNION ALL
        SELECT DATEADD(DAY, 1, Fecha) FROM Fechas WHERE Fecha < @FechaFin
    )
    INSERT INTO dbo.Vuelo (FrecuenciaId, FechaOperacion)
    SELECT f.FrecuenciaId, fe.Fecha
    FROM   Fechas fe
    JOIN   dbo.Frecuencia f
           ON f.DiaSemana = (DATEDIFF(DAY, CAST('19000101' AS DATE), fe.Fecha) % 7) + 1
          AND f.Activa = 1
    WHERE  NOT EXISTS (SELECT 1 FROM dbo.Vuelo v
                       WHERE v.FrecuenciaId = f.FrecuenciaId
                         AND v.FechaOperacion = fe.Fecha)
    OPTION (MAXRECURSION 400);
END;
GO

/* =====================================================================================
   A9. DATOS INICIALES: CATÁLOGOS, AEROPUERTOS, RUTAS, FRECUENCIAS, FLOTA Y ASIENTOS
       (origen: versión anterior de Script_BD_AerolineaVIP.sql, más los catálogos de pagos simulados)
===================================================================================== */
INSERT INTO dbo.EstadoAeronave (Codigo, Descripcion) VALUES
 ('DISPONIBLE',    N'Habilitada para asignaciones'),
 ('NO_DISPONIBLE', N'No elegible para nuevas asignaciones');

INSERT INTO dbo.EstadoVuelo (Codigo, Descripcion) VALUES
 ('PROGRAMADO', N'Vuelo programado'),
 ('CONFIRMADO', N'Vuelo confirmado operativamente'),
 ('CANCELADO',  N'Vuelo cancelado'),
 ('COMPLETADO', N'Vuelo realizado');

INSERT INTO dbo.EstadoItinerario (Codigo, Descripcion) VALUES
 ('ACTIVO',   N'Disponible para comercialización'),
 ('INACTIVO', N'No disponible para comercialización');

INSERT INTO dbo.TipoDocumentoIdentidad (Codigo, Descripcion) VALUES
 ('CEDULA',    N'Cédula de identidad'),
 ('PASAPORTE', N'Pasaporte');

INSERT INTO dbo.EstadoReserva (Codigo, Descripcion) VALUES
 ('PENDIENTE',  N'Reserva registrada, pendiente de compra'),
 ('CONFIRMADA', N'Reserva confirmada'),
 ('CANCELADA',  N'Reserva cancelada'),
 ('EXPIRADA',   N'Reserva expirada por tiempo de retención');

INSERT INTO dbo.EstadoAsignacion (Codigo, Descripcion, OcupaInventario, EstadoVisual) VALUES
 ('PENDIENTE',  N'Asiento reservado, pendiente de pago (morado)', 1, 'RESERVADO'),
 ('CONFIRMADA', N'Asiento ocupado, compra pagada (rojo)',         1, 'OCUPADO'),
 ('CANCELADA',  N'Asiento cancelado (vuelve a estar disponible)', 0, 'DISPONIBLE'),
 ('LIBERADA',   N'Asiento liberado por expiración',               0, 'DISPONIBLE');

INSERT INTO dbo.Moneda (Codigo, Descripcion) VALUES
 ('USD', N'Dólar estadounidense');

INSERT INTO dbo.EstadoCompra (Codigo, Descripcion) VALUES
 ('PENDIENTE', N'Compra registrada, pendiente de pago'),
 ('PAGADA',    N'Compra con pago simulado aprobado'),
 ('ANULADA',   N'Compra anulada');

INSERT INTO dbo.EstadoPago (Codigo, Descripcion) VALUES
 ('PENDIENTE', N'Pago simulado pendiente de resultado'),
 ('APROBADO',  N'Pago simulado aprobado'),
 ('RECHAZADO', N'Pago simulado rechazado'),
 ('REVERSADO', N'Pago simulado reversado');

INSERT INTO dbo.EstadoCheckIn (Codigo, Descripcion) VALUES
 ('COMPLETADO', N'Check-in completado'),
 ('ANULADO',    N'Check-in anulado');

INSERT INTO dbo.TipoComprobante (Codigo, Descripcion) VALUES
 ('CONFIRMACION', N'Confirmación de la operación'),
 ('FACTURA',      N'Comprobante de venta académico (ejemplo, sin validez tributaria)'),
 ('NOTA_CREDITO', N'Nota de crédito académica (ejemplo, sin validez tributaria)');

INSERT INTO dbo.EstadoComprobante (Codigo, Descripcion) VALUES
 ('EMITIDO', N'Comprobante emitido'),
 ('ANULADO', N'Comprobante anulado');

-- Pagos simulados
INSERT INTO dbo.MetodoPago (Codigo, Nombre) VALUES
 ('TARJETA_CREDITO', N'Tarjeta de crédito (simulación académica)'),
 ('TARJETA_DEBITO',  N'Tarjeta de débito (simulación académica)');

-- Configuración académica de presentación (a confirmar). Prefijos y longitudes solo
-- orientan la validación de formato; no se verifica la existencia de ninguna tarjeta.
INSERT INTO dbo.MarcaTarjeta (Codigo, Nombre, LongitudNumero, LongitudCodigoSeguridad, PrefijosReferencia, RutaLogo) VALUES
 ('VISA',       N'Visa',             16, 3, '4',                '~/Content/img/tarjetas/visa.svg'),
 ('MASTERCARD', N'Mastercard',       16, 3, '51-55, 2221-2720', '~/Content/img/tarjetas/mastercard.svg'),
 ('AMEX',       N'American Express', 15, 4, '34, 37',           '~/Content/img/tarjetas/amex.svg'),
 ('DINERS',     N'Diners Club',      14, 3, '36, 38, 300-305',  '~/Content/img/tarjetas/diners.svg');

INSERT INTO dbo.DatosEmisor (DatosEmisorId, NombreComercial, Identificacion, Direccion, Telefono, Correo, LeyendaComprobante)
VALUES (1, N'Aerolínea VIP (proyecto académico)', N'PENDIENTE DE DEFINICIÓN (DP-05)', N'PENDIENTE DE DEFINICIÓN',
        NULL, NULL,
        N'Comprobante académico de una operación simulada. No constituye factura tributaria ni documento autorizado.');

-- Aeropuertos (Galápagos: supuesto pendiente DP-01)
INSERT INTO dbo.Aeropuerto (CodigoIATA, Nombre, Localidad) VALUES
 ('UIO', N'Aeropuerto Internacional Mariscal Sucre',          N'Quito'),
 ('GYE', N'Aeropuerto Internacional José Joaquín de Olmedo', N'Guayaquil'),
 ('MEC', N'Aeropuerto Internacional Eloy Alfaro',            N'Manta'),
 ('GPS', N'Aeropuerto Ecológico Seymour (supuesto DP-01)',   N'Galápagos');

-- Rutas con tarifa de EJEMPLO (DP-04): 650 USD en tramos con Galápagos, 450 USD en los demás.
INSERT INTO dbo.Ruta (AeropuertoOrigenId, AeropuertoDestinoId, TarifaReferencial)
SELECT o.AeropuertoId, d.AeropuertoId, x.Tarifa
FROM (VALUES ('UIO','GYE',450.00), ('GYE','UIO',450.00),
             ('UIO','MEC',450.00), ('MEC','UIO',450.00),
             ('GYE','GPS',650.00), ('GPS','GYE',650.00)) x (Origen, Destino, Tarifa)
JOIN dbo.Aeropuerto o ON o.CodigoIATA = x.Origen
JOIN dbo.Aeropuerto d ON d.CodigoIATA = x.Destino;

-- Frecuencias confirmadas (1 = lunes ... 7 = domingo)
INSERT INTO dbo.Frecuencia (RutaId, DiaSemana, HoraSalida, HoraLlegada)
SELECT r.RutaId, x.Dia, x.Salida, x.Llegada
FROM (VALUES
        ('UIO','GYE',2,'09:00','10:00'), ('GYE','UIO',2,'14:00','15:00'), ('UIO','GYE',2,'19:00','20:00'),
        ('UIO','GYE',4,'09:00','10:00'), ('GYE','UIO',4,'14:00','15:00'), ('UIO','GYE',4,'19:00','20:00'),
        ('UIO','GYE',6,'09:00','10:00'), ('GYE','UIO',6,'14:00','15:00'), ('UIO','GYE',6,'19:00','20:00'),
        ('UIO','MEC',1,'09:00','10:00'), ('MEC','UIO',1,'14:00','15:00'), ('UIO','MEC',1,'19:00','20:00'),
        ('UIO','MEC',3,'09:00','10:00'), ('MEC','UIO',3,'14:00','15:00'), ('UIO','MEC',3,'19:00','20:00'),
        ('UIO','MEC',5,'09:00','10:00'), ('MEC','UIO',5,'14:00','15:00'), ('UIO','MEC',5,'19:00','20:00'),
        ('UIO','GYE',7,'09:00','10:00'), ('GYE','GPS',7,'11:00','13:00'),
        ('GPS','GYE',7,'15:00','17:00'), ('GYE','UIO',7,'18:00','19:00')
     ) x (Origen, Destino, Dia, Salida, Llegada)
JOIN dbo.Aeropuerto o ON o.CodigoIATA = x.Origen
JOIN dbo.Aeropuerto d ON d.CodigoIATA = x.Destino
JOIN dbo.Ruta r       ON r.AeropuertoOrigenId = o.AeropuertoId
                     AND r.AeropuertoDestinoId = d.AeropuertoId;

-- Flota: 3 Embraer Phenom 300E de 7 asientos (identificadores internos, no matrículas reales)
INSERT INTO dbo.ModeloAeronave (Fabricante, Nombre, CapacidadAsientos)
VALUES (N'Embraer', N'Phenom 300E', 7);

-- Distribución de cabina (DP-11, propuesta basada en el dibujo del documento de características)
INSERT INTO dbo.Asiento (ModeloAeronaveId, NumeroAsiento, Codigo, Fila, Lado, Ubicacion)
SELECT m.ModeloAeronaveId, x.Numero, x.Codigo, x.Fila, x.Lado, x.Ubicacion
FROM (VALUES
        (1, '1D', 1, 'D', N'Fila 1, lado derecho, ventanilla (junto a la cabina de mando)'),
        (2, '2D', 2, 'D', N'Fila 2, lado derecho, ventanilla'),
        (3, '2I', 2, 'I', N'Fila 2, lado izquierdo, ventanilla (después de la puerta)'),
        (4, '3D', 3, 'D', N'Fila 3, lado derecho, ventanilla'),
        (5, '3I', 3, 'I', N'Fila 3, lado izquierdo, ventanilla'),
        (6, '4D', 4, 'D', N'Fila 4, lado derecho, ventanilla (junto al baño)'),
        (7, '4I', 4, 'I', N'Fila 4, lado izquierdo, ventanilla')
     ) x (Numero, Codigo, Fila, Lado, Ubicacion)
CROSS JOIN dbo.ModeloAeronave m
WHERE m.Fabricante = N'Embraer' AND m.Nombre = N'Phenom 300E';

INSERT INTO dbo.Aeronave (Matricula, ModeloAeronaveId)
SELECT x.Matricula, m.ModeloAeronaveId
FROM (VALUES ('AV-01'), ('AV-02'), ('AV-03')) x (Matricula)
CROSS JOIN dbo.ModeloAeronave m
WHERE m.Fabricante = N'Embraer' AND m.Nombre = N'Phenom 300E';
GO

/* =====================================================================================
   A10. DATOS OPERATIVOS DE DEMOSTRACIÓN (origen: Script_DATA.sql)
        Semana pasada  : 12/10/2026 al 18/10/2026 -> vuelos completados.
        Semana próxima : 02/11/2026 al 08/11/2026 -> vuelos confirmados o programados.
        Se ejecuta en una sola transacción: si algo falla no queda nada a medias.
===================================================================================== */
SET XACT_ABORT ON;
GO

DROP TABLE IF EXISTS #Res;
DROP TABLE IF EXISTS #Map;
DROP TABLE IF EXISTS #Pax;
GO

BEGIN TRY
    BEGIN TRANSACTION;

    -- ---------- Vuelos ----------
    EXEC dbo.usp_GenerarVuelos @FechaInicio = '20261012', @FechaFin = '20261018';
    EXEC dbo.usp_GenerarVuelos @FechaInicio = '20261102', @FechaFin = '20261108';

    DECLARE @AV1 INT = (SELECT AeronaveId FROM dbo.Aeronave WHERE Matricula = 'AV-01');
    DECLARE @AV2 INT = (SELECT AeronaveId FROM dbo.Aeronave WHERE Matricula = 'AV-02');
    DECLARE @AV3 INT = (SELECT AeronaveId FROM dbo.Aeronave WHERE Matricula = 'AV-03');

    -- Asignación de EJEMPLO (no definitiva, DP-02). El vuelo del sábado 07/11 a las
    -- 19:00 queda sin aeronave para representar una asignación pendiente.
    UPDATE v
       SET v.AeronaveId = CASE WHEN f.DiaSemana IN (1, 3, 5) THEN @AV1
                               WHEN f.DiaSemana IN (2, 4, 6) THEN @AV2
                               ELSE @AV3 END
      FROM dbo.Vuelo v
      JOIN dbo.Frecuencia f ON f.FrecuenciaId = v.FrecuenciaId
     WHERE NOT (v.FechaOperacion = '20261107' AND f.HoraSalida = '19:00');

    UPDATE dbo.Vuelo
       SET EstadoVueloCodigo = 'CONFIRMADO'
     WHERE FechaOperacion BETWEEN '20261102' AND '20261104';

    UPDATE v
       SET v.EstadoVueloCodigo = 'CANCELADO'
      FROM dbo.Vuelo v
      JOIN dbo.Frecuencia f ON f.FrecuenciaId = v.FrecuenciaId
     WHERE v.FechaOperacion = '20261106' AND f.HoraSalida = '19:00';

    -- ---------- Itinerarios y tramos ----------
    INSERT INTO dbo.Itinerario (Referencia, Nombre, EstadoItinerarioCodigo)
    SELECT 'IT-' + CONVERT(CHAR(8), d.FechaOperacion, 112),
           CASE WHEN d.DiaSemana IN (1, 3, 5) THEN N'Quito – Manta – Quito'
                WHEN d.DiaSemana IN (2, 4, 6) THEN N'Quito – Guayaquil – Quito'
                ELSE N'Quito – Galápagos – Quito (escala en Guayaquil)' END
             + N' · ' + CONVERT(NVARCHAR(10), d.FechaOperacion, 103),
           CASE WHEN d.FechaOperacion < '20261101' THEN 'INACTIVO' ELSE 'ACTIVO' END
      FROM (SELECT DISTINCT FechaOperacion, DiaSemana FROM dbo.vw_VueloDetalle) d;

    INSERT INTO dbo.TramoItinerario (ItinerarioId, VueloId, Orden)
    SELECT i.ItinerarioId,
           vd.VueloId,
           ROW_NUMBER() OVER (PARTITION BY vd.FechaOperacion ORDER BY vd.HoraSalida)
      FROM dbo.vw_VueloDetalle vd
      JOIN dbo.Itinerario i ON i.Referencia = 'IT-' + CONVERT(CHAR(8), vd.FechaOperacion, 112)
     WHERE vd.DiaSemana = 7
        OR vd.HoraSalida IN ('09:00', '14:00');

    -- ---------- Pasajeros (ficticios) ----------
    INSERT INTO dbo.Pasajero (TipoDocumentoCodigo, NumeroDocumento, Nombres, Apellidos, FechaNacimiento, Correo, Telefono, FechaRegistro)
    VALUES
     ('CEDULA',    '1710000001', N'Mariana Isabel',    N'Cevallos Ortiz',    '19850314', N'mariana.cevallos@ejemplo.com',   '0991000001',     '20260928 09:10'),
     ('CEDULA',    '1710000002', N'Andrés Felipe',     N'Cevallos Ortiz',    '19830722', N'andres.cevallos@ejemplo.com',    '0991000002',     '20260928 09:12'),
     ('CEDULA',    '1710000003', N'Sofía Valentina',   N'Cevallos Paredes',  '20100105', NULL,                              NULL,             '20260928 09:13'),
     ('CEDULA',    '1710000004', N'Martín Alejandro',  N'Cevallos Paredes',  '20120918', NULL,                              NULL,             '20260928 09:14'),
     ('CEDULA',    '1710000005', N'Rosa Elena',        N'Ortiz Benítez',     '19580411', N'rosa.ortiz@ejemplo.com',         '0991000005',     '20260928 09:15'),
     ('PASAPORTE', 'PA1000006',  N'James Robert',      N'Miller',            '19760203', N'james.miller@ejemplo.com',       '+1 5550100006',  '20260928 09:17'),
     ('PASAPORTE', 'PA1000007',  N'Laura Anne',        N'Miller',            '19790630', N'laura.miller@ejemplo.com',       '+1 5550100007',  '20260928 09:18'),
     ('CEDULA',    '0910000008', N'Carlos Eduardo',    N'Andrade Villacís',  '19700127', N'carlos.andrade@ejemplo.com',     '0991000008',     '20261001 11:00'),
     ('CEDULA',    '0910000009', N'Patricia Lucía',    N'Salazar Mora',      '19720815', N'patricia.salazar@ejemplo.com',   '0991000009',     '20261001 11:02'),
     ('CEDULA',    '1310000010', N'Diego Sebastián',   N'Zambrano Loor',     '19880509', N'diego.zambrano@ejemplo.com',     '0991000010',     '20261002 16:40'),
     ('CEDULA',    '1710000011', N'Gabriela Fernanda', N'Herrera Castillo',  '19910221', N'gabriela.herrera@ejemplo.com',   '0991000011',     '20261003 08:20'),
     ('CEDULA',    '1710000012', N'Javier Antonio',    N'Ruiz Proaño',       '19890612', N'javier.ruiz@ejemplo.com',        '0991000012',     '20261003 08:22'),
     ('CEDULA',    '1710000013', N'Valeria Cristina',  N'Ponce Jaramillo',   '19930930', N'valeria.ponce@ejemplo.com',      '0991000013',     '20261003 08:24'),
     ('CEDULA',    '1310000014', N'Luis Alberto',      N'Mendoza Vera',      '19650418', N'luis.mendoza@ejemplo.com',       '0991000014',     '20261004 10:05'),
     ('CEDULA',    '1710000015', N'Daniela Alejandra', N'Vásquez Rivas',     '19870726', N'daniela.vasquez@ejemplo.com',    '0991000015',     '20261010 14:30'),
     ('CEDULA',    '1710000016', N'Ricardo José',      N'Vásquez Rivas',     '19840103', N'ricardo.vasquez@ejemplo.com',    '0991000016',     '20261010 14:31'),
     ('PASAPORTE', 'PB2000017',  N'Elena',             N'García Martín',     '19810311', N'elena.garcia@ejemplo.com',       '+34 600100017',  '20261012 18:45'),
     ('CEDULA',    '0910000018', N'Fernando Xavier',   N'Molina Aguirre',    '19680524', N'fernando.molina@ejemplo.com',    '0991000018',     '20261014 07:50'),
     ('CEDULA',    '0910000019', N'Ana María',         N'Molina Aguirre',    '19710808', N'anamaria.molina@ejemplo.com',    '0991000019',     '20261014 07:52'),
     ('CEDULA',    '0910000020', N'Tomás Ignacio',     N'Molina Torres',     '20030117', N'tomas.molina@ejemplo.com',       '0991000020',     '20261014 07:53'),
     ('CEDULA',    '1310000021', N'Paola Estefanía',   N'Intriago Cedeño',   '19950402', N'paola.intriago@ejemplo.com',     '0991000021',     '20261015 08:00'),
     ('CEDULA',    '1710000022', N'Sebastián Andrés',  N'Guerrero León',     '19900913', N'sebastian.guerrero@ejemplo.com', '0991000022',     '20261020 15:00'),
     ('CEDULA',    '1710000023', N'Camila Belén',      N'Guerrero León',     '19921127', N'camila.guerrero@ejemplo.com',    '0991000023',     '20261020 15:01'),
     ('PASAPORTE', 'PC3000024',  N'Hiroshi',           N'Tanaka',            '19770619', N'hiroshi.tanaka@ejemplo.com',     '+81 9010000024', '20261019 12:10');

    -- ---------- Reservas ----------
    -- UsuarioId: identificadores de DEMOSTRACIÓN. Para enlazarlos con usuarios reales,
    -- el código de inicialización de Identity puede crear usuarios con estos mismos Id.
    -- Tipo de viaje: IV = ida y vuelta (09:00 + 14:00), GPS = 4 tramos del domingo,
    --                H09 = solo vuelo de 09:00, H14 = solo vuelo de 14:00.
    CREATE TABLE #Res (
        Clave           CHAR(3)       NOT NULL PRIMARY KEY,
        UsuarioId       NVARCHAR(128) NOT NULL,
        FechaCreacion   DATETIME2(0)  NOT NULL,
        FechaExpiracion DATETIME2(0)  NULL,
        Estado          VARCHAR(20)   NOT NULL,
        FechaViaje      DATE          NOT NULL,
        Tipo            VARCHAR(3)    NOT NULL,
        ReservaId       INT           NULL
    );

    INSERT INTO #Res (Clave, UsuarioId, FechaCreacion, FechaExpiracion, Estado, FechaViaje, Tipo)
    VALUES
     ('R01', N'7c1e4a20-0001-4b6e-9a01-000000000001', '20260928 09:20', NULL,             'CONFIRMADA', '20261018', 'GPS'),
     ('R02', N'7c1e4a20-0002-4b6e-9a01-000000000002', '20261001 11:05', NULL,             'CONFIRMADA', '20261013', 'IV'),
     ('R03', N'7c1e4a20-0003-4b6e-9a01-000000000003', '20261002 16:45', NULL,             'CONFIRMADA', '20261012', 'IV'),
     ('R04', N'7c1e4a20-0004-4b6e-9a01-000000000004', '20261003 08:30', NULL,             'CONFIRMADA', '20261015', 'H09'),
     ('R05', N'7c1e4a20-0005-4b6e-9a01-000000000005', '20261004 10:10', NULL,             'CANCELADA',  '20261014', 'H09'),
     ('R06', N'7c1e4a20-0001-4b6e-9a01-000000000001', '20261010 14:35', NULL,             'CONFIRMADA', '20261102', 'IV'),
     ('R07', N'7c1e4a20-0002-4b6e-9a01-000000000002', '20261012 18:50', NULL,             'CONFIRMADA', '20261103', 'IV'),
     ('R08', N'7c1e4a20-0003-4b6e-9a01-000000000003', '20261014 07:55', NULL,             'CONFIRMADA', '20261108', 'GPS'),
     ('R09', N'7c1e4a20-0004-4b6e-9a01-000000000004', '20261015 08:05', '20261016 08:05', 'EXPIRADA',   '20261104', 'H09'),
     ('R10', N'7c1e4a20-0005-4b6e-9a01-000000000005', '20261020 15:05', '20261022 15:05', 'PENDIENTE',  '20261105', 'H09'),
     ('R11', N'7c1e4a20-0001-4b6e-9a01-000000000001', '20261021 09:00', '20261023 09:00', 'PENDIENTE',  '20261107', 'H14'),
     ('R12', N'7c1e4a20-0002-4b6e-9a01-000000000002', '20261019 12:15', NULL,             'CONFIRMADA', '20261107', 'H09');

    CREATE TABLE #Map (Clave CHAR(3) NOT NULL PRIMARY KEY, ReservaId INT NOT NULL);

    MERGE dbo.Reserva AS t
    USING #Res AS s
       ON 1 = 0
    WHEN NOT MATCHED THEN
        INSERT (UsuarioId, FechaCreacion, FechaExpiracion, EstadoReservaCodigo)
        VALUES (s.UsuarioId, s.FechaCreacion, s.FechaExpiracion, s.Estado)
    OUTPUT s.Clave, inserted.ReservaId INTO #Map (Clave, ReservaId);

    UPDATE r SET r.ReservaId = m.ReservaId
      FROM #Res r JOIN #Map m ON m.Clave = r.Clave;

    -- ---------- Asientos por pasajero y vuelo ----------
    -- Asiento: 1 = 1D, 2 = 2D, 3 = 2I, 4 = 3D, 5 = 3I, 6 = 4D, 7 = 4I.
    CREATE TABLE #Pax (
        Clave            CHAR(3)     NOT NULL,
        NumeroDocumento  VARCHAR(20) NOT NULL,
        Asiento          TINYINT     NOT NULL,
        PRIMARY KEY (Clave, NumeroDocumento)
    );

    INSERT INTO #Pax (Clave, NumeroDocumento, Asiento)
    VALUES
     ('R01','1710000001',1), ('R01','1710000002',2), ('R01','1710000003',3), ('R01','1710000004',4),
     ('R01','1710000005',5), ('R01','PA1000006',6),  ('R01','PA1000007',7),
     ('R02','0910000008',1), ('R02','0910000009',2),
     ('R03','1310000010',1),
     ('R04','1710000011',1), ('R04','1710000012',2), ('R04','1710000013',3),
     ('R05','1310000014',1),
     ('R06','1710000015',1), ('R06','1710000016',2),
     ('R07','PB2000017',1),
     ('R08','0910000018',1), ('R08','0910000019',2), ('R08','0910000020',3),
     ('R09','1310000021',1),
     ('R10','1710000022',1), ('R10','1710000023',2),
     ('R11','1710000001',1),
     ('R12','PC3000024',1),  ('R12','0910000008',2);

    INSERT INTO dbo.ReservaPasajeroVuelo (ReservaId, PasajeroId, VueloId, NumeroAsiento, ImporteTramo, EstadoAsignacionCodigo, FechaAsignacion)
    SELECT r.ReservaId,
           p.PasajeroId,
           vd.VueloId,
           x.Asiento,
           vd.TarifaReferencial,
           CASE r.Estado WHEN 'CONFIRMADA' THEN 'CONFIRMADA'
                         WHEN 'PENDIENTE'  THEN 'PENDIENTE'
                         WHEN 'CANCELADA'  THEN 'CANCELADA'
                         ELSE 'LIBERADA' END,
           r.FechaCreacion
      FROM #Pax x
      JOIN #Res r          ON r.Clave = x.Clave
      JOIN dbo.Pasajero p  ON p.NumeroDocumento = x.NumeroDocumento
      JOIN dbo.vw_VueloDetalle vd
           ON vd.FechaOperacion = r.FechaViaje
          AND (    r.Tipo = 'GPS'
               OR (r.Tipo = 'IV'  AND vd.HoraSalida IN ('09:00', '14:00'))
               OR (r.Tipo = 'H09' AND vd.HoraSalida = '09:00')
               OR (r.Tipo = 'H14' AND vd.HoraSalida = '14:00'));

    IF (SELECT COUNT(*) FROM dbo.ReservaPasajeroVuelo) <> 62
        THROW 50102, N'La cantidad de asignaciones de asientos no coincide con el plan (62).', 1;

    UPDATE dbo.Vuelo
       SET EstadoVueloCodigo = 'COMPLETADO'
     WHERE FechaOperacion < '20261101';

    -- ---------- Compras (importe = suma de ImporteTramo) ----------
    INSERT INTO dbo.Compra (ReservaId, FechaCompra, Importe, MonedaCodigo, EstadoCompraCodigo)
    SELECT r.ReservaId,
           DATEADD(MINUTE, 30, r.FechaCreacion),
           t.Importe,
           'USD',
           CASE r.Estado WHEN 'CONFIRMADA' THEN 'PAGADA'
                         WHEN 'CANCELADA'  THEN 'ANULADA'
                         ELSE 'PENDIENTE' END
      FROM #Res r
     CROSS APPLY (SELECT SUM(a.ImporteTramo) AS Importe
                    FROM dbo.ReservaPasajeroVuelo a
                   WHERE a.ReservaId = r.ReservaId) t
     WHERE r.Clave NOT IN ('R09', 'R11');

    -- ---------- Pagos simulados (datos ficticios) ----------
    -- Primer intento rechazado para R01 (luego aprobado) y R10 (sigue pendiente).
    INSERT INTO dbo.Pago (CompraId, FechaPago, Importe, EstadoPagoCodigo, MetodoPagoCodigo, MarcaTarjetaCodigo,
                          UltimosCuatroDigitos, NombreTitular, CodigoOperacion, MensajeResultado)
    SELECT c.CompraId, DATEADD(MINUTE, 2, c.FechaCompra), c.Importe, 'RECHAZADO',
           'TARJETA_DEBITO', 'MASTERCARD', '0000', N'TITULAR FICTICIO',
           'SIM-' + rv.CodigoReserva + '-01',
           N'Pago simulado rechazado (dato de demostración).'
      FROM dbo.Compra c
      JOIN dbo.Reserva rv ON rv.ReservaId = c.ReservaId
      JOIN #Res r         ON r.ReservaId  = c.ReservaId
     WHERE r.Clave IN ('R01', 'R10');

    -- Pago aprobado para cada compra pagada; reversado para la compra anulada.
    -- La marca rota entre las 4 configuradas para mostrar todas en la demostración.
    INSERT INTO dbo.Pago (CompraId, FechaPago, Importe, EstadoPagoCodigo, MetodoPagoCodigo, MarcaTarjetaCodigo,
                          UltimosCuatroDigitos, NombreTitular, CodigoOperacion, MensajeResultado)
    SELECT c.CompraId, DATEADD(MINUTE, 5, c.FechaCompra), c.Importe,
           CASE c.EstadoCompraCodigo WHEN 'PAGADA' THEN 'APROBADO' ELSE 'REVERSADO' END,
           'TARJETA_CREDITO',
           CASE c.CompraId % 4 WHEN 1 THEN 'VISA' WHEN 2 THEN 'MASTERCARD' WHEN 3 THEN 'AMEX' ELSE 'DINERS' END,
           RIGHT('0000' + CAST(c.CompraId AS VARCHAR(10)), 4),
           N'TITULAR FICTICIO',
           'SIM-' + rv.CodigoReserva + '-02',
           CASE c.EstadoCompraCodigo WHEN 'PAGADA' THEN N'Pago simulado aprobado (dato de demostración).'
                                     ELSE N'Pago simulado reversado por cancelación (dato de demostración).' END
      FROM dbo.Compra c
      JOIN dbo.Reserva rv ON rv.ReservaId = c.ReservaId
     WHERE c.EstadoCompraCodigo IN ('PAGADA', 'ANULADA');

    -- ---------- Check-in de los vuelos completados ----------
    INSERT INTO dbo.CheckIn (AsignacionId, FechaRegistro, EstadoCheckInCodigo)
    SELECT a.AsignacionId,
           DATEADD(HOUR, -20, vd.SalidaProgramada),
           CASE WHEN p.NumeroDocumento = '1710000013' THEN 'ANULADO' ELSE 'COMPLETADO' END
      FROM dbo.ReservaPasajeroVuelo a
      JOIN dbo.vw_VueloDetalle vd ON vd.VueloId = a.VueloId
      JOIN dbo.Pasajero p         ON p.PasajeroId = a.PasajeroId
     WHERE a.EstadoAsignacionCodigo = 'CONFIRMADA'
       AND vd.FechaOperacion < '20261101';

    -- ---------- Comprobantes académicos (numeración de ejemplo) ----------
    ;WITH C AS (
        SELECT c.CompraId, c.FechaCompra, c.EstadoCompraCodigo,
               ROW_NUMBER() OVER (ORDER BY c.CompraId) AS n
          FROM dbo.Compra c
         WHERE c.EstadoCompraCodigo IN ('PAGADA', 'ANULADA')
    )
    INSERT INTO dbo.Comprobante (CompraId, TipoComprobanteCodigo, NumeroComprobante, FechaEmision, EstadoComprobanteCodigo)
    SELECT CompraId, 'CONFIRMACION',
           'CNF-' + RIGHT('000000' + CAST(n AS VARCHAR(6)), 6),
           DATEADD(MINUTE, 6, FechaCompra),
           CASE EstadoCompraCodigo WHEN 'ANULADA' THEN 'ANULADO' ELSE 'EMITIDO' END
      FROM C
    UNION ALL
    SELECT CompraId, 'FACTURA',
           'ACAD-' + RIGHT('000000' + CAST(n AS VARCHAR(6)), 6),
           DATEADD(MINUTE, 7, FechaCompra),
           'EMITIDO'
      FROM C;

    INSERT INTO dbo.Comprobante (CompraId, TipoComprobanteCodigo, NumeroComprobante, FechaEmision, EstadoComprobanteCodigo)
    SELECT c.CompraId, 'NOTA_CREDITO',
           'NC-ACAD-' + RIGHT('000000' + CAST(ROW_NUMBER() OVER (ORDER BY c.CompraId) AS VARCHAR(6)), 6),
           DATEADD(DAY, 2, c.FechaCompra),
           'EMITIDO'
      FROM dbo.Compra c
     WHERE c.EstadoCompraCodigo = 'ANULADA';

    COMMIT TRANSACTION;
    PRINT N'OK  Parte A: datos iniciales insertados correctamente.';
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    PRINT N'ERROR  Parte A: no se insertaron los datos operativos (transacción revertida).';
    THROW;
END CATCH;
GO

DROP TABLE IF EXISTS #Res;
DROP TABLE IF EXISTS #Map;
DROP TABLE IF EXISTS #Pax;
GO

/* =====================================================================================
   A11. VERIFICACIÓN DE LA PARTE A: todas las tablas de dominio deben tener datos
===================================================================================== */
DECLARE @Conteo TABLE (Tabla SYSNAME NOT NULL, Registros INT NOT NULL);

INSERT INTO @Conteo (Tabla, Registros)
          SELECT 'EstadoAeronave',         COUNT(*) FROM dbo.EstadoAeronave
UNION ALL SELECT 'EstadoVuelo',            COUNT(*) FROM dbo.EstadoVuelo
UNION ALL SELECT 'EstadoItinerario',       COUNT(*) FROM dbo.EstadoItinerario
UNION ALL SELECT 'TipoDocumentoIdentidad', COUNT(*) FROM dbo.TipoDocumentoIdentidad
UNION ALL SELECT 'EstadoReserva',          COUNT(*) FROM dbo.EstadoReserva
UNION ALL SELECT 'EstadoAsignacion',       COUNT(*) FROM dbo.EstadoAsignacion
UNION ALL SELECT 'Moneda',                 COUNT(*) FROM dbo.Moneda
UNION ALL SELECT 'EstadoCompra',           COUNT(*) FROM dbo.EstadoCompra
UNION ALL SELECT 'EstadoPago',             COUNT(*) FROM dbo.EstadoPago
UNION ALL SELECT 'EstadoCheckIn',          COUNT(*) FROM dbo.EstadoCheckIn
UNION ALL SELECT 'TipoComprobante',        COUNT(*) FROM dbo.TipoComprobante
UNION ALL SELECT 'EstadoComprobante',      COUNT(*) FROM dbo.EstadoComprobante
UNION ALL SELECT 'MetodoPago',             COUNT(*) FROM dbo.MetodoPago
UNION ALL SELECT 'MarcaTarjeta',           COUNT(*) FROM dbo.MarcaTarjeta
UNION ALL SELECT 'DatosEmisor',            COUNT(*) FROM dbo.DatosEmisor
UNION ALL SELECT 'Aeropuerto',             COUNT(*) FROM dbo.Aeropuerto
UNION ALL SELECT 'Ruta',                   COUNT(*) FROM dbo.Ruta
UNION ALL SELECT 'Frecuencia',             COUNT(*) FROM dbo.Frecuencia
UNION ALL SELECT 'ModeloAeronave',         COUNT(*) FROM dbo.ModeloAeronave
UNION ALL SELECT 'Asiento',                COUNT(*) FROM dbo.Asiento
UNION ALL SELECT 'Aeronave',               COUNT(*) FROM dbo.Aeronave
UNION ALL SELECT 'Vuelo',                  COUNT(*) FROM dbo.Vuelo
UNION ALL SELECT 'Itinerario',             COUNT(*) FROM dbo.Itinerario
UNION ALL SELECT 'TramoItinerario',        COUNT(*) FROM dbo.TramoItinerario
UNION ALL SELECT 'Pasajero',               COUNT(*) FROM dbo.Pasajero
UNION ALL SELECT 'Reserva',                COUNT(*) FROM dbo.Reserva
UNION ALL SELECT 'ReservaPasajeroVuelo',   COUNT(*) FROM dbo.ReservaPasajeroVuelo
UNION ALL SELECT 'Compra',                 COUNT(*) FROM dbo.Compra
UNION ALL SELECT 'Pago',                   COUNT(*) FROM dbo.Pago
UNION ALL SELECT 'CheckIn',                COUNT(*) FROM dbo.CheckIn
UNION ALL SELECT 'Comprobante',            COUNT(*) FROM dbo.Comprobante;

SELECT Tabla, Registros,
       CASE WHEN Registros > 0 THEN 'OK' ELSE 'SIN DATOS' END AS Verificacion
  FROM @Conteo
 ORDER BY Tabla;

DECLARE @Total INT  = (SELECT COUNT(*) FROM @Conteo);
DECLARE @Vacias INT = (SELECT COUNT(*) FROM @Conteo WHERE Registros = 0);
DECLARE @AsientosFlota INT = (SELECT COUNT(*) FROM dbo.vw_AsientoAeronave);

IF @Vacias > 0
    PRINT N'ERROR  Parte A: hay ' + CAST(@Vacias AS NVARCHAR(10)) + N' tablas sin datos.';
ELSE
    PRINT N'OK  Parte A: las ' + CAST(@Total AS NVARCHAR(10)) + N' tablas de dominio tienen datos.';

IF @AsientosFlota = 21
    PRINT N'OK  Parte A: 3 aeronaves x 7 asientos = 21 asientos en la flota.';
ELSE
    PRINT N'ERROR  Parte A: la flota tiene ' + CAST(@AsientosFlota AS NVARCHAR(10)) + N' asientos (se esperaban 21).';
GO

/* =====================================================================================
   A12. CONSULTAS DE EJEMPLO PARA GENERAR EL COMPROBANTE ACADÉMICO EN PDF
        La aplicación ASP.NET ejecuta consultas equivalentes y genera el PDF.
===================================================================================== */
DECLARE @Numero VARCHAR(30) = 'ACAD-000001';

-- 1) Encabezado: emisor, comprobante, reserva, compra y pago simulado.
SELECT ComprobanteId, NumeroComprobante, TipoComprobante, FechaEmision, EstadoComprobanteCodigo,
       EmisorNombre, EmisorIdentificacion, EmisorDireccion, LeyendaComprobante,
       CodigoReserva, UsuarioId, TotalOperacion, MonedaCodigo, EstadoCompraCodigo,
       CodigoOperacion, EstadoPagoCodigo, MetodoPago, MarcaTarjeta, TarjetaEnmascarada
  FROM dbo.vw_ComprobanteDetalle
 WHERE TipoComprobanteCodigo = 'FACTURA' AND NumeroComprobante = @Numero;

-- 2) Detalle: un concepto por pasajero y tramo (vuelo, horario, asiento e importe).
SELECT p.Nombres + N' ' + p.Apellidos      AS Pasajero,
       p.TipoDocumentoCodigo + N' ' + p.NumeroDocumento AS Documento,
       vd.CodigoVuelo,
       vd.CiudadOrigen + N' → ' + vd.CiudadDestino AS Ruta,
       vd.FechaOperacion,
       vd.HoraSalida,
       vd.HoraLlegada,
       s.Codigo                             AS Asiento,
       s.Ubicacion,
       a.ImporteTramo,
       c.MonedaCodigo
  FROM dbo.Comprobante cb
  JOIN dbo.Compra c                 ON c.CompraId = cb.CompraId
  JOIN dbo.ReservaPasajeroVuelo a   ON a.ReservaId = c.ReservaId
  JOIN dbo.Pasajero p               ON p.PasajeroId = a.PasajeroId
  JOIN dbo.vw_VueloDetalle vd       ON vd.VueloId = a.VueloId
  JOIN dbo.Vuelo v                  ON v.VueloId = a.VueloId
  JOIN dbo.Aeronave ae              ON ae.AeronaveId = v.AeronaveId
  JOIN dbo.Asiento s                ON s.ModeloAeronaveId = ae.ModeloAeronaveId
                                   AND s.NumeroAsiento    = a.NumeroAsiento
 WHERE cb.TipoComprobanteCodigo = 'FACTURA' AND cb.NumeroComprobante = @Numero
 ORDER BY vd.SalidaProgramada, s.NumeroAsiento;

-- 3) Datos del cliente (opcional, consulta entre bases del mismo servidor):
-- SELECT u.UserName, u.Email
--   FROM AerolineaVIP_Identity.dbo.AspNetUsers u
--  WHERE u.Id = (SELECT UsuarioId FROM dbo.vw_ComprobanteDetalle
--                 WHERE TipoComprobanteCodigo = 'FACTURA' AND NumeroComprobante = @Numero);

-- 4) Mapa de asientos de un vuelo (pantallas 7 y 8): estado y color de cada asiento.
SELECT TOP (7) v.CodigoVuelo, e.CodigoAsiento, e.Ubicacion, e.EstadoVisual, e.ColorEstado
  FROM dbo.vw_EstadoAsientoVuelo e
  JOIN dbo.vw_VueloDetalle v ON v.VueloId = e.VueloId
 WHERE v.FechaOperacion = '20261108' AND v.HoraSalida = '11:00'
 ORDER BY e.NumeroAsiento;
GO

/* #####################################################################################
   PARTE B. BASE DE DATOS INDEPENDIENTE PARA ASP.NET IDENTITY: AerolineaVIP_Identity
   ------------------------------------------------------------------------------------
   Tablas estándar de ASP.NET Identity 2.x (Microsoft.AspNet.Identity.EntityFramework).
   Se crean SOLO si no existen. Los roles (SuperUsuario, Administrador, Cliente, etc.),
   los usuarios iniciales y su asignación de roles se crean MEDIANTE CÓDIGO en la
   aplicación (Global.asax.cs / inicializador), porque Identity genera el hash de las
   contraseñas.
   Si el grupo amplía ApplicationUser con propiedades adicionales (por ejemplo,
   Nombres o Apellidos), debe agregar esas columnas a AspNetUsers o permitir que
   Entity Framework cree las tablas en esta base vacía.
##################################################################################### */
IF DB_ID(N'AerolineaVIP_Identity') IS NULL
    CREATE DATABASE AerolineaVIP_Identity;
GO

USE AerolineaVIP_Identity;
GO

IF OBJECT_ID(N'dbo.AspNetRoles') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetRoles (
        Id    NVARCHAR(128) NOT NULL,
        Name  NVARCHAR(256) NOT NULL,
        CONSTRAINT [PK_dbo.AspNetRoles] PRIMARY KEY CLUSTERED (Id)
    );
    CREATE UNIQUE NONCLUSTERED INDEX RoleNameIndex ON dbo.AspNetRoles (Name);
END;

IF OBJECT_ID(N'dbo.AspNetUsers') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUsers (
        Id                    NVARCHAR(128) NOT NULL,
        Email                 NVARCHAR(256) NULL,
        EmailConfirmed        BIT           NOT NULL,
        PasswordHash          NVARCHAR(MAX) NULL,
        SecurityStamp         NVARCHAR(MAX) NULL,
        PhoneNumber           NVARCHAR(MAX) NULL,
        PhoneNumberConfirmed  BIT           NOT NULL,
        TwoFactorEnabled      BIT           NOT NULL,
        LockoutEndDateUtc     DATETIME      NULL,
        LockoutEnabled        BIT           NOT NULL,
        AccessFailedCount     INT           NOT NULL,
        UserName              NVARCHAR(256) NOT NULL,
        CONSTRAINT [PK_dbo.AspNetUsers] PRIMARY KEY CLUSTERED (Id)
    );
    CREATE UNIQUE NONCLUSTERED INDEX UserNameIndex ON dbo.AspNetUsers (UserName);
END;

IF OBJECT_ID(N'dbo.AspNetUserClaims') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserClaims (
        Id          INT IDENTITY(1,1) NOT NULL,
        UserId      NVARCHAR(128) NOT NULL,
        ClaimType   NVARCHAR(MAX) NULL,
        ClaimValue  NVARCHAR(MAX) NULL,
        CONSTRAINT [PK_dbo.AspNetUserClaims] PRIMARY KEY CLUSTERED (Id),
        CONSTRAINT [FK_dbo.AspNetUserClaims_dbo.AspNetUsers_UserId] FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_UserId ON dbo.AspNetUserClaims (UserId);
END;

IF OBJECT_ID(N'dbo.AspNetUserLogins') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserLogins (
        LoginProvider  NVARCHAR(128) NOT NULL,
        ProviderKey    NVARCHAR(128) NOT NULL,
        UserId         NVARCHAR(128) NOT NULL,
        CONSTRAINT [PK_dbo.AspNetUserLogins] PRIMARY KEY CLUSTERED (LoginProvider, ProviderKey, UserId),
        CONSTRAINT [FK_dbo.AspNetUserLogins_dbo.AspNetUsers_UserId] FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_UserId ON dbo.AspNetUserLogins (UserId);
END;

IF OBJECT_ID(N'dbo.AspNetUserRoles') IS NULL
BEGIN
    CREATE TABLE dbo.AspNetUserRoles (
        UserId  NVARCHAR(128) NOT NULL,
        RoleId  NVARCHAR(128) NOT NULL,
        CONSTRAINT [PK_dbo.AspNetUserRoles] PRIMARY KEY CLUSTERED (UserId, RoleId),
        CONSTRAINT [FK_dbo.AspNetUserRoles_dbo.AspNetUsers_UserId] FOREIGN KEY (UserId)
            REFERENCES dbo.AspNetUsers (Id) ON DELETE CASCADE,
        CONSTRAINT [FK_dbo.AspNetUserRoles_dbo.AspNetRoles_RoleId] FOREIGN KEY (RoleId)
            REFERENCES dbo.AspNetRoles (Id) ON DELETE CASCADE
    );
    CREATE NONCLUSTERED INDEX IX_UserId ON dbo.AspNetUserRoles (UserId);
    CREATE NONCLUSTERED INDEX IX_RoleId ON dbo.AspNetUserRoles (RoleId);
END;
GO

-- Verificación de la PARTE B (las tablas existen; quedan vacías hasta que la aplicación
-- ejecute la inicialización de roles y usuarios por código).
IF OBJECT_ID(N'dbo.AspNetRoles') IS NOT NULL AND OBJECT_ID(N'dbo.AspNetUsers') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetUserRoles') IS NOT NULL AND OBJECT_ID(N'dbo.AspNetUserClaims') IS NOT NULL
   AND OBJECT_ID(N'dbo.AspNetUserLogins') IS NOT NULL
    PRINT N'OK  Parte B: base AerolineaVIP_Identity lista con las 5 tablas de ASP.NET Identity.';
ELSE
    PRINT N'ERROR  Parte B: faltan tablas de ASP.NET Identity.';
GO

USE AerolineaVIP;
GO
PRINT N'OK  Script_BD_AerolineaVIP finalizado.';
GO
