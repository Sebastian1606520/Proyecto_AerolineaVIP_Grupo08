using System;
using System.Linq;

namespace Con_Autenticacion.Models
{
    public partial class Vuelo
    {
        // Capacidad ejecutiva estándar (Embraer Phenom 300E)
        public const int CapacidadMaximaVIP = 7;

        // Regla: Asientos libres considerando la capacidad fija de 7 asientos
        public int AsientosDisponibles
        {
            get
            {
                if (ReservaPasajeroVuelo == null) return CapacidadMaximaVIP;
                int ocupados = ReservaPasajeroVuelo.Count(r =>
                    r.EstadoAsignacionCodigo == "PENDIENTE" ||
                    r.EstadoAsignacionCodigo == "CONFIRMADA");
                return Math.Max(0, CapacidadMaximaVIP - ocupados);
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

        // Regla: Bloqueo de venta con menos de 60 minutos de anticipación al despegue
        public bool PermiteVentaPorHorario
        {
            get
            {
                if (Frecuencia == null) return false;
                DateTime fechaSalida = FechaOperacion.Date.Add(Frecuencia.HoraSalida);
                return (fechaSalida - DateTime.Now).TotalMinutes >= 60;
            }
        }

        // Regla: Valida si es vendible (aeronave asignada, no cancelado, con asientos, no despegado y con tiempo)
        public bool EsVendible(int pasajerosSolicitados = 1)
        {
            return !YaPartio &&
                   PermiteVentaPorHorario &&
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