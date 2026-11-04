using Con_Autenticacion.Models;
using Con_Autenticacion.Negocio;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Web.Mvc;

namespace Con_Autenticacion.Controllers
{
    // Consulta e impresión de comprobantes (Pantalla 16 · MOD-08). Ruta: /Comprobantes/Index (wireframe).
    // El cliente solo ve sus comprobantes; Counter, Administrador y SuperUsuario ven todos.
    [Authorize]
    public class ComprobantesController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // =========================================================================
        // PANTALLA 16: VISUALIZACIÓN DE COMPROBANTES (/Comprobantes/Index)
        // =========================================================================
        [HttpGet]
        public ActionResult Index(DateTime? desde, DateTime? hasta, string tipo, int? ver)
        {
            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;
            bool esPersonal = User.IsInRole("Counter") || User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");

            // Rango de fechas inválido
            if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            {
                ViewBag.ErrorFiltro = "La fecha «Hasta» no puede ser anterior a la fecha «Desde».";
                hasta = null;
            }

            // Tipos válidos del filtro
            var tiposValidos = new List<string> { "CONFIRMACION", "FACTURA", "NOTA_CREDITO" };
            if (!string.IsNullOrEmpty(tipo) && !tiposValidos.Contains(tipo))
            {
                tipo = null;
            }

            var consulta = db.Comprobante
                .Include(c => c.Compra.Reserva)
                .Include(c => c.TipoComprobante)
                .AsQueryable();

            // Titularidad: el cliente solo ve lo suyo
            if (!esPersonal)
            {
                consulta = consulta.Where(c => c.Compra.Reserva.UsuarioId == currentUserId);
            }

            if (desde.HasValue)
            {
                DateTime d = desde.Value.Date;
                consulta = consulta.Where(c => c.FechaEmision >= d);
            }
            if (hasta.HasValue)
            {
                DateTime h = hasta.Value.Date.AddDays(1);   // incluye todo el día "hasta"
                consulta = consulta.Where(c => c.FechaEmision < h);
            }
            if (!string.IsNullOrEmpty(tipo))
            {
                consulta = consulta.Where(c => c.TipoComprobanteCodigo == tipo);
            }

            var comprobantes = consulta
                .OrderByDescending(c => c.FechaEmision)
                .ThenBy(c => c.TipoComprobanteCodigo)
                .ToList();

            // Datos para conservar los filtros
            ViewBag.Desde = desde.HasValue ? desde.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Hasta = hasta.HasValue ? hasta.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Tipo = tipo ?? "";
            ViewBag.HayFiltros = desde.HasValue || hasta.HasValue || !string.IsNullOrEmpty(tipo);

            // ---- Vista previa del comprobante seleccionado ----
            Comprobante seleccionado = null;
            if (ver.HasValue)
            {
                // Solo si está dentro de los comprobantes permitidos para este usuario
                seleccionado = comprobantes.FirstOrDefault(c => c.ComprobanteId == ver.Value);
                if (seleccionado == null)
                {
                    seleccionado = db.Comprobante.Find(ver.Value);
                    if (seleccionado != null && !esPersonal && seleccionado.Compra.Reserva.UsuarioId != currentUserId)
                    {
                        seleccionado = null;
                    }
                }
                if (seleccionado == null)
                {
                    ViewBag.ErrorFiltro = "No se encontró el comprobante solicitado.";
                }
            }

            ViewBag.Seleccionado = seleccionado;

            if (seleccionado != null)
            {
                var compra = seleccionado.Compra;
                var reserva = compra.Reserva;

                // Emisor, reserva, total y pago simulado desde la vista SQL vw_ComprobanteDetalle
                var encabezado = new PagoServicio(db).ObtenerEncabezadoComprobante(seleccionado.ComprobanteId);
                ViewBag.Encabezado = encabezado;
                ViewBag.HayPago = PagoServicio.TienePagoVigente(encabezado);

                // Cliente (base de Identity)
                using (var identity = new ApplicationDbContext())
                {
                    var usuario = identity.Users.FirstOrDefault(u => u.Id == reserva.UsuarioId);
                    ViewBag.ClienteNombre = usuario != null ? usuario.UserName : "Usuario de la reserva";
                    ViewBag.ClienteCorreo = usuario != null ? usuario.Email : "";
                }

                // Detalle por pasajero y tramo, con el código de asiento de la tabla Asiento
                var detalle = reserva.ReservaPasajeroVuelo
                    .OrderBy(a => a.Vuelo.FechaOperacion).ThenBy(a => a.Vuelo.Frecuencia.HoraSalida)
                    .ThenBy(a => a.Pasajero.Apellidos)
                    .ToList();

                var asientos = db.Asiento.ToList();
                var codigos = new Dictionary<int, string>();
                foreach (var a in detalle)
                {
                    int modeloId = a.Vuelo.Aeronave != null ? a.Vuelo.Aeronave.ModeloAeronaveId : 0;
                    var asiento = asientos.FirstOrDefault(s => s.NumeroAsiento == a.NumeroAsiento && (modeloId == 0 || s.ModeloAeronaveId == modeloId));
                    codigos[a.AsignacionId] = asiento != null ? asiento.Codigo : a.NumeroAsiento.ToString();
                }

                ViewBag.Detalle = detalle;
                new VueloServicio(db).CargarDetalle(detalle.Select(a => a.Vuelo));   // vista SQL vw_VueloDetalle
                ViewBag.CodigosAsiento = codigos;
            }

            return View(comprobantes);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
