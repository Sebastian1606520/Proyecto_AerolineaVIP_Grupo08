using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;

namespace Con_Autenticacion.Controllers
{
    [Authorize]
    public class ReservasController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // GET: Reservas
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
            if (vuelo == null || vuelo.EstadoVueloCodigo == "CANCELADO")
            {
                TempData["Error"] = "El vuelo seleccionado no está disponible o ha sido cancelado.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            if (vuelo.AeronaveId == null)
            {
                TempData["Error"] = "El vuelo no tiene aeronave asignada; sus asientos no pueden reservarse.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            int cantidadPasajeros = (pasajeros >= 1 && pasajeros <= 7) ? pasajeros : 1;

            // Verificar si hay cupo suficiente
            int ocupados = vuelo.ReservaPasajeroVuelo
                .Count(r => r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA");
            int disponibles = 7 - ocupados;

            if (disponibles < cantidadPasajeros)
            {
                TempData["Error"] = "No quedan suficientes asientos disponibles para la cantidad de pasajeros requerida.";
                return RedirectToAction("Resultados", "Vuelos", new
                {
                    origenId = vuelo.Frecuencia.Ruta.AeropuertoOrigenId,
                    destinoId = vuelo.Frecuencia.Ruta.AeropuertoDestinoId,
                    fecha = vuelo.FechaOperacion.ToString("yyyy-MM-dd"),
                    pasajeros = cantidadPasajeros
                });
            }

            ViewBag.Pasajeros = cantidadPasajeros;
            ViewBag.HoraCorte = DateTime.Now.ToString("HH:mm");

            return View(vuelo);
        }

        // POST: /Reservas/SeleccionAsiento
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SeleccionAsiento(int vueloId, int pasajeros, List<int> asientosSeleccionados)
        {
            if (asientosSeleccionados == null || asientosSeleccionados.Count != pasajeros)
            {
                TempData["Error"] = "Debe asignar un asiento a cada pasajero antes de continuar.";
                return RedirectToAction("SeleccionAsiento", new { id = vueloId, pasajeros = pasajeros });
            }

            if (asientosSeleccionados.Distinct().Count() != pasajeros)
            {
                TempData["Error"] = "No puede asignar el mismo asiento a más de un pasajero.";
                return RedirectToAction("SeleccionAsiento", new { id = vueloId, pasajeros = pasajeros });
            }

            // Revalidar disponibilidad
            var ocupados = db.ReservaPasajeroVuelo
                .Where(r => r.VueloId == vueloId && (r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA"))
                .Select(r => (int)r.NumeroAsiento)
                .ToList();

            foreach (var numAsiento in asientosSeleccionados)
            {
                if (ocupados.Contains(numAsiento))
                {
                    TempData["Error"] = "Uno de los asientos elegidos acaba de ser reservado. Por favor seleccione otro.";
                    return RedirectToAction("SeleccionAsiento", new { id = vueloId, pasajeros = pasajeros });
                }
            }

            // Guardar en sesión
            Session["Reserva_VueloId"] = vueloId;
            Session["Reserva_PasajerosCount"] = pasajeros;
            Session["Reserva_Asientos"] = asientosSeleccionados;

            // Redirige a la Pantalla 9: /Reservas/Pasajeros
            return RedirectToAction("Pasajeros", "Reservas");
        }

        [HttpGet]
        [Authorize]
        public ActionResult Pasajeros()
        {
            if (Session["Reserva_VueloId"] == null || Session["Reserva_Asientos"] == null)
            {
                TempData["Error"] = "No hay una selección activa de asientos. Por favor elija sus asientos.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            int vueloId = (int)Session["Reserva_VueloId"];
            List<int> asientosIds = Session["Reserva_Asientos"] as List<int>;

            var vuelo = db.Vuelo.Find(vueloId);
            if (vuelo == null)
            {
                TempData["Error"] = "El vuelo seleccionado no está disponible.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var mapaCodigos = new Dictionary<int, string>
    {
        { 1, "1D" }, { 2, "2D" }, { 3, "2I" }, { 4, "3D" }, { 5, "3I" }, { 6, "4D" }, { 7, "4I" }
    };

            ViewBag.CodigosAsientos = asientosIds.Select(id => mapaCodigos.ContainsKey(id) ? mapaCodigos[id] : id.ToString()).ToList();
            ViewBag.AsientosNumeros = asientosIds;

            return View(vuelo);
        }

        // POST: /Reservas/Pasajeros
        [HttpPost]
        [Authorize]
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
                TempData["Error"] = "Debe completar la información de todos los pasajeros.";
                return RedirectToAction("Pasajeros");
            }

            // 1. Evitar cédulas/pasaportes duplicados en la misma reserva
            var documentos = pasajeros
                .Where(p => !string.IsNullOrWhiteSpace(p.NumeroDocumento))
                .Select(p => p.NumeroDocumento.Trim().ToUpper())
                .ToList();

            if (documentos.Distinct().Count() != documentos.Count)
            {
                TempData["Error"] = "No puede ingresar el mismo número de documento para más de un pasajero.";
                return RedirectToAction("Pasajeros");
            }

            // 2. Validaciones de obligatoriedad y formato de cédula
            foreach (var p in pasajeros)
            {
                if (string.IsNullOrWhiteSpace(p.Nombres) || string.IsNullOrWhiteSpace(p.Apellidos) || string.IsNullOrWhiteSpace(p.NumeroDocumento))
                {
                    TempData["Error"] = "Nombres, apellidos y documento son campos obligatorios.";
                    return RedirectToAction("Pasajeros");
                }

                // Si el código es Cédula, validar exactamente 10 dígitos
                if (p.TipoDocumentoCodigo == "CEDULA" && (p.NumeroDocumento.Trim().Length != 10 || !p.NumeroDocumento.Trim().All(char.IsDigit)))
                {
                    TempData["Error"] = "La cédula debe contener exactamente 10 dígitos numéricos.";
                    return RedirectToAction("Pasajeros");
                }
            }

            // 3. Reutilización o registro en la base de datos
            List<int> pasajerosIds = new List<int>();

            foreach (var p in pasajeros)
            {
                string numDoc = p.NumeroDocumento.Trim().ToUpper();
                string tipoCodigo = p.TipoDocumentoCodigo;

                var pasajeroBD = db.Pasajero.FirstOrDefault(x => x.TipoDocumentoCodigo == tipoCodigo && x.NumeroDocumento == numDoc);

                if (pasajeroBD != null)
                {
                    if (!string.IsNullOrWhiteSpace(p.Correo)) pasajeroBD.Correo = p.Correo.Trim();
                    if (!string.IsNullOrWhiteSpace(p.Telefono)) pasajeroBD.Telefono = p.Telefono.Trim();
                    db.SaveChanges();
                    pasajerosIds.Add(pasajeroBD.PasajeroId);
                }
                else
                {
                    var nuevo = new Pasajero
                    {
                        TipoDocumentoCodigo = tipoCodigo,
                        NumeroDocumento = numDoc,
                        Nombres = p.Nombres.Trim(),
                        Apellidos = p.Apellidos.Trim(),
                        FechaNacimiento = p.FechaNacimiento,
                        Correo = !string.IsNullOrWhiteSpace(p.Correo) ? p.Correo.Trim() : null,
                        Telefono = !string.IsNullOrWhiteSpace(p.Telefono) ? p.Telefono.Trim() : null
                    };

                    db.Pasajero.Add(nuevo);
                    db.SaveChanges();
                    pasajerosIds.Add(nuevo.PasajeroId);
                }
            }

            Session["Reserva_PasajerosIds"] = pasajerosIds;

            return RedirectToAction("Confirmar", "Reservas");
        }


        [HttpGet]
        [Authorize]
        public ActionResult Confirmar()
        {
            // 1. Estado vacío: si faltan datos en sesión, regresa a buscar
            if (Session["Reserva_VueloId"] == null || Session["Reserva_Asientos"] == null || Session["Reserva_PasajerosIds"] == null)
            {
                TempData["Error"] = "La sesión de reserva no está completa o ha expirado.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            int vueloId = (int)Session["Reserva_VueloId"];
            List<int> asientosNumeros = Session["Reserva_Asientos"] as List<int>;
            List<int> pasajerosIds = Session["Reserva_PasajerosIds"] as List<int>;

            var vuelo = db.Vuelo.Find(vueloId);
            if (vuelo == null)
            {
                TempData["Error"] = "El vuelo seleccionado ya no está disponible.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // 2. Mapeo oficial de asientos (Embraer Phenom 300E - DP-11)
            var mapaCodigos = new Dictionary<int, string>
    {
        { 1, "1D" }, { 2, "2D" }, { 3, "2I" }, { 4, "3D" }, { 5, "3I" }, { 6, "4D" }, { 7, "4I" }
    };

            // 3. Recuperar las entidades Pasajero desde la base de datos
            var listaPasajeros = db.Pasajero.Where(p => pasajerosIds.Contains(p.PasajeroId)).ToList();
            // Conservar el orden exacto de asignación de asientos
            var pasajerosOrdenados = pasajerosIds.Select(id => listaPasajeros.First(p => p.PasajeroId == id)).ToList();

            // 4. Colecciones enviadas a la vista
            ViewBag.CodigosAsientos = asientosNumeros.Select(num => mapaCodigos.ContainsKey(num) ? mapaCodigos[num] : num.ToString()).ToList();
            ViewBag.AsientosNumeros = asientosNumeros;
            ViewBag.Pasajeros = pasajerosOrdenados;

            // Tarifa referencial de la ruta (DP-04)
            decimal tarifaBase = vuelo.Frecuencia.Ruta.TarifaReferencial > 0
                ? vuelo.Frecuencia.Ruta.TarifaReferencial
                : 150.00m;

            ViewBag.TarifaIndividual = tarifaBase;
            ViewBag.TotalPagar = tarifaBase * pasajerosOrdenados.Count;

            return View(vuelo);
        }

        // POST: /Reservas/Confirmar
        [HttpPost]
        [Authorize]
        [ValidateAntiForgeryToken]
        public ActionResult Confirmar(bool? aceptarCondiciones, string accionBoton)
        {
            if (Session["Reserva_VueloId"] == null || Session["Reserva_Asientos"] == null || Session["Reserva_PasajerosIds"] == null)
            {
                TempData["Error"] = "La sesión ha expirado. Por favor realice su búsqueda nuevamente.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            if (!aceptarCondiciones.HasValue || !aceptarCondiciones.Value)
            {
                TempData["Error"] = "Debe aceptar las condiciones de la reserva para continuar.";
                return RedirectToAction("Confirmar");
            }

            int vueloId = (int)Session["Reserva_VueloId"];
            List<int> asientosNumeros = Session["Reserva_Asientos"] as List<int>;
            List<int> pasajerosIds = Session["Reserva_PasajerosIds"] as List<int>;

            var vuelo = db.Vuelo.Find(vueloId);
            if (vuelo == null)
            {
                TempData["Error"] = "El vuelo seleccionado ya no existe.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // 1. Revalidación en BD: evitar colisión con el índice único UX_RPV_PasajeroActivo
            string sqlVerificarPasajero = @"
        SELECT COUNT(1) 
        FROM ReservaPasajeroVuelo 
        WHERE VueloId = @p0 
          AND PasajeroId = @p1 
          AND EstadoAsignacionCodigo IN ('PENDIENTE', 'CONFIRMADA');";

            for (int i = 0; i < pasajerosIds.Count; i++)
            {
                int asignacionesActivas = db.Database.SqlQuery<int>(sqlVerificarPasajero, vueloId, pasajerosIds[i]).FirstOrDefault();
                if (asignacionesActivas > 0)
                {
                    var pas = db.Pasajero.Find(pasajerosIds[i]);
                    string nombrePasajero = pas != null ? (pas.Nombres + " " + pas.Apellidos) : ("ID " + pasajerosIds[i]);
                    TempData["Error"] = "El pasajero " + nombrePasajero + " ya tiene un boleto reservado o confirmado en este mismo vuelo.";
                    return RedirectToAction("Confirmar");
                }
            }

            // 2. Tarifa referencial
            decimal tarifaUnit = 150.00m;
            try
            {
                if (vuelo.Frecuencia != null && vuelo.Frecuencia.Ruta != null && vuelo.Frecuencia.Ruta.TarifaReferencial > 0)
                {
                    tarifaUnit = vuelo.Frecuencia.Ruta.TarifaReferencial;
                }
            }
            catch { }

            // 3. Identificador de usuario autenticado
            string currentUserId = User.Identity.GetUserId();
            if (string.IsNullOrWhiteSpace(currentUserId))
            {
                currentUserId = User.Identity.Name;
            }

            int reservaIdGenerada = 0;
            string codigoGeneradoBD = "";

            try
            {
                // 4. Inserción en Reserva (sin CodigoReserva porque es columna calculada)
                string sqlReserva = @"
            INSERT INTO Reserva (UsuarioId, FechaCreacion, FechaExpiracion, EstadoReservaCodigo)
            VALUES (@p0, GETDATE(), DATEADD(HOUR, 2, GETDATE()), 'PENDIENTE');
            SELECT CAST(SCOPE_IDENTITY() AS INT);";

                reservaIdGenerada = db.Database.SqlQuery<int>(sqlReserva, currentUserId).FirstOrDefault();

                // 5. Recuperar el CodigoReserva autocalculado por SQL Server
                codigoGeneradoBD = db.Database.SqlQuery<string>(
                    "SELECT CodigoReserva FROM Reserva WHERE ReservaId = @p0", reservaIdGenerada
                ).FirstOrDefault() ?? ("RV" + reservaIdGenerada.ToString("D6"));

                // 6. Insertar detalle por pasajero y asiento
                for (int i = 0; i < pasajerosIds.Count; i++)
                {
                    string sqlAsiento = @"
                INSERT INTO ReservaPasajeroVuelo (ReservaId, PasajeroId, VueloId, NumeroAsiento, EstadoAsignacionCodigo, ImporteTramo)
                VALUES (@p0, @p1, @p2, @p3, 'PENDIENTE', @p4);";

                    db.Database.ExecuteSqlCommand(sqlAsiento, reservaIdGenerada, pasajerosIds[i], vueloId, asientosNumeros[i], tarifaUnit);
                }
            }
            catch (Exception ex)
            {
                Exception realEx = ex;
                while (realEx.InnerException != null)
                {
                    realEx = realEx.InnerException;
                }

                TempData["Error"] = "DETALLE BD: " + realEx.Message;
                return RedirectToAction("Confirmar");
            }

            // 7. Limpieza de variables de sesión del proceso de reserva
            Session.Remove("Reserva_VueloId");
            Session.Remove("Reserva_Asientos");
            Session.Remove("Reserva_PasajerosIds");
            Session.Remove("Reserva_PasajerosCount");

            // 8. Redirección según botón pulsado
            if (accionBoton == "Pagar")
            {
                return RedirectToAction("Pagar", "Compras", new { id = reservaIdGenerada });
            }

            TempData["Exito"] = "Reserva " + codigoGeneradoBD + " registrada con éxito. Estado: Pendiente de pago.";
            return RedirectToAction("Pagar", "Compras", new { id = reservaIdGenerada });
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

        // GET: Reservas/Details/5
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
        public ActionResult Create()
        {
            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion");
            return View();
        }

        // POST: Reservas/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "ReservaId,CodigoReserva,UsuarioId,FechaCreacion,FechaExpiracion,EstadoReservaCodigo")] Reserva reserva)
        {
            if (ModelState.IsValid)
            {
                db.Reserva.Add(reserva);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion", reserva.EstadoReservaCodigo);
            return View(reserva);
        }

        // GET: Reservas/Edit/5
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
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "ReservaId,CodigoReserva,UsuarioId,FechaCreacion,FechaExpiracion,EstadoReservaCodigo")] Reserva reserva)
        {
            if (ModelState.IsValid)
            {
                db.Entry(reserva).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.EstadoReservaCodigo = new SelectList(db.EstadoReserva, "Codigo", "Descripcion", reserva.EstadoReservaCodigo);
            return View(reserva);
        }

        // GET: Reservas/Delete/5
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
        public ActionResult DeleteConfirmed(int id)
        {
            Reserva reserva = db.Reserva.Find(id);
            db.Reserva.Remove(reserva);
            db.SaveChanges();
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
