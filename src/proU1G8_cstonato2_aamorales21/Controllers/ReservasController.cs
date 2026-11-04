using Con_Autenticacion.Models;
using Con_Autenticacion.Negocio;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;

namespace Con_Autenticacion.Controllers
{
    [Authorize]
    public class ReservasController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // GET: Reservas  (CRUD de scaffolding)
        [Authorize(Roles = "Details")]
        public ActionResult Index()
        {
            var reserva = db.Reserva.Include(r => r.EstadoReserva);
            return View(reserva.ToList());
        }

        [HttpGet]
        public ActionResult SeleccionAsiento(int? id, int pasajeros = 1)
        {
            if (!id.HasValue)
            {
                TempData["Error"] = "Debe seleccionar un vuelo válido para continuar.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var vuelo = db.Vuelo.Find(id.Value);
            if (vuelo == null)
            {
                TempData["Error"] = "El vuelo seleccionado no está disponible o ha sido cancelado.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var tramos = new ReservaServicio(db).ObtenerTramos(vuelo);
            if (tramos == null)
            {
                TempData["Error"] = "El itinerario de este vuelo no está completo; no puede reservarse.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            int cantidadPasajeros = (pasajeros >= 1 && pasajeros <= 7) ? pasajeros : 1;

            // Misma regla que usa Resultados (VueloNegocio.EsVendible), repetida en el servidor:
            // no cancelado, con aeronave, sin partir, con 60 min de anticipación y cupo en TODOS los tramos
            if (tramos.Any(t => !t.EsVendible(cantidadPasajeros)))
            {
                TempData["Error"] = "Este vuelo ya no está disponible para " + cantidadPasajeros + " pasajero(s): está cancelado, sin aeronave, sin cupo suficiente o muy próximo a salir.";
                return RedirectToAction("Resultados", "Vuelos", new
                {
                    origenId = tramos.First().Frecuencia.Ruta.AeropuertoOrigenId,
                    destinoId = tramos.Last().Frecuencia.Ruta.AeropuertoDestinoId,
                    fecha = vuelo.FechaOperacion.ToString("yyyy-MM-dd"),
                    pasajeros = cantidadPasajeros
                });
            }

            ViewBag.Tramos = tramos;
            ViewBag.Pasajeros = cantidadPasajeros;
            ViewBag.EsItinerario = tramos.Count > 1;
            ViewBag.UltimoTramo = tramos.Last();

            return View(tramos.First());
        }

        // POST: /Reservas/SeleccionAsiento
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SeleccionAsiento(int vueloId, int pasajeros, List<int> asientosSeleccionados, List<int> asientosTramo2)
        {
            Reserva.LiberarVencidas(db);
            var vuelo = db.Vuelo.Find(vueloId);
            var tramos = vuelo != null ? new ReservaServicio(db).ObtenerTramos(vuelo) : null;
            if (tramos == null)
            {
                TempData["Error"] = "Debe seleccionar un vuelo válido para continuar.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var seleccionPorTramo = new List<List<int>> { asientosSeleccionados, asientosTramo2 };

            // Se valida cada tramo en el servidor (RNP-04)
            for (int k = 0; k < tramos.Count; k++)
            {
                var seleccion = seleccionPorTramo[k];
                string tramoTexto = tramos.Count > 1 ? " en el tramo " + (k + 1) : "";

                if (seleccion == null || seleccion.Count != pasajeros)
                {
                    TempData["Error"] = "Debe asignar un asiento a cada pasajero" + tramoTexto + " antes de continuar.";
                    return RedirectToAction("SeleccionAsiento", new { id = vueloId, pasajeros = pasajeros });
                }

                if (seleccion.Distinct().Count() != pasajeros)
                {
                    TempData["Error"] = "No puede asignar el mismo asiento a más de un pasajero" + tramoTexto + ".";
                    return RedirectToAction("SeleccionAsiento", new { id = vueloId, pasajeros = pasajeros });
                }

                int idTramo = tramos[k].VueloId;
                var ocupados = db.ReservaPasajeroVuelo
                    .Where(r => r.VueloId == idTramo && (r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA"))
                    .Select(r => (int)r.NumeroAsiento)
                    .ToList();

                if (seleccion.Any(n => ocupados.Contains(n)))
                {
                    TempData["Error"] = "Uno de los asientos elegidos" + tramoTexto + " acaba de ser reservado. Por favor seleccione otro.";
                    return RedirectToAction("SeleccionAsiento", new { id = vueloId, pasajeros = pasajeros });
                }
            }

            // Selección temporal en sesión (no es una reserva hasta el paso 3)
            Session["Reserva_VueloId"] = tramos[0].VueloId;          // tramo 1 (compatibilidad con pantallas 9 y 10)
            Session["Reserva_PasajerosCount"] = pasajeros;
            Session["Reserva_Asientos"] = asientosSeleccionados;     // asientos del tramo 1
            Session["Reserva_Tramos"] = tramos.Select(t => t.VueloId).ToList();
            Session["Reserva_AsientosTramo2"] = tramos.Count > 1 ? asientosTramo2 : null;

            return RedirectToAction("Pasajeros", "Reservas");
        }

        // Código del asiento (DP-11): número en BD → 1D, 2D, 2I, 3D, 3I, 4D, 4I
        private static readonly Dictionary<int, string> CodigosAsiento = new Dictionary<int, string>
{
    { 1, "1D" }, { 2, "2D" }, { 3, "2I" }, { 4, "3D" }, { 5, "3I" }, { 6, "4D" }, { 7, "4I" }
};

        private static string CodigoAsiento(int numero)
        {
            return CodigosAsiento.ContainsKey(numero) ? CodigosAsiento[numero] : numero.ToString();
        }

        // Vuelve a la pantalla 9 conservando lo que el usuario escribió (wireframe, punto 17)
        private ActionResult VolverAPasajeros(List<Pasajero> pasajeros, string mensaje)
        {
            TempData["Error"] = mensaje;
            TempData["PasajerosIngresados"] = pasajeros;
            return RedirectToAction("Pasajeros");
        }

        [HttpGet]
        public ActionResult Pasajeros()
        {
            if (Session["Reserva_VueloId"] == null || Session["Reserva_Asientos"] == null)
            {
                TempData["Error"] = "No hay una selección activa de asientos. Por favor elija sus asientos.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var tramosIds = Session["Reserva_Tramos"] as List<int> ?? new List<int> { (int)Session["Reserva_VueloId"] };
            var asientosTramo1 = Session["Reserva_Asientos"] as List<int>;
            var asientosTramo2 = Session["Reserva_AsientosTramo2"] as List<int>;

            var tramos = tramosIds.Select(id => db.Vuelo.Find(id)).ToList();
            if (tramos.Any(t => t == null))
            {
                TempData["Error"] = "El vuelo seleccionado no está disponible.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Códigos de asiento por pasajero: [pasajero][tramo]
            var codigosPorPasajero = new List<List<string>>();
            for (int i = 0; i < asientosTramo1.Count; i++)
            {
                var fila = new List<string> { CodigoAsiento(asientosTramo1[i]) };
                if (tramos.Count > 1 && asientosTramo2 != null && i < asientosTramo2.Count)
                {
                    fila.Add(CodigoAsiento(asientosTramo2[i]));
                }
                codigosPorPasajero.Add(fila);
            }

            ViewBag.Tramos = tramos;
            ViewBag.UltimoTramo = tramos.Last();
            ViewBag.EsItinerario = tramos.Count > 1;
            ViewBag.CodigosPorPasajero = codigosPorPasajero;
            ViewBag.TotalPasajeros = codigosPorPasajero.Count;
            ViewBag.PasajerosIngresados = TempData["PasajerosIngresados"] as List<Pasajero>;

            return View(tramos.First());
        }

        // POST: /Reservas/Pasajeros
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Pasajeros(List<Pasajero> pasajeros)
        {
            if (Session["Reserva_VueloId"] == null || Session["Reserva_Asientos"] == null)
            {
                TempData["Error"] = "La sesión ha expirado. Por favor realice su búsqueda nuevamente.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            List<int> asientosIds = Session["Reserva_Asientos"] as List<int>;

            if (pasajeros == null || pasajeros.Count != asientosIds.Count)
            {
                return VolverAPasajeros(pasajeros, "Debe completar la información de todos los pasajeros.");
            }

            // 1. Validaciones del wireframe (pantalla 9), en la clase de negocio Pasajero
            for (int i = 0; i < pasajeros.Count; i++)
            {
                string error = pasajeros[i].ValidarDatosCompletos(i + 1);
                if (error != null)
                {
                    return VolverAPasajeros(pasajeros, error);
                }
            }

            // 2. El mismo documento no puede repetirse en la reserva
            var documentos = pasajeros.Select(p => p.TipoDocumentoCodigo + "|" + p.NumeroDocumento).ToList();
            if (documentos.Distinct().Count() != documentos.Count)
            {
                return VolverAPasajeros(pasajeros, "No puede ingresar el mismo número de documento para más de un pasajero.");
            }

            // 3. Reutilización o registro en la base de datos
            List<int> pasajerosIds = new List<int>();

            foreach (var p in pasajeros)
            {
                string numDoc = p.NumeroDocumento;
                string tipoCodigo = p.TipoDocumentoCodigo;

                var pasajeroBD = db.Pasajero.FirstOrDefault(x => x.TipoDocumentoCodigo == tipoCodigo && x.NumeroDocumento == numDoc);

                if (pasajeroBD != null)
                {
                    if (p.Correo != null) pasajeroBD.Correo = p.Correo;
                    if (p.Telefono != null) pasajeroBD.Telefono = p.Telefono;
                    db.SaveChanges();
                    pasajerosIds.Add(pasajeroBD.PasajeroId);
                }
                else
                {
                    var nuevo = new Pasajero
                    {
                        TipoDocumentoCodigo = tipoCodigo,
                        NumeroDocumento = numDoc,
                        Nombres = p.Nombres,
                        Apellidos = p.Apellidos,
                        FechaNacimiento = p.FechaNacimiento,
                        Correo = p.Correo,
                        Telefono = p.Telefono,
                        FechaRegistro = DateTime.Now      // antes quedaba en 01/01/0001
                    };

                    db.Pasajero.Add(nuevo);
                    db.SaveChanges();
                    pasajerosIds.Add(nuevo.PasajeroId);
                }
            }

            Session["Reserva_PasajerosIds"] = pasajerosIds;

            return RedirectToAction("Confirmar", "Reservas");
        }

        // Lee de la sesión la operación armada en las pantallas 8 y 9
        private bool LeerOperacion(out List<Vuelo> tramos, out List<List<int>> asientosPorTramo, out List<int> pasajerosIds)
        {
            tramos = null;
            asientosPorTramo = null;
            pasajerosIds = null;

            if (Session["Reserva_VueloId"] == null || Session["Reserva_Asientos"] == null || Session["Reserva_PasajerosIds"] == null)
                return false;

            var tramosIds = Session["Reserva_Tramos"] as List<int> ?? new List<int> { (int)Session["Reserva_VueloId"] };
            pasajerosIds = Session["Reserva_PasajerosIds"] as List<int>;

            asientosPorTramo = new List<List<int>> { Session["Reserva_Asientos"] as List<int> };
            if (tramosIds.Count > 1)
                asientosPorTramo.Add(Session["Reserva_AsientosTramo2"] as List<int>);

            var lista = new List<Vuelo>();
            foreach (int id in tramosIds)
            {
                lista.Add(db.Vuelo.Find(id));
            }
            tramos = lista;

            int cantidad = pasajerosIds.Count;
            return tramos.All(t => t != null) && asientosPorTramo.All(a => a != null && a.Count == cantidad);
        }

        private void LimpiarOperacion()
        {
            Session.Remove("Reserva_VueloId");
            Session.Remove("Reserva_Asientos");
            Session.Remove("Reserva_AsientosTramo2");
            Session.Remove("Reserva_Tramos");
            Session.Remove("Reserva_PasajerosIds");
            Session.Remove("Reserva_PasajerosCount");
        }

        [HttpGet]
        public ActionResult Confirmar()
        {
            List<Vuelo> tramos;
            List<List<int>> asientosPorTramo;
            List<int> pasajerosIds;

            if (!LeerOperacion(out tramos, out asientosPorTramo, out pasajerosIds))
            {
                TempData["Error"] = "La sesión de reserva no está completa o ha expirado.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Pasajeros en el mismo orden en que se les asignó asiento
            var lista = db.Pasajero.Where(p => pasajerosIds.Contains(p.PasajeroId)).ToList();
            var pasajeros = pasajerosIds.Select(id => lista.First(p => p.PasajeroId == id)).ToList();

            // Códigos de asiento por pasajero: [pasajero][tramo]
            var codigos = new List<List<string>>();
            for (int i = 0; i < pasajeros.Count; i++)
            {
                codigos.Add(asientosPorTramo.Select(a => CodigoAsiento(a[i])).ToList());
            }

            decimal importePorPasajero = tramos.Sum(t => ReservaServicio.TarifaTramo(t));

            ViewBag.Tramos = tramos;
            ViewBag.Pasajeros = pasajeros;
            ViewBag.CodigosPorPasajero = codigos;
            ViewBag.TarifasTramo = tramos.Select(t => ReservaServicio.TarifaTramo(t)).ToList();
            ViewBag.ImportePorPasajero = importePorPasajero;
            ViewBag.TotalPagar = importePorPasajero * pasajeros.Count;
            ViewBag.RetencionMin = Utilidades.LeerParametro("RetencionReservaMin", 120);
            ViewBag.EsCounter = User.IsInRole("Counter");
            ViewBag.CorreoCliente = TempData["CorreoCliente"] as string ?? "";

            return View(tramos.First());
        }

        // POST: /Reservas/Confirmar
        // La lógica de la confirmación está en Negocio/ReservaServicio.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Confirmar(bool? aceptarCondiciones, string accionBoton, string correoCliente)
        {
            Reserva.LiberarVencidas(db);
            List<Vuelo> tramos;
            List<List<int>> asientosPorTramo;
            List<int> pasajerosIds;

            if (!LeerOperacion(out tramos, out asientosPorTramo, out pasajerosIds))
            {
                TempData["Error"] = "La sesión ha expirado. Por favor realice su búsqueda nuevamente.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            if (aceptarCondiciones != true)
            {
                TempData["Error"] = "Debe aceptar las condiciones de la reserva para continuar.";
                return RedirectToAction("Confirmar");
            }

            var servicio = new ReservaServicio(db);

            // 1. Revalidar cada tramo en el servidor (RNP-04)
            string volverA;
            string error = servicio.RevalidarTramos(tramos, asientosPorTramo, pasajerosIds, out volverA);
            if (error != null)
            {
                TempData["Error"] = error;
                if (volverA == "Buscar")
                    return RedirectToAction("Buscar", "Vuelos");
                if (volverA == "SeleccionAsiento")
                    return RedirectToAction("SeleccionAsiento", new { id = tramos[0].VueloId, pasajeros = pasajerosIds.Count });
                return RedirectToAction("Confirmar");
            }

            // 2. Vencimiento de la retención (DP-07 / RG-19)
            DateTime ahora = DateTime.Now;
            DateTime expiracion;
            error = servicio.CalcularExpiracion(tramos, ahora, out expiracion);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Titular: el usuario autenticado, o el cliente indicado por el Counter
            string usuarioId = User.Identity.GetUserId();
            if (User.IsInRole("Counter"))
            {
                error = servicio.BuscarClienteParaCounter(correoCliente, out usuarioId);
                if (error != null)
                {
                    TempData["Error"] = error;
                    TempData["CorreoCliente"] = (correoCliente ?? "").Trim();
                    return RedirectToAction("Confirmar");
                }
            }

            // 3. Reserva y asientos en UNA transacción
            Reserva reserva;
            error = servicio.CrearReserva(usuarioId, tramos, asientosPorTramo, pasajerosIds, ahora, expiracion, out reserva);
            if (error != null)
            {
                TempData["Error"] = error;
                return RedirectToAction("Confirmar");
            }

            // 4. Fin de la selección temporal
            LimpiarOperacion();

            // 5. «Confirmar y pagar» → pantalla 11 · «Confirmar reserva» → pantalla 12
            if (accionBoton == "Pagar")
            {
                return RedirectToAction("Pagar", "Compras", new { id = reserva.ReservaId });
            }
            return RedirectToAction("Confirmacion", "Compras", new { id = reserva.ReservaId });
        }

        // =========================================================================
        // PANTALLA 13: CONSULTA DE RESERVAS (GET /Reservas/MisReservas)
        // =========================================================================
        [HttpGet]
        public ActionResult MisReservas(DateTime? desde, DateTime? hasta, string estado, int pagina = 1)
        {
            // Siempre se filtra por el usuario autenticado, nunca por un parámetro de la URL
            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;

            // Rango de fechas inválido: se avisa junto al filtro y no se aplica el "hasta"
            if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            {
                ViewBag.ErrorFiltro = "La fecha «Hasta» no puede ser anterior a la fecha «Desde».";
                hasta = null;
            }

            // Estados válidos del filtro
            var estadosValidos = new List<string> { "PENDIENTE", "CONFIRMADA", "CANCELADA", "EXPIRADA" };
            if (!string.IsNullOrEmpty(estado) && !estadosValidos.Contains(estado))
            {
                estado = null;
            }

            var reservas = db.Reserva
                .Include(r => r.Compra)
                .Include(r => r.ReservaPasajeroVuelo.Select(a => a.Vuelo.Frecuencia.Ruta.Aeropuerto))
                .Include(r => r.ReservaPasajeroVuelo.Select(a => a.Vuelo.Frecuencia.Ruta.Aeropuerto1))
                .Where(r => r.UsuarioId == currentUserId)
                .OrderByDescending(r => r.FechaCreacion)
                .ToList();

            DateTime ahora = DateTime.Now;

            // Filtro por fecha de viaje (fecha del primer tramo de la reserva)
            if (desde.HasValue)
            {
                reservas = reservas
                    .Where(r => r.ReservaPasajeroVuelo.Any() && r.ReservaPasajeroVuelo.Min(a => a.Vuelo.FechaOperacion) >= desde.Value.Date)
                    .ToList();
            }
            if (hasta.HasValue)
            {
                reservas = reservas
                    .Where(r => r.ReservaPasajeroVuelo.Any() && r.ReservaPasajeroVuelo.Min(a => a.Vuelo.FechaOperacion) <= hasta.Value.Date)
                    .ToList();
            }

            // Filtro por estado (una reserva PENDIENTE con el plazo vencido se considera EXPIRADA - DP-07)
            if (!string.IsNullOrEmpty(estado))
            {
                reservas = reservas.Where(r =>
                {
                    bool vencida = r.EstadoReservaCodigo == "PENDIENTE" && r.FechaExpiracion.HasValue && r.FechaExpiracion.Value < ahora;
                    string estadoEfectivo = vencida ? "EXPIRADA" : r.EstadoReservaCodigo;
                    return estadoEfectivo == estado;
                }).ToList();
            }

            // Paginación simple (8 reservas por página)
            int tamanoPagina = 8;
            int totalRegistros = reservas.Count;
            int totalPaginas = (int)Math.Ceiling(totalRegistros / (double)tamanoPagina);
            if (totalPaginas < 1) totalPaginas = 1;
            if (pagina < 1) pagina = 1;
            if (pagina > totalPaginas) pagina = totalPaginas;

            var paginaActual = reservas
                .Skip((pagina - 1) * tamanoPagina)
                .Take(tamanoPagina)
                .ToList();

            // Datos para conservar los filtros en la vista
            ViewBag.Desde = desde.HasValue ? desde.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Hasta = hasta.HasValue ? hasta.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Estado = estado ?? "";
            ViewBag.Pagina = pagina;
            ViewBag.TotalPaginas = totalPaginas;
            ViewBag.TotalRegistros = totalRegistros;
            ViewBag.HayFiltros = desde.HasValue || hasta.HasValue || !string.IsNullOrEmpty(estado);
            ViewBag.Ahora = ahora;

            return View(paginaActual);
        }
        // =========================================================================
        // PANTALLA 14: GESTIÓN DE RESERVAS Y CANCELACIONES (/Reservas/Gestionar/{id})
        // =========================================================================

        // Verifica que el usuario sea el titular o un rol autorizado
        private bool PuedeGestionar(Reserva reserva)
        {
            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;
            return reserva.UsuarioId == currentUserId
                || User.IsInRole("Counter")
                || User.IsInRole("Administrador")
                || User.IsInRole("SuperUsuario");
        }

        // GET: /Reservas/Gestionar/5
        [HttpGet]
        public ActionResult Gestionar(int? id)
        {
            if (!id.HasValue)
            {
                TempData["Error"] = "Identificador de reserva no válido.";
                return RedirectToAction("MisReservas");
            }

            var reserva = db.Reserva.Find(id.Value);
            if (reserva == null || !PuedeGestionar(reserva))
            {
                // Mensaje neutral: no se revela si la reserva existe
                TempData["Error"] = "No se encontró la reserva solicitada.";
                return RedirectToAction("MisReservas");
            }

            // Estado efectivo (una pendiente vencida se muestra como expirada - DP-07)
            bool vencida = reserva.EstadoReservaCodigo == "PENDIENTE" && reserva.FechaExpiracion.HasValue && reserva.FechaExpiracion.Value < DateTime.Now;
            ViewBag.EstadoReserva = vencida ? "EXPIRADA" : reserva.EstadoReservaCodigo;

            var compra = reserva.Compra.OrderByDescending(c => c.FechaCompra).FirstOrDefault();
            ViewBag.EstadoCompra = compra != null ? compra.EstadoCompraCodigo : "";
            ViewBag.ImporteTotal = reserva.ReservaPasajeroVuelo.Sum(a => (decimal?)a.ImporteTramo) ?? 0m;

            // Códigos de asiento leídos de la tabla Asiento (DP-11)
            var asientos = db.Asiento.ToList();
            var codigos = new Dictionary<int, string>();
            foreach (var a in reserva.ReservaPasajeroVuelo)
            {
                int modeloId = a.Vuelo.Aeronave != null ? a.Vuelo.Aeronave.ModeloAeronaveId : 0;
                var asiento = asientos.FirstOrDefault(s => s.NumeroAsiento == a.NumeroAsiento && (modeloId == 0 || s.ModeloAeronaveId == modeloId));
                codigos[a.AsignacionId] = asiento != null ? asiento.Codigo : a.NumeroAsiento.ToString();
            }
            ViewBag.CodigosAsiento = codigos;

            // Evaluación de la política DP-08
            bool conReembolso;
            double horasRestantes;
            string motivoNoCancelable = reserva.EvaluarCancelacion(out conReembolso, out horasRestantes);
            ViewBag.PuedeCancelar = motivoNoCancelable == null;
            ViewBag.MotivoNoCancelable = motivoNoCancelable;
            ViewBag.ConReembolso = conReembolso;
            ViewBag.HorasRestantes = horasRestantes;

            return View(reserva);
        }

        // POST: /Reservas/Gestionar/5  (Confirmar solicitud de cancelación)
        // La lógica de la cancelación está en Negocio/ReservaServicio.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Gestionar(int id, string motivo)
        {
            var reserva = db.Reserva.Find(id);
            if (reserva == null || !PuedeGestionar(reserva))
            {
                TempData["Error"] = "No se encontró la reserva solicitada.";
                return RedirectToAction("MisReservas");
            }

            string mensajeExito;
            string error = new ReservaServicio(db).CancelarReserva(reserva, motivo, out mensajeExito);
            if (error != null)
            {
                TempData["Error"] = error;
            }
            else
            {
                TempData["Exito"] = mensajeExito;
            }
            return RedirectToAction("Gestionar", new { id = id });
        }
        // =========================================================================
        // PANTALLA 15: CHECK-IN (/Reservas/CheckIn)
        // =========================================================================

        // La lógica del check-in está en Negocio/CheckInServicio.cs.
        // Aquí solo se recibe la petición, se verifican permisos y se muestra el resultado.

        // GET: /Reservas/CheckIn?codigo=RV00000006&documento=1710000015
        [HttpGet]
        public ActionResult CheckIn(string codigo, string documento)
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

            ViewBag.Elegibilidad = servicio.EvaluarReserva(reserva);
            ViewBag.CodigosAsiento = servicio.ObtenerCodigosAsiento(reserva);
            return View(reserva);
        }

        // POST: /Reservas/CheckIn  (Confirmar check-in de las asignaciones marcadas)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CheckIn(string codigo, string documento, List<int> asignaciones)
        {
            codigo = (codigo ?? "").Trim().ToUpper();
            documento = (documento ?? "").Trim().ToUpper();

            var servicio = new CheckInServicio(db);

            var reserva = servicio.BuscarReserva(codigo, documento);
            if (reserva == null || !PuedeGestionar(reserva))
            {
                TempData["Error"] = "No se encontró una reserva con esos datos.";
                return RedirectToAction("CheckIn");
            }

            if (asignaciones == null || asignaciones.Count == 0)
            {
                TempData["Error"] = "Seleccione al menos un pasajero para registrar el check-in.";
                return RedirectToAction("CheckIn", new { codigo = codigo, documento = documento });
            }

            var registrados = new List<string>();
            var rechazados = new List<string>();

            try
            {
                servicio.Registrar(reserva, asignaciones, registrados, rechazados);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "No se pudo registrar el check-in: " + MensajeInterno(ex);
                return RedirectToAction("CheckIn", new { codigo = codigo, documento = documento });
            }

            if (registrados.Any())
                TempData["Exito"] = "Check-in completado para " + string.Join(", ", registrados) + ".";
            if (rechazados.Any())
                TempData["Error"] = "No se pudo registrar el check-in de " + string.Join(", ", rechazados) + " porque ya no es elegible.";

            return RedirectToAction("CheckIn", new { codigo = codigo, documento = documento });
        }

        // =========================================================================
        // CRUD DE SCAFFOLDING (roles de permiso: Details, Create, Edit, Delete)
        // =========================================================================

        // Mensaje real de la base de datos (triggers, restricciones)
        private static string MensajeInterno(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message;
        }

        // GET: Reservas/Details/5
        [Authorize(Roles = "Details")]
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Reserva reserva = db.Reserva.Find(id);
            if (reserva == null)
            {
                return HttpNotFound();
            }
            return View(reserva);
        }

        // GET: Reservas/Create
        [Authorize(Roles = "Create")]
        public ActionResult Create()
        {
            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion");
            return View();
        }

        // POST: Reservas/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Create")]
        public ActionResult Create([Bind(Include = "ReservaId,CodigoReserva,UsuarioId,FechaCreacion,FechaExpiracion,EstadoReservaCodigo")] Reserva reserva)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    db.Reserva.Add(reserva);
                    db.SaveChanges();
                    TempData["Exito"] = "Reserva creada.";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    db.Entry(reserva).State = EntityState.Detached;
                    ModelState.AddModelError("", "No se guardó la reserva: " + MensajeInterno(ex));
                }
            }

            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion", reserva.EstadoReservaCodigo);
            return View(reserva);
        }

        // GET: Reservas/Edit/5
        [Authorize(Roles = "Edit")]
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Reserva reserva = db.Reserva.Find(id);
            if (reserva == null)
            {
                return HttpNotFound();
            }
            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion", reserva.EstadoReservaCodigo);
            return View(reserva);
        }

        // POST: Reservas/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Edit")]
        public ActionResult Edit([Bind(Include = "ReservaId,CodigoReserva,UsuarioId,FechaCreacion,FechaExpiracion,EstadoReservaCodigo")] Reserva reserva)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    db.Entry(reserva).State = EntityState.Modified;
                    db.SaveChanges();
                    TempData["Exito"] = "Cambios guardados.";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "No se guardaron los cambios: " + MensajeInterno(ex));
                }
            }
            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion", reserva.EstadoReservaCodigo);
            return View(reserva);
        }

        // GET: Reservas/Delete/5
        [Authorize(Roles = "Delete")]
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Reserva reserva = db.Reserva.Find(id);
            if (reserva == null)
            {
                return HttpNotFound();
            }
            return View(reserva);
        }

        // POST: Reservas/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            Reserva reserva = db.Reserva.Find(id);
            try
            {
                db.Reserva.Remove(reserva);
                db.SaveChanges();
                TempData["Exito"] = "Reserva eliminada.";
            }
            catch (Exception)
            {
                TempData["Error"] = "No se puede eliminar: la reserva tiene asientos, compras o check-in asociados (integridad referencial).";
            }
            return RedirectToAction("Index");
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