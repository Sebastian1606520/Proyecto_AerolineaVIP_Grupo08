using System;
using System.Collections.Generic;
using System.Linq;

namespace Con_Autenticacion.Models
{
    /// <summary>
    /// Modelo auxiliar para la representación visual y estado de cada asiento en la cabina
    /// </summary>
    public class AsientoInfo
    {
        public int Numero { get; set; }
        public string Codigo { get; set; }
        public string Ubicacion { get; set; }
        public string Estado { get; set; } // "disponible", "reservado", "ocupado"
        public string GridClass { get; set; }
        public bool EstaDisponible => Estado == "disponible";
    }

    /// <summary>
    /// Lógica de negocio y propiedades calculadas para la entidad Vuelo
    /// </summary>
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

        // Regla: Asientos actualmente ocupados o reservados
        public int AsientosOcupados
        {
            get
            {
                if (ReservaPasajeroVuelo == null) return 0;
                return ReservaPasajeroVuelo.Count(r =>
                    r.EstadoAsignacionCodigo == "PENDIENTE" ||
                    r.EstadoAsignacionCodigo == "CONFIRMADA");
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

        // Regla: Distribución y estado de los 7 asientos del Phenom 300E para vistas directas
        public List<AsientoInfo> AsientosCabina
        {
            get
            {
                var mapa = new[]
                {
                    new { N = 1, C = "1D", U = "ventanilla derecha, fila 1", Cls = "seat-1d" },
                    new { N = 2, C = "2D", U = "ventanilla derecha, fila 2", Cls = "seat-2d" },
                    new { N = 4, C = "3D", U = "ventanilla derecha, fila 3", Cls = "seat-3d" },
                    new { N = 6, C = "4D", U = "ventanilla derecha, fila 4", Cls = "seat-4d" },
                    new { N = 3, C = "2I", U = "ventanilla izquierda, fila 2", Cls = "seat-2i" },
                    new { N = 5, C = "3I", U = "ventanilla izquierda, fila 3", Cls = "seat-3i" },
                    new { N = 7, C = "4I", U = "ventanilla izquierda, fila 4", Cls = "seat-4i" }
                };

                var lista = new List<AsientoInfo>();
                foreach (var s in mapa)
                {
                    string estado = "disponible";
                    if (ReservaPasajeroVuelo != null)
                    {
                        if (ReservaPasajeroVuelo.Any(r => r.NumeroAsiento == s.N && r.EstadoAsignacionCodigo == "CONFIRMADA"))
                        {
                            estado = "ocupado";
                        }
                        else if (ReservaPasajeroVuelo.Any(r => r.NumeroAsiento == s.N && r.EstadoAsignacionCodigo == "PENDIENTE"))
                        {
                            estado = "reservado";
                        }
                    }

                    lista.Add(new AsientoInfo
                    {
                        Numero = s.N,
                        Codigo = s.C,
                        Ubicacion = s.U,
                        Estado = estado,
                        GridClass = s.Cls
                    });
                }

                return lista;
            }
        }
    }
}