using System;
using System.Linq;

namespace Con_Autenticacion.Models
{
    // La palabra 'partial' fusiona esta clase con la que genera el .edmx
    public partial class Vuelo
    {
        // Regla: Asientos libres considerando la capacidad fija de 7 asientos (Embraer Phenom 300E)
        public int AsientosDisponibles
        {
            get
            {
                if (ReservaPasajeroVuelo == null) return 7;
                int ocupados = ReservaPasajeroVuelo.Count(r =>
                    r.EstadoAsignacionCodigo == "PENDIENTE" ||
                    r.EstadoAsignacionCodigo == "CONFIRMADA");
                return Math.Max(0, 7 - ocupados);
            }
        }

        // Regla: Saber si el vuelo ya partió combinando su fecha de operación y la hora de salida
        public bool YaPartio
        {
            get
            {
                if (Frecuencia == null) return false;
                DateTime fechaSalida = FechaOperacion.Date.Add(Frecuencia.HoraSalida);
                return fechaSalida <= DateTime.Now;
            }
        }

        // Regla: Valida si es vendible (aeronave asignada, no cancelado, con asientos y no despegado)
        public bool EsVendible(int pasajerosSolicitados = 1)
        {
            return !YaPartio &&
                   EstadoVueloCodigo != "CANCELADO" &&
                   AeronaveId != null &&
                   AsientosDisponibles >= pasajerosSolicitados;
        }

        // Regla: Jornada técnica para el filtrado en cliente
        public string Jornada
        {
            get
            {
                if (Frecuencia == null) return "manana";
                if (Frecuencia.HoraSalida.Hours < 12) return "manana";
                if (Frecuencia.HoraSalida.Hours < 18) return "tarde";
                return "noche";
            }
        }

        // Regla: Etiqueta textual del turno
        public string DescripcionJornada
        {
            get
            {
                if (Frecuencia == null) return "Vuelo regular";
                if (Frecuencia.HoraSalida.Hours < 12) return "Vuelo de mañana";
                if (Frecuencia.HoraSalida.Hours < 18) return "Vuelo de tarde";
                return "Vuelo nocturno";
            }
        }
    }
}