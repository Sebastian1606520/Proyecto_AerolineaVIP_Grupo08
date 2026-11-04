using System;
using System.Collections.Generic;
using System.Web.Mvc;

namespace Con_Autenticacion.Models
{
    // ViewModel de la pantalla 21 (Consultas y reportes).
    // La vista solo muestra estos datos: los cálculos los hace ReporteServicio.ArmarReporte.
    public class ReportesViewModel
    {
        // Filtros
        public string Consulta { get; set; }        // ocupacion / reservas / pagos
        public string Desde { get; set; }
        public string Hasta { get; set; }
        public int? RutaId { get; set; }
        public int? AeronaveId { get; set; }
        public string Estado { get; set; }
        public string ErrorFiltro { get; set; }
        public DateTime FechaCorte { get; set; }

        // Listas desplegables de los filtros
        public SelectList RutasLista { get; set; }
        public SelectList AeronavesLista { get; set; }

        // ---- Ocupación por vuelo ----
        public List<FilaOcupacion> Ocupacion { get; set; } = new List<FilaOcupacion>();
        public int TotalCapacidad { get; set; }
        public int TotalReservados { get; set; }
        public int TotalOcupados { get; set; }
        public int TotalDisponibles { get; set; }

        // ---- Reservas por estado ----
        public List<FilaResumenReservas> ResumenReservas { get; set; } = new List<FilaResumenReservas>();
        public List<FilaReserva> Reservas { get; set; } = new List<FilaReserva>();

        // ---- Compras y pagos simulados ----
        public List<FilaPago> Pagos { get; set; } = new List<FilaPago>();
        public decimal TotalCompras { get; set; }
        public decimal TotalAprobado { get; set; }
    }

    public class FilaOcupacion
    {
        public string CodigoVuelo { get; set; }
        public string Fecha { get; set; }
        public string Hora { get; set; }
        public string Ruta { get; set; }
        public string Aeronave { get; set; }
        public int? Capacidad { get; set; }          // null: vuelo sin aeronave (se muestra "—")
        public int Reservados { get; set; }
        public int Ocupados { get; set; }
        public int? Disponibles { get; set; }
        public string Estado { get; set; }
    }

    public class FilaResumenReservas
    {
        public string Estado { get; set; }
        public int Reservas { get; set; }
        public int Pasajeros { get; set; }
        public decimal Importe { get; set; }
    }

    public class FilaReserva
    {
        public int ReservaId { get; set; }
        public string CodigoReserva { get; set; }
        public string Creada { get; set; }
        public string FechaViaje { get; set; }
        public int Pasajeros { get; set; }
        public string Estado { get; set; }
        public string EstadoCompra { get; set; }
        public decimal Importe { get; set; }
    }

    public class FilaPago
    {
        public string CodigoReserva { get; set; }
        public string FechaCompra { get; set; }
        public string EstadoCompra { get; set; }
        public decimal Importe { get; set; }
        public decimal Aprobado { get; set; }
        public int Rechazos { get; set; }
        public string UltimoPago { get; set; }       // null: sin intentos de pago
        public string UltimoPagoOperacion { get; set; }
        public bool Cuadra { get; set; }             // conciliación compra / pago aprobado
    }
}
