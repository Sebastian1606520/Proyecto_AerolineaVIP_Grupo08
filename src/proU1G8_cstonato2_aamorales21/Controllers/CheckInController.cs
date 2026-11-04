using Con_Autenticacion.Models;
using Con_Autenticacion.Negocio;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Mvc;

namespace Con_Autenticacion.Controllers
{
    // Check-in en línea (Pantalla 15 · MOD-07). Ruta: /CheckIn/Index (wireframe).
    // La lógica está en CheckInServicio; aquí solo se reciben las peticiones,
    // se verifican permisos y se muestra el resultado.
    [Authorize]
    public class CheckInController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // Titular de la reserva o personal autorizado (Counter / Administrador / SuperUsuario)
        private bool PuedeGestionar(Reserva reserva)
        {
            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;
            return reserva.UsuarioId == currentUserId
                || User.IsInRole("Counter")
                || User.IsInRole("Administrador")
                || User.IsInRole("SuperUsuario");
        }

        // =========================================================================
        // PANTALLA 15: CHECK-IN (/CheckIn/Index)
        // =========================================================================

        // La lógica del check-in está en Negocio/CheckInServicio.cs.
        // Aquí solo se recibe la petición, se verifican permisos y se muestra el resultado.

        // GET: /CheckIn/Index?codigo=RV00000006&documento=1710000015
        [HttpGet]
        public ActionResult Index(string codigo, string documento)
        {
            codigo = (codigo ?? "").Trim().ToUpper();
            documento = (documento ?? "").Trim().ToUpper();
            ViewBag.Codigo = codigo;
            ViewBag.Documento = documento;

            // Paso 1: sin datos todavía, solo se muestra el formulario
            if (codigo == "" && documento == "")
            {
                return View((Reserva)null);
            }

            var servicio = new CheckInServicio(db);

            // Validación de formato en el servidor
            string error = servicio.ValidarFormato(codigo, documento);
            if (error != null)
            {
                ViewBag.ErrorBusqueda = error;
                return View((Reserva)null);
            }

            // RG-37 + permisos (titular o Counter / Administrador / SuperUsuario)
            var reserva = servicio.BuscarReserva(codigo, documento);
            if (reserva == null || !PuedeGestionar(reserva))
            {
                // Mensaje neutral: no se revela si la reserva existe
                ViewBag.ErrorBusqueda = "No se encontró una reserva con esos datos.";
                return View((Reserva)null);
            }

            var elegibilidad = servicio.EvaluarReserva(reserva);
            ViewBag.Elegibilidad = elegibilidad;
            ViewBag.Elegibles = CheckInServicio.ContarElegibles(elegibilidad);
            ViewBag.Asignaciones = servicio.ObtenerAsignacionesOrdenadas(reserva);

            // Ruta de cada tramo desde la vista SQL vw_VueloDetalle
            new VueloServicio(db).CargarDetalle(reserva.ReservaPasajeroVuelo.Select(a => a.Vuelo));
            ViewBag.CodigosAsiento = servicio.ObtenerCodigosAsiento(reserva);
            return View(reserva);
        }

        // POST: /CheckIn/Index  (Confirmar check-in de las asignaciones marcadas)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Index(string codigo, string documento, List<int> asignaciones)
        {
            codigo = (codigo ?? "").Trim().ToUpper();
            documento = (documento ?? "").Trim().ToUpper();

            var servicio = new CheckInServicio(db);

            var reserva = servicio.BuscarReserva(codigo, documento);
            if (reserva == null || !PuedeGestionar(reserva))
            {
                TempData["Error"] = "No se encontró una reserva con esos datos.";
                return RedirectToAction("Index");
            }

            if (asignaciones == null || asignaciones.Count == 0)
            {
                TempData["Error"] = "Seleccione al menos un pasajero para registrar el check-in.";
                return RedirectToAction("Index", new { codigo = codigo, documento = documento });
            }

            var registrados = new List<string>();
            var rechazados = new List<string>();

            try
            {
                servicio.Registrar(reserva, asignaciones, registrados, rechazados);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo registrar el check-in: " + Utilidades.MensajeInterno(ex);
                return RedirectToAction("Index", new { codigo = codigo, documento = documento });
            }

            if (registrados.Any())
                TempData["Exito"] = "Check-in completado para " + string.Join(", ", registrados) + ".";
            if (rechazados.Any())
                TempData["Error"] = "No se pudo registrar el check-in de " + string.Join(", ", rechazados) + " porque ya no es elegible.";

            return RedirectToAction("Index", new { codigo = codigo, documento = documento });
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
