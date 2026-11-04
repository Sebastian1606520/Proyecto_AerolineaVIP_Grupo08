using System;
using System.Configuration;
using System.Linq;

namespace Con_Autenticacion.Models
{
    public partial class Reserva
    {
        public bool EstaVencida =>
            EstadoReservaCodigo == "PENDIENTE" &&
            FechaExpiracion.HasValue &&
            FechaExpiracion.Value < DateTime.Now;

        public string EstadoEfectivo =>
            EstaVencida ? "EXPIRADA" : EstadoReservaCodigo;

        // DP-07: las reservas pendientes vencidas pasan a EXPIRADA y sus asientos a LIBERADA.
        // Así el asiento que el mapa ya muestra en verde también queda libre en la base
        // (si no, el índice UX_RPV_AsientoActivo impediría reservarlo de nuevo).
        public static void LiberarVencidas(AerolineaVIP_Context db)
        {
            DateTime ahora = DateTime.Now;
            var vencidas = db.Reserva
                .Where(r => r.EstadoReservaCodigo == "PENDIENTE"
                         && r.FechaExpiracion.HasValue && r.FechaExpiracion < ahora)
                .ToList();

            if (!vencidas.Any()) return;

            foreach (var r in vencidas)
            {
                foreach (var a in r.ReservaPasajeroVuelo.Where(x => x.EstadoAsignacionCodigo == "PENDIENTE"))
                {
                    a.EstadoAsignacionCodigo = "LIBERADA";
                }
                r.EstadoReservaCodigo = "EXPIRADA";
            }
            db.SaveChanges();
        }

        // DP-08: para cancelar una reserva pagada (CONFIRMADA) el motivo es obligatorio
        public bool RequiereMotivoCancelacion => EstadoReservaCodigo == "CONFIRMADA";

        public string EvaluarCancelacion(out bool conReembolso, out double horasRestantes)
        {
            conReembolso = false;
            horasRestantes = 0;

            int limiteHoras = 48;
            if (int.TryParse(ConfigurationManager.AppSettings["LimiteReembolsoHoras"], out int cfg))
            {
                limiteHoras = cfg;
            }

            var primerVuelo = ReservaPasajeroVuelo?
                .Select(a => a.Vuelo)
                .OrderBy(v => v.FechaOperacion)
                .ThenBy(v => v.Frecuencia.HoraSalida)
                .FirstOrDefault();

            if (primerVuelo == null)
                return "La reserva no tiene tramos asociados.";

            DateTime salida = primerVuelo.FechaOperacion.Add(primerVuelo.Frecuencia.HoraSalida);
            horasRestantes = (salida - DateTime.Now).TotalHours;

            if (EstadoReservaCodigo == "CANCELADA")
                return "La reserva ya se encuentra cancelada.";

            if (EstaVencida || EstadoReservaCodigo == "EXPIRADA")
                return "La reserva expiró; sus asientos ya fueron liberados.";

            if (horasRestantes <= 0)
                return "El vuelo ya salió; la reserva no puede cancelarse.";

            bool tieneCheckIn = ReservaPasajeroVuelo
                .Any(a => a.CheckIn.Any(c => c.EstadoCheckInCodigo == "COMPLETADO"));
            if (tieneCheckIn)
                return "La reserva tiene check-in completado; ya no puede cancelarse.";

            if (EstadoReservaCodigo == "CONFIRMADA")
                conReembolso = horasRestantes >= limiteHoras;

            return null;
        }
    }
}