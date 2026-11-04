using System;
using System.Collections.Generic;
using System.Linq;

namespace Con_Autenticacion.Models
{
    public class AsientoInfo
    {
        public int Numero { get; set; }
        public string Codigo { get; set; }
        public string Ubicacion { get; set; }
        public string Estado { get; set; } // "disponible", "reservado", "ocupado"
        public string GridClass { get; set; }
        public bool EstaDisponible => Estado == "disponible";
    }

    public partial class Vuelo
    {
        public const int CapacidadMaximaVIP = 7;

        // Regla: Un asiento solo bloquea cupo si está CONFIRMADA o si está PENDIENTE y aún NO expira
        public int AsientosOcupados
        {
            get
            {
                if (ReservaPasajeroVuelo == null) return 0;
                DateTime ahora = DateTime.Now;

                return ReservaPasajeroVuelo.Count(r =>
                    r.EstadoAsignacionCodigo == "CONFIRMADA" ||
                    (r.EstadoAsignacionCodigo == "PENDIENTE" &&
                     r.Reserva != null &&
                     r.Reserva.FechaExpiracion.HasValue &&
                     r.Reserva.FechaExpiracion.Value > ahora));
            }
        }

        public int AsientosDisponibles => Math.Max(0, CapacidadMaximaVIP - AsientosOcupados);

        public bool YaPartio
        {
            get
            {
                if (Frecuencia == null) return false;
                DateTime fechaSalida = FechaOperacion.Date.Add(Frecuencia.HoraSalida);
                return fechaSalida <= DateTime.Now;
            }
        }

        public bool PermiteVentaPorHorario
        {
            get
            {
                if (Frecuencia == null) return false;
                DateTime fechaSalida = FechaOperacion.Date.Add(Frecuencia.HoraSalida);
                return (fechaSalida - DateTime.Now).TotalMinutes >= 60;
            }
        }

        public bool EsVendible(int pasajerosSolicitados = 1)
        {
            return !YaPartio &&
                   PermiteVentaPorHorario &&
                   EstadoVueloCodigo != "CANCELADO" &&
                   AeronaveId != null &&
                   AsientosDisponibles >= pasajerosSolicitados;
        }

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

        // Distribución técnica de cabina Phenom 300E
        public List<AsientoInfo> AsientosCabina
        {
            get
            {
                var mapa = new[]
                {
                    new { N = 1, C = "1D", U = "Ventanilla derecha, fila 1", Cls = "seat-1d" },
                    new { N = 2, C = "2D", U = "Mesa VIP derecha, fila 2", Cls = "seat-2d" },
                    new { N = 3, C = "2I", U = "Mesa VIP izquierda, fila 2", Cls = "seat-2i" },
                    new { N = 4, C = "3D", U = "Mesa VIP derecha, fila 3", Cls = "seat-3d" },
                    new { N = 5, C = "3I", U = "Mesa VIP izquierda, fila 3", Cls = "seat-3i" },
                    new { N = 6, C = "4D", U = "Popa derecha, fila 4", Cls = "seat-4d" },
                    new { N = 7, C = "4I", U = "Popa izquierda, fila 4", Cls = "seat-4i" }
                };

                var lista = new List<AsientoInfo>();
                DateTime ahora = DateTime.Now;

                foreach (var s in mapa)
                {
                    string estado = "disponible";

                    if (ReservaPasajeroVuelo != null)
                    {
                        // 1. Ocupado firme por pago aprobado -> ROJO
                        if (ReservaPasajeroVuelo.Any(r => r.NumeroAsiento == s.N && r.EstadoAsignacionCodigo == "CONFIRMADA"))
                        {
                            estado = "ocupado";
                        }
                        // 2. Reservado temporal SOLO si el plazo de 2h sigue vigente -> MORADO
                        else if (ReservaPasajeroVuelo.Any(r => r.NumeroAsiento == s.N &&
                                                              r.EstadoAsignacionCodigo == "PENDIENTE" &&
                                                              (r.Reserva == null || !r.Reserva.FechaExpiracion.HasValue || r.Reserva.FechaExpiracion.Value > ahora)))
                        {
                            estado = "reservado";
                        }
                        // 3. Si expiró, queda como "disponible" (VERDE) automáticamente
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