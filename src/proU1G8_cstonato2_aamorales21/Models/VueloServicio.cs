using Con_Autenticacion.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace Con_Autenticacion.Negocio
{
    // Resultado de la búsqueda de vuelos (Pantalla 6)
    public class ResultadoBusqueda
    {
        public string Error { get; set; }            // null si la búsqueda es válida
        public List<Vuelo> Vuelos { get; set; }
        public bool EsItinerario { get; set; }       // ruta con escala dominical UIO <-> GPS
        public Aeropuerto Origen { get; set; }
        public Aeropuerto Destino { get; set; }
        public DateTime Fecha { get; set; }
    }

    // Lógica de negocio de vuelos e itinerarios
    // (Pantallas 5, 6 y 18 · MOD-02 consulta de itinerarios y MOD-10 programación).
    public class VueloServicio
    {
        private readonly AerolineaVIP_Context db;

        public VueloServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // Fecha y hora de salida / llegada de un vuelo
        public static DateTime Salida(Vuelo v)
        {
            return v.FechaOperacion.Add(v.Frecuencia.HoraSalida);
        }

        public static DateTime Llegada(Vuelo v)
        {
            return v.FechaOperacion.Add(v.Frecuencia.HoraLlegada);
        }

        // =====================================================================
        // DATOS DEL VUELO (Pantallas 12 a 16, 18, 19 y 21) — vista SQL vw_VueloDetalle
        // =====================================================================

        // Carga en cada vuelo su código, origen y destino desde la vista SQL dbo.vw_VueloDetalle
        public void CargarDetalle(IEnumerable<Vuelo> vuelos)
        {
            var lista = vuelos.Where(v => v != null).Distinct().ToList();
            if (!lista.Any())
                return;

            var ids = lista.Select(v => v.VueloId).Distinct().ToList();
            var datos = db.vw_VueloDetalle
                .AsNoTracking()
                .Where(d => ids.Contains(d.VueloId))
                .ToList()
                .ToDictionary(d => d.VueloId);

            foreach (var vuelo in lista)
            {
                vw_VueloDetalle d;
                if (datos.TryGetValue(vuelo.VueloId, out d))
                {
                    vuelo.CodigoVueloVista = d.CodigoVuelo;
                    vuelo.OrigenVista = d.Origen;
                    vuelo.DestinoVista = d.Destino;
                    vuelo.CiudadOrigenVista = d.CiudadOrigen;
                    vuelo.CiudadDestinoVista = d.CiudadDestino;
                }
            }
        }

        // =====================================================================
        // MAPA DE ASIENTOS (Pantallas 7 y 8) — vista SQL vw_EstadoAsientoVuelo
        // =====================================================================

        // Carga en el vuelo el estado de sus 7 asientos desde la vista SQL
        // dbo.vw_EstadoAsientoVuelo: DISPONIBLE (verde), RESERVADO (morado), OCUPADO (rojo).
        // Si el vuelo no tiene aeronave, la vista no devuelve filas y se mantiene el cálculo en C#.
        public void CargarEstadoAsientos(Vuelo vuelo)
        {
            // DP-07: primero se liberan las reservas pendientes vencidas, para que
            // la vista no siga mostrando en morado un asiento cuyo plazo ya terminó
            Reserva.LiberarVencidas(db);

            var filas = db.vw_EstadoAsientoVuelo
                .AsNoTracking()
                .Where(a => a.VueloId == vuelo.VueloId)
                .OrderBy(a => a.NumeroAsiento)
                .ToList();

            if (!filas.Any())
                return;

            vuelo.AsientosDesdeVista = filas.Select(f => new AsientoInfo
            {
                Numero = f.NumeroAsiento,
                Codigo = f.CodigoAsiento,
                Ubicacion = f.Ubicacion,
                Estado = f.EstadoVisual.ToLower(),          // disponible / reservado / ocupado
                Color = f.ColorEstado,                       // Verde / Morado / Rojo
                GridClass = "seat-" + f.CodigoAsiento.ToLower()
            }).ToList();
        }

        // =====================================================================
        // ASIENTOS DISPONIBLES (Pantallas 6, 7 y 8) — vista SQL vw_DisponibilidadVuelo
        // =====================================================================

        // Carga en cada vuelo sus asientos disponibles desde la vista SQL
        // dbo.vw_DisponibilidadVuelo (Disponibles = Capacidad - Ocupados).
        public void CargarDisponibilidad(IEnumerable<Vuelo> vuelos)
        {
            var lista = vuelos.ToList();
            if (!lista.Any())
                return;

            // DP-07: las reservas pendientes vencidas dejan de ocupar asientos
            Reserva.LiberarVencidas(db);

            var ids = lista.Select(v => v.VueloId).ToList();
            var datos = db.vw_DisponibilidadVuelo
                .AsNoTracking()
                .Where(d => ids.Contains(d.VueloId))
                .ToList();

            foreach (var vuelo in lista)
            {
                var d = datos.FirstOrDefault(x => x.VueloId == vuelo.VueloId);
                if (d != null && d.Disponibles.HasValue)
                {
                    vuelo.DisponiblesDesdeVista = d.Disponibles.Value;
                }
            }
        }

        // Versión para un solo vuelo
        public void CargarDisponibilidad(Vuelo vuelo)
        {
            CargarDisponibilidad(new List<Vuelo> { vuelo });
        }

        // =====================================================================
        // BÚSQUEDA DE VUELOS (Pantallas 5 y 6)
        // =====================================================================

        public ResultadoBusqueda BuscarVuelos(int? origenId, int? destinoId, DateTime? fecha)
        {
            var resultado = new ResultadoBusqueda { Vuelos = new List<Vuelo>() };

            // 1. Validaciones básicas de entrada
            if (!origenId.HasValue || !destinoId.HasValue || !fecha.HasValue || origenId.Value == destinoId.Value)
            {
                resultado.Error = (origenId.HasValue && destinoId.HasValue && origenId.Value == destinoId.Value)
                    ? "El aeropuerto de origen y destino deben ser diferentes."
                    : "Debe especificar origen, destino y fecha para consultar vuelos.";
                return resultado;
            }

            resultado.Fecha = fecha.Value.Date;
            resultado.Origen = db.Aeropuerto.Find(origenId.Value);
            resultado.Destino = db.Aeropuerto.Find(destinoId.Value);

            if (resultado.Origen == null || resultado.Destino == null)
            {
                resultado.Error = "Uno de los aeropuertos seleccionados no es válido.";
                return resultado;
            }

            string iataO = resultado.Origen.CodigoIATA;
            string iataD = resultado.Destino.CodigoIATA;

            // 2. Ruta especial con escala dominical UIO <-> GPS
            bool esIdaGalapagos = (iataO == "UIO" && iataD == "GPS");
            bool esRetornoGalapagos = (iataO == "GPS" && iataD == "UIO");
            bool esItinerario = esIdaGalapagos || esRetornoGalapagos;
            bool esDomingo = resultado.Fecha.DayOfWeek == DayOfWeek.Sunday;
            resultado.EsItinerario = esItinerario;

            if (esItinerario && !esDomingo)
            {
                resultado.Error = "La ruta con escala técnica Quito – Galápagos opera exclusivamente los domingos.";
                return resultado;
            }

            if (!esItinerario && esDomingo)
            {
                resultado.Error = "Los vuelos directos operan de lunes a sábado. Los domingos están reservados para Galápagos.";
                return resultado;
            }

            // 3. Consulta base: todos los vuelos de la fecha. Los cancelados, sin aeronave, sin cupo
            //    o ya partidos se muestran igual, y la vista los marca como no disponibles (EsVendible)
            int oId = origenId.Value;
            int dId = destinoId.Value;
            DateTime fechaBusqueda = resultado.Fecha;

            var consulta = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave)
                .Include(v => v.ReservaPasajeroVuelo.Select(r => r.Reserva))
                .Where(v => v.FechaOperacion == fechaBusqueda);

            if (esItinerario)
            {
                // Tramo 1: origen → GYE   ·   Tramo 2: GYE → destino   (mismo domingo)
                // Se compara por Id (en el .edmx Aeropuerto = DESTINO, Aeropuerto1 = ORIGEN)
                int gyeId = db.Aeropuerto.Where(a => a.CodigoIATA == "GYE").Select(a => a.AeropuertoId).FirstOrDefault();

                var vuelosDia = consulta.OrderBy(v => v.Frecuencia.HoraSalida).ToList();

                var tramo1 = vuelosDia.FirstOrDefault(v => v.Frecuencia.Ruta.AeropuertoOrigenId == oId
                                                        && v.Frecuencia.Ruta.AeropuertoDestinoId == gyeId);
                var tramo2 = vuelosDia.FirstOrDefault(v => v.Frecuencia.Ruta.AeropuertoOrigenId == gyeId
                                                        && v.Frecuencia.Ruta.AeropuertoDestinoId == dId);

                // El itinerario solo se ofrece completo (DP-09: los tramos no se venden sueltos)
                if (tramo1 != null && tramo2 != null)
                {
                    resultado.Vuelos.Add(tramo1);
                    resultado.Vuelos.Add(tramo2);
                }
            }
            else
            {
                // Vuelos directos de lunes a sábado (los tramos del domingo no se venden sueltos)
                resultado.Vuelos = consulta
                    .Where(v => v.Frecuencia.DiaSemana != 7
                             && v.Frecuencia.Ruta.AeropuertoOrigenId == oId
                             && v.Frecuencia.Ruta.AeropuertoDestinoId == dId)
                    .OrderBy(v => v.Frecuencia.HoraSalida)
                    .ToList();
            }

            return resultado;
        }

        // =====================================================================
        // PROGRAMACIÓN DE VUELOS (Pantalla 18)
        // =====================================================================

        // Revisa los vuelos de cada aeronave y devuelve la lista de conflictos y advertencias.
        // Cada elemento: [0] = "CONFLICTO" o "ADVERTENCIA", [1] = texto.
        public List<string[]> BuscarConflictos(List<Vuelo> vuelos)
        {
            int tiempoEnTierra = Utilidades.LeerParametro("TiempoMinimoEnTierraMin", 45);
            var resultado = new List<string[]>();

            var porAeronave = vuelos
                .Where(v => v.AeronaveId != null && v.EstadoVueloCodigo != "CANCELADO")
                .GroupBy(v => v.Aeronave.Matricula);

            foreach (var grupo in porAeronave)
            {
                var lista = grupo.OrderBy(v => Salida(v)).ToList();
                for (int i = 1; i < lista.Count; i++)
                {
                    var anterior = lista[i - 1];
                    var actual = lista[i];
                    string codAnt = "VIP" + anterior.VueloId.ToString("D5");
                    string codAct = "VIP" + actual.VueloId.ToString("D5");
                    string destinoAnt = anterior.CodigoDestino;
                    string origenAct = actual.CodigoOrigen;

                    if (anterior.FechaOperacion == actual.FechaOperacion)
                    {
                        double minutos = (Salida(actual) - Llegada(anterior)).TotalMinutes;
                        if (minutos < tiempoEnTierra)
                        {
                            resultado.Add(new[] { "CONFLICTO", "La aeronave " + grupo.Key + " tiene " + codAnt + " y " + codAct
                                + " el " + actual.FechaOperacion.ToString("dd/MM/yyyy") + " con solo " + minutos + " min en tierra (mínimo " + tiempoEnTierra + ")." });
                        }
                        if (destinoAnt != origenAct)
                        {
                            resultado.Add(new[] { "CONFLICTO", "La aeronave " + grupo.Key + " termina " + codAnt + " en " + destinoAnt
                                + " pero " + codAct + " sale de " + origenAct + " el mismo día." });
                        }
                    }
                    else if (destinoAnt != origenAct)
                    {
                        resultado.Add(new[] { "ADVERTENCIA", "La aeronave " + grupo.Key + " pernocta en " + destinoAnt + " tras " + codAnt
                            + " (" + anterior.FechaOperacion.ToString("dd/MM") + ") y su siguiente vuelo " + codAct + " sale de " + origenAct
                            + " (" + actual.FechaOperacion.ToString("dd/MM") + "). Requiere traslado sin pasajeros." });
                    }
                }
            }
            return resultado;
        }

        // Cambia la aeronave o el estado de un vuelo aplicando las reglas operativas.
        // Devuelve null si se guardó (mensajeExito queda con el texto para mostrar), o el mensaje de error.
        // volverAEdicion indica si, ante un error, se debe reabrir el formulario de edición.
        public string ActualizarVuelo(int vueloId, int? aeronaveId, string estadoVueloCodigo,
                                      out string mensajeExito, out bool volverAEdicion)
        {
            mensajeExito = null;
            volverAEdicion = false;

            var vuelo = db.Vuelo.Find(vueloId);
            if (vuelo == null)
                return "El vuelo no existe.";

            string codigo = "VIP" + vuelo.VueloId.ToString("D5");

            // No se modifican vuelos COMPLETADOS
            if (vuelo.EstadoVueloCodigo == "COMPLETADO")
                return "El vuelo " + codigo + " está completado y no puede modificarse.";

            volverAEdicion = true;

            if (string.IsNullOrEmpty(estadoVueloCodigo) || !db.EstadoVuelo.Any(e => e.Codigo == estadoVueloCodigo))
                return "Seleccione un estado válido.";

            bool cambiaAeronave = vuelo.AeronaveId != aeronaveId;

            // RG-05: un vuelo cancelado no admite cambio de aeronave
            if (cambiaAeronave && vuelo.EstadoVueloCodigo == "CANCELADO")
                return "El vuelo " + codigo + " está cancelado y no admite cambios de aeronave.";

            // Un vuelo con asientos vendidos no puede quedar sin aeronave (RG-03 / RG-04)
            int asientosVigentes = vuelo.ReservaPasajeroVuelo.Count(a => a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA");
            if (!aeronaveId.HasValue && asientosVigentes > 0)
                return "El vuelo " + codigo + " tiene " + asientosVigentes + " asientos vigentes; no puede quedar sin aeronave.";

            // RG-06 y RG-07: tiempo en tierra y continuidad con los vuelos del mismo día de esa aeronave
            if (aeronaveId.HasValue && cambiaAeronave && estadoVueloCodigo != "CANCELADO")
            {
                string conflicto = ValidarAsignacionAeronave(vuelo, aeronaveId.Value);
                if (conflicto != null)
                    return conflicto;
            }

            vuelo.AeronaveId = aeronaveId;
            vuelo.EstadoVueloCodigo = estadoVueloCodigo;

            try
            {
                db.SaveChanges();   // el trigger trg_Vuelo_Validar revalida día, aeronave disponible y superposición
            }
            catch (Exception ex)
            {
                return "No se guardó el cambio: " + Utilidades.MensajeInterno(ex);
            }

            string aviso = (estadoVueloCodigo == "CANCELADO" && asientosVigentes > 0)
                ? " Atención: el vuelo tiene " + asientosVigentes + " asientos vigentes; revise las reservas afectadas en la pantalla 20."
                : "";
            mensajeExito = "Cambios guardados en el vuelo " + codigo + "." + aviso;
            return null;
        }

        // RG-06 y RG-07: la aeronave debe tener el tiempo mínimo en tierra y continuidad
        // de aeropuerto con su vuelo anterior y siguiente del mismo día.
        // Devuelve null si la asignación es válida, o el mensaje del conflicto.
        private string ValidarAsignacionAeronave(Vuelo vuelo, int aeronaveId)
        {
            int tiempoEnTierra = Utilidades.LeerParametro("TiempoMinimoEnTierraMin", 45);
            var mismoDia = db.Vuelo
                .Where(v => v.AeronaveId == aeronaveId && v.FechaOperacion == vuelo.FechaOperacion
                         && v.VueloId != vuelo.VueloId && v.EstadoVueloCodigo != "CANCELADO")
                .ToList();

            var anterior = mismoDia.Where(v => Salida(v) < Salida(vuelo)).OrderByDescending(v => Salida(v)).FirstOrDefault();
            var siguiente = mismoDia.Where(v => Salida(v) > Salida(vuelo)).OrderBy(v => Salida(v)).FirstOrDefault();
            string matricula = db.Aeronave.Find(aeronaveId).Matricula;

            if (anterior != null)
            {
                double minutos = (Salida(vuelo) - Llegada(anterior)).TotalMinutes;
                if (minutos < tiempoEnTierra || anterior.Frecuencia.Ruta.AeropuertoDestinoId != vuelo.Frecuencia.Ruta.AeropuertoOrigenId)
                {
                    return "Conflicto operativo: la aeronave " + matricula + " ya tiene el vuelo VIP" + anterior.VueloId.ToString("D5")
                        + " (" + anterior.Frecuencia.HoraSalida.ToString(@"hh\:mm") + "–" + anterior.Frecuencia.HoraLlegada.ToString(@"hh\:mm")
                        + ") y no cumple el tiempo en tierra o la continuidad de aeropuerto. No se guardó la asignación.";
                }
            }
            if (siguiente != null)
            {
                double minutos = (Salida(siguiente) - Llegada(vuelo)).TotalMinutes;
                if (minutos < tiempoEnTierra || vuelo.Frecuencia.Ruta.AeropuertoDestinoId != siguiente.Frecuencia.Ruta.AeropuertoOrigenId)
                {
                    return "Conflicto operativo: la aeronave " + matricula + " ya tiene el vuelo VIP" + siguiente.VueloId.ToString("D5")
                        + " (" + siguiente.Frecuencia.HoraSalida.ToString(@"hh\:mm") + "–" + siguiente.Frecuencia.HoraLlegada.ToString(@"hh\:mm")
                        + ") y no cumple el tiempo en tierra o la continuidad de aeropuerto. No se guardó la asignación.";
                }
            }
            return null;
        }

        // Genera los vuelos del rango a partir de las frecuencias (usp_GenerarVuelos).
        // Devuelve null si terminó (mensajeExito queda con el resultado), o el mensaje de error.
        public string GenerarVuelos(DateTime? desde, DateTime? hasta, out string mensajeExito)
        {
            mensajeExito = null;

            if (!desde.HasValue || !hasta.HasValue || hasta.Value < desde.Value || (hasta.Value - desde.Value).TotalDays > 365)
                return "Indique un rango de fechas válido (máximo 366 días) para generar vuelos.";

            try
            {
                int antes = db.Vuelo.Count();
                db.Database.ExecuteSqlCommand("EXEC dbo.usp_GenerarVuelos @p0, @p1", desde.Value.Date, hasta.Value.Date);
                int generados = db.Vuelo.Count() - antes;

                mensajeExito = generados > 0
                    ? "Se generaron " + generados + " vuelos. Quedan PROGRAMADOS y sin aeronave hasta que se asignen."
                    : "No se generaron vuelos nuevos: el rango ya estaba programado.";
                return null;
            }
            catch (Exception ex)
            {
                return "No se pudieron generar los vuelos: " + Utilidades.MensajeInterno(ex);
            }
        }
    }
}