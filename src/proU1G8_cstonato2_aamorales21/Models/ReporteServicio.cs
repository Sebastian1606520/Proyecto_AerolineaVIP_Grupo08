using Con_Autenticacion.Models;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace Con_Autenticacion.Negocio
{
    // Consultas y reportes administrativos (Pantalla 21 · MOD-10).
    // Ocupación por vuelo, reservas por estado y compras / pagos simulados.
    public class ReporteServicio
    {
        private readonly AerolineaVIP_Context db;

        public ReporteServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // Rango de fechas del reporte: por defecto 30 días atrás y 30 adelante.
        // Corrige rangos inválidos (Hasta < Desde, más de 366 días).
        // Devuelve null si el rango era válido, o el aviso para mostrar en el filtro.
        public string NormalizarRango(DateTime? desde, DateTime? hasta, out DateTime fDesde, out DateTime fHasta)
        {
            fDesde = (desde ?? DateTime.Today.AddDays(-30)).Date;
            fHasta = (hasta ?? DateTime.Today.AddDays(30)).Date;
            string aviso = null;

            if (fHasta < fDesde)
            {
                aviso = "La fecha «Hasta» no puede ser anterior a la fecha «Desde».";
                fHasta = fDesde.AddDays(30);
            }
            if ((fHasta - fDesde).TotalDays > 366)
            {
                aviso = "El rango no puede superar 366 días.";
                fHasta = fDesde.AddDays(366);
            }
            return aviso;
        }

        // ---------------- OCUPACIÓN POR VUELO ----------------
        // Devuelve los vuelos del rango y, por vuelo, los asientos reservados (PENDIENTE)
        // y ocupados (CONFIRMADA).
        public List<Vuelo> OcupacionPorVuelo(DateTime fDesde, DateTime fHasta, int? rutaId, int? aeronaveId, string estado,
                                             out Dictionary<int, int> reservados, out Dictionary<int, int> ocupados)
        {
            var vuelos = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave.ModeloAeronave)
                .Where(v => v.FechaOperacion >= fDesde && v.FechaOperacion <= fHasta);

            if (rutaId.HasValue) vuelos = vuelos.Where(v => v.Frecuencia.RutaId == rutaId.Value);
            if (aeronaveId.HasValue) vuelos = vuelos.Where(v => v.AeronaveId == aeronaveId.Value);
            if (!string.IsNullOrEmpty(estado)) vuelos = vuelos.Where(v => v.EstadoVueloCodigo == estado);

            var lista = vuelos.ToList()
                .OrderBy(v => v.FechaOperacion).ThenBy(v => v.Frecuencia.HoraSalida)
                .ToList();

            var ids = lista.Select(v => v.VueloId).ToList();
            var asignaciones = db.ReservaPasajeroVuelo
                .Where(a => ids.Contains(a.VueloId) && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA"))
                .Select(a => new { a.VueloId, a.EstadoAsignacionCodigo })
                .ToList();

            reservados = asignaciones.Where(a => a.EstadoAsignacionCodigo == "PENDIENTE")
                .GroupBy(a => a.VueloId).ToDictionary(g => g.Key, g => g.Count());
            ocupados = asignaciones.Where(a => a.EstadoAsignacionCodigo == "CONFIRMADA")
                .GroupBy(a => a.VueloId).ToDictionary(g => g.Key, g => g.Count());

            return lista;
        }

        // Capacidad y disponibles por vuelo desde la vista SQL
        // dbo.vw_DisponibilidadVuelo (wireframe, pantalla 21). Clave = VueloId.
        public Dictionary<int, vw_DisponibilidadVuelo> ObtenerDisponibilidad(DateTime fDesde, DateTime fHasta)
        {
            Reserva.LiberarVencidas(db);   // DP-07: lo vencido no cuenta como ocupado

            return db.vw_DisponibilidadVuelo
                .AsNoTracking()
                .Where(d => d.FechaOperacion >= fDesde && d.FechaOperacion <= fHasta)
                .ToList()
                .ToDictionary(d => d.VueloId);
        }

        // ---------------- RESERVAS POR ESTADO ----------------
        // Devuelve las reservas creadas en el rango y un resumen por estado:
        // cada elemento = [estado, cantidad, pasajeros, importe].
        public List<Reserva> ReservasPorEstado(DateTime fDesde, DateTime fHasta, int? rutaId, int? aeronaveId, string estado,
                                               out List<object[]> resumen)
        {
            DateTime hastaExclusivo = fHasta.AddDays(1);   // incluye todo el día "hasta"

            var reservas = db.Reserva
                .Include(r => r.Compra)
                .Where(r => r.FechaCreacion >= fDesde && r.FechaCreacion < hastaExclusivo);

            if (rutaId.HasValue)
                reservas = reservas.Where(r => r.ReservaPasajeroVuelo.Any(a => a.Vuelo.Frecuencia.RutaId == rutaId.Value));
            if (aeronaveId.HasValue)
                reservas = reservas.Where(r => r.ReservaPasajeroVuelo.Any(a => a.Vuelo.AeronaveId == aeronaveId.Value));
            if (!string.IsNullOrEmpty(estado))
                reservas = reservas.Where(r => r.EstadoReservaCodigo == estado);

            var lista = reservas.OrderByDescending(r => r.FechaCreacion).ToList();

            resumen = lista
                .GroupBy(r => r.EstadoReservaCodigo)
                .Select(g => new object[]
                {
                    g.Key,
                    g.Count(),
                    g.Sum(r => r.ReservaPasajeroVuelo.Select(a => a.PasajeroId).Distinct().Count()),
                    g.Sum(r => r.ReservaPasajeroVuelo.Sum(a => a.ImporteTramo))
                })
                .OrderBy(x => (string)x[0])
                .ToList();

            return lista;
        }

        // ---------------- COMPRAS Y PAGOS SIMULADOS ----------------
        public List<Compra> ComprasYPagos(DateTime fDesde, DateTime fHasta, int? rutaId, int? aeronaveId, string estado)
        {
            DateTime hastaExclusivo = fHasta.AddDays(1);   // incluye todo el día "hasta"

            var compras = db.Compra
                .Include(c => c.Reserva)
                .Include(c => c.Pago)
                .Where(c => c.FechaCompra >= fDesde && c.FechaCompra < hastaExclusivo);

            if (rutaId.HasValue)
                compras = compras.Where(c => c.Reserva.ReservaPasajeroVuelo.Any(a => a.Vuelo.Frecuencia.RutaId == rutaId.Value));
            if (aeronaveId.HasValue)
                compras = compras.Where(c => c.Reserva.ReservaPasajeroVuelo.Any(a => a.Vuelo.AeronaveId == aeronaveId.Value));
            if (!string.IsNullOrEmpty(estado))
                compras = compras.Where(c => c.EstadoCompraCodigo == estado);

            return compras.OrderByDescending(c => c.FechaCompra).ToList();
        }
    }
}