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
        public string Color { get; set; }  // ColorEstado de la vista SQL (Verde / Morado / Rojo)
        public bool EstaDisponible => Estado == "disponible";
    }

    public partial class Vuelo
    {
        public const int CapacidadMaximaVIP = 7;

        // ---- Datos leídos de la vista SQL vw_VueloDetalle ----
        // Los carga VueloServicio.CargarDetalle. No son columnas de la tabla Vuelo:
        // Entity Framework los ignora.
        public string CodigoVueloVista { get; set; }
        public string OrigenVista { get; set; }
        public string DestinoVista { get; set; }
        public string CiudadOrigenVista { get; set; }
        public string CiudadDestinoVista { get; set; }

        // Código, origen y destino del vuelo. Si se cargó la vista SQL, vienen de ella;
        // si no, de la ruta. OJO: en el .edmx, Ruta.Aeropuerto1 = ORIGEN y Ruta.Aeropuerto = DESTINO.
        public string CodigoVuelo => CodigoVueloVista ?? ("VIP" + VueloId.ToString("D5"));
        public string CodigoOrigen => OrigenVista ?? Frecuencia.Ruta.Aeropuerto1.CodigoIATA;
        public string CodigoDestino => DestinoVista ?? Frecuencia.Ruta.Aeropuerto.CodigoIATA;
        public string CiudadOrigen => CiudadOrigenVista ?? Frecuencia.Ruta.Aeropuerto1.Localidad;
        public string CiudadDestino => CiudadDestinoVista ?? Frecuencia.Ruta.Aeropuerto.Localidad;

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

        // Disponibles leídos de la vista SQL vw_DisponibilidadVuelo.
        // Lo carga VueloServicio.CargarDisponibilidad (pantallas 6, 7 y 8).
        // No es una columna de la tabla Vuelo: Entity Framework la ignora.
        public int? DisponiblesDesdeVista { get; set; }

        // Si se cargó la vista SQL, el dato viene de la base; si no, se calcula en C#
        public int AsientosDisponibles => DisponiblesDesdeVista.HasValue
            ? Math.Max(0, DisponiblesDesdeVista.Value)
            : Math.Max(0, CapacidadMaximaVIP - AsientosOcupados);

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

        // Estado de los asientos leído de la vista SQL vw_EstadoAsientoVuelo.
        // Lo carga VueloServicio.CargarEstadoAsientos (pantallas 7 y 8).
        // No es una columna de la tabla Vuelo: Entity Framework la ignora.
        public List<AsientoInfo> AsientosDesdeVista { get; set; }

        // Distribución técnica de cabina Phenom 300E
        public List<AsientoInfo> AsientosCabina
        {
            get
            {
                // Si se cargó la vista SQL, los estados y colores vienen de la base de datos
                if (AsientosDesdeVista != null)
                    return AsientosDesdeVista;

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