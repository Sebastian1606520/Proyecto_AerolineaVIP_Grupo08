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