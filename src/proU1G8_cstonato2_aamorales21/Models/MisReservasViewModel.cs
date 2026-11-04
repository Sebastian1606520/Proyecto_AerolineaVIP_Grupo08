using System;
using System.Collections.Generic;

namespace Con_Autenticacion.Models
{
    // ViewModel de la pantalla 13 (Mis reservas).
    // La vista solo muestra estos datos: los cálculos los hace ReservaServicio.ObtenerMisReservas.
    public class MisReservasViewModel
    {
        // Filtros (para conservarlos en el formulario y en la paginación)
        public string Desde { get; set; }
        public string Hasta { get; set; }
        public string Estado { get; set; }
        public string ErrorFiltro { get; set; }
        public bool HayFiltros { get; set; }

        // Paginación
        public int Pagina { get; set; }
        public int TotalPaginas { get; set; }
        public int TotalRegistros { get; set; }

        // Reservas de la página actual
        public List<ReservaResumen> Reservas { get; set; } = new List<ReservaResumen>();
    }

    // Una fila de "Mis reservas", con los datos ya calculados
    public class ReservaResumen
    {
        public int ReservaId { get; set; }
        public string CodigoReserva { get; set; }
        public string FechaViaje { get; set; }
        public string Ruta { get; set; }
        public string Escala { get; set; }
        public int NumPasajeros { get; set; }

        public string EstadoReserva { get; set; }   // estado efectivo: una pendiente vencida es EXPIRADA (DP-07)
        public string EstadoCompra { get; set; }    // PAGADA / PENDIENTE / ANULADA / "" (sin compra)

        public bool PuedePagar { get; set; }
        public bool PuedeCancelar { get; set; }     // DP-08

        // Si la reserva está pendiente y vigente, hasta cuándo se puede pagar
        public DateTime? ExpiraEl { get; set; }
    }
}
