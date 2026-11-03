using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using Con_Autenticacion.Models;

namespace Con_Autenticacion.Controllers
{
    public class VuelosController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // Mensaje real de la base de datos (triggers, restricciones)
        private static string MensajeInterno(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message;
        }

        // =========================================================================
        // PANTALLA 5: BÚSQUEDA DE VUELOS (pública)
        // =========================================================================
        // GET: Vuelos/Buscar (Pantalla 5)
        [HttpGet]
        [AllowAnonymous]
        public ActionResult Buscar()
        {
            var aeropuertos = db.Aeropuerto
                .Where(a => a.Activo)
                .OrderBy(a => a.Localidad)
                .Select(a => new
                {
                    Id = a.AeropuertoId,
                    Texto = a.Localidad + " (" + a.CodigoIATA + ")"
                })
                .ToList();

            ViewBag.Aeropuertos = new SelectList(aeropuertos, "Id", "Texto");
            ViewBag.Origenes = new SelectList(aeropuertos, "Id", "Texto");
            ViewBag.Destinos = new SelectList(aeropuertos, "Id", "Texto");

            return View();
        }

        // GET: Vuelos/Resultados (Pantalla 6)
        [HttpGet]
        [AllowAnonymous]
        public ActionResult Resultados(int? origenId, int? destinoId, DateTime? fecha, int pasajeros = 1)
        {
            // 1. Validaciones básicas de entrada
            if (!origenId.HasValue || !destinoId.HasValue || !fecha.HasValue || origenId.Value == destinoId.Value)
            {
                TempData["Error"] = (origenId.HasValue && destinoId.HasValue && origenId.Value == destinoId.Value)
                    ? "El aeropuerto de origen y destino deben ser diferentes."
                    : "Debe especificar origen, destino y fecha para consultar vuelos.";
                return RedirectToAction("Buscar");
            }

            DateTime fechaBusqueda = fecha.Value.Date;
            int totalPasajeros = Math.Max(1, Math.Min(pasajeros, 7));

            var aeroOrigen = db.Aeropuerto.Find(origenId.Value);
            var aeroDestino = db.Aeropuerto.Find(destinoId.Value);

            if (aeroOrigen == null || aeroDestino == null)
            {
                TempData["Error"] = "Uno de los aeropuertos seleccionados no es válido.";
                return RedirectToAction("Buscar");
            }

            string iataO = aeroOrigen.CodigoIATA;
            string iataD = aeroDestino.CodigoIATA;

            // 2. Determinar si es la ruta especial con escala dominical UIO <-> GPS
            bool esIdaGalapagos = (iataO == "UIO" && iataD == "GPS");
            bool esRetornoGalapagos = (iataO == "GPS" && iataD == "UIO");
            bool esItinerario = esIdaGalapagos || esRetornoGalapagos;
            bool esDomingo = fechaBusqueda.DayOfWeek == DayOfWeek.Sunday;

            if (esItinerario && !esDomingo)
            {
                TempData["Error"] = "La ruta con escala técnica Quito – Galápagos opera exclusivamente los domingos.";
                return RedirectToAction("Buscar");
            }

            if (!esItinerario && esDomingo)
            {
                TempData["Error"] = "Los vuelos directos operan de lunes a sábado. Los domingos están reservados para Galápagos.";
                return RedirectToAction("Buscar");
            }

            // 3. Consulta base: todos los vuelos de la fecha. Los cancelados, sin aeronave, sin cupo
            //    o ya partidos se muestran igual, y la vista los marca como no disponibles (EsVendible)
            int oId = origenId.Value;
            int dId = destinoId.Value;

            var consulta = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave)
                .Include(v => v.ReservaPasajeroVuelo.Select(r => r.Reserva))
                .Where(v => v.FechaOperacion == fechaBusqueda);

            List<Vuelo> vuelosResultado = new List<Vuelo>();

            if (esItinerario)
            {
                // Tramo 1: origen → GYE   ·   Tramo 2: GYE → destino   (mismo domingo)
                // Se compara por Id (AeropuertoOrigenId / AeropuertoDestinoId) para no depender de
                // las navegaciones del .edmx (Aeropuerto = DESTINO, Aeropuerto1 = ORIGEN)
                int gyeId = db.Aeropuerto.Where(a => a.CodigoIATA == "GYE").Select(a => a.AeropuertoId).FirstOrDefault();

                var vuelosDia = consulta.OrderBy(v => v.Frecuencia.HoraSalida).ToList();

                var tramo1 = vuelosDia.FirstOrDefault(v => v.Frecuencia.Ruta.AeropuertoOrigenId == oId
                                                        && v.Frecuencia.Ruta.AeropuertoDestinoId == gyeId);
                var tramo2 = vuelosDia.FirstOrDefault(v => v.Frecuencia.Ruta.AeropuertoOrigenId == gyeId
                                                        && v.Frecuencia.Ruta.AeropuertoDestinoId == dId);

                // El itinerario solo se ofrece completo (DP-09: los tramos no se venden sueltos)
                if (tramo1 != null && tramo2 != null)
                {
                    vuelosResultado.Add(tramo1);
                    vuelosResultado.Add(tramo2);
                }
            }
            else
            {
                // Vuelos directos de lunes a sábado (los tramos del domingo no se venden sueltos)
                vuelosResultado = consulta
                    .Where(v => v.Frecuencia.DiaSemana != 7
                             && v.Frecuencia.Ruta.AeropuertoOrigenId == oId
                             && v.Frecuencia.Ruta.AeropuertoDestinoId == dId)
                    .OrderBy(v => v.Frecuencia.HoraSalida)
                    .ToList();
            }

            // 4. Parámetros para la vista
            ViewBag.EsItinerarioEscala = esItinerario;
            ViewBag.OrigenId = origenId.Value;
            ViewBag.DestinoId = destinoId.Value;
            ViewBag.CiudadOrigen = $"{aeroOrigen.Localidad} ({iataO})";
            ViewBag.CiudadDestino = $"{aeroDestino.Localidad} ({iataD})";
            ViewBag.Fecha = fechaBusqueda;
            ViewBag.Pasajeros = totalPasajeros;

            return View(vuelosResultado);
        }

        // GET: Vuelos/Disponibilidad/5 (Pantalla 7)
        [HttpGet]
        [AllowAnonymous]
        public ActionResult Disponibilidad(int? id, int pasajeros = 1)
        {
            if (!id.HasValue)
            {
                TempData["Error"] = "Identificador de vuelo no válido.";
                return RedirectToAction("Buscar");
            }

            var vuelo = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave)
                .Include(v => v.ReservaPasajeroVuelo)
                .FirstOrDefault(v => v.VueloId == id.Value);

            if (vuelo == null || vuelo.EstadoVueloCodigo == "CANCELADO")
            {
                TempData["Error"] = "El vuelo seleccionado no está disponible o ha sido cancelado.";
                return RedirectToAction("Buscar");
            }

            // Regla RNP-01: No comercializar vuelos sin aeronave asignada
            if (vuelo.AeronaveId == null)
            {
                TempData["Error"] = "Este vuelo aún no tiene aeronave asignada; sus asientos no pueden consultarse.";
                return RedirectToAction("Buscar");
            }

            int cantPasajeros = Math.Max(1, Math.Min(pasajeros, 7));
            ViewBag.HoraCorte = DateTime.Now.ToString("HH:mm");
            ViewBag.Pasajeros = cantPasajeros;

            int origenBusqueda = vuelo.Frecuencia.Ruta.AeropuertoOrigenId;
            int destinoBusqueda = vuelo.Frecuencia.Ruta.AeropuertoDestinoId;

            // «Regresar a resultados»: un tramo del domingo vuelve a la búsqueda del itinerario completo
            if (vuelo.Frecuencia.DiaSemana == 7)
            {
                var uio = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "UIO");
                var gps = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "GPS");

                if (uio != null && gps != null)
                {
                    // En el modelo (.edmx): Aeropuerto1 = ORIGEN, Aeropuerto = DESTINO
                    bool esIda = vuelo.Frecuencia.Ruta.Aeropuerto1.CodigoIATA == "UIO"
                              || vuelo.Frecuencia.Ruta.Aeropuerto.CodigoIATA == "GPS";

                    origenBusqueda = esIda ? uio.AeropuertoId : gps.AeropuertoId;
                    destinoBusqueda = esIda ? gps.AeropuertoId : uio.AeropuertoId;
                }
            }

            ViewBag.OrigenBusqueda = origenBusqueda;
            ViewBag.DestinoBusqueda = destinoBusqueda;

            return View(vuelo);
        }

        // =========================================================================
        // CRUD DE SCAFFOLDING (roles de permiso: Details, Create, Edit, Delete)
        // =========================================================================

        // GET: Vuelos
        [Authorize(Roles = "Details")]
        public ActionResult Index()
        {
            var vuelos = db.Vuelo
                .Include(v => v.Aeronave)
                .Include(v => v.EstadoVuelo)
                .Include(v => v.Frecuencia)
                .OrderByDescending(v => v.FechaOperacion)
                .ToList();
            return View(vuelos);
        }

        // GET: Vuelos/Details/5
        [Authorize(Roles = "Details")]
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Vuelo vuelo = db.Vuelo.Find(id);
            if (vuelo == null)
            {
                return HttpNotFound();
            }
            return View(vuelo);
        }

        // GET: Vuelos/Create
        [Authorize(Roles = "Create")]
        public ActionResult Create()
        {
            ViewBag.AeronaveId = new SelectList(db.Aeronave, "AeronaveId", "Matricula");
            ViewBag.EstadoVueloCodigo = new SelectList(db.EstadoVuelo, "Codigo", "Descripcion");
            ViewBag.FrecuenciaId = new SelectList(db.Frecuencia, "FrecuenciaId", "FrecuenciaId");
            return View();
        }

        // POST: Vuelos/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Create")]
        public ActionResult Create([Bind(Include = "VueloId,FrecuenciaId,FechaOperacion,AeronaveId,EstadoVueloCodigo")] Vuelo vuelo)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    db.Vuelo.Add(vuelo);
                    db.SaveChanges();
                    TempData["Exito"] = "Vuelo creado.";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    // Ej.: trigger trg_Vuelo_Validar (día de operación, aeronave, superposición)
                    db.Entry(vuelo).State = EntityState.Detached;
                    ModelState.AddModelError("", "No se guardó el vuelo: " + MensajeInterno(ex));
                }
            }

            ViewBag.AeronaveId = new SelectList(db.Aeronave, "AeronaveId", "Matricula", vuelo.AeronaveId);
            ViewBag.EstadoVueloCodigo = new SelectList(db.EstadoVuelo, "Codigo", "Descripcion", vuelo.EstadoVueloCodigo);
            ViewBag.FrecuenciaId = new SelectList(db.Frecuencia, "FrecuenciaId", "FrecuenciaId", vuelo.FrecuenciaId);
            return View(vuelo);
        }

        // GET: Vuelos/Edit/5
        [Authorize(Roles = "Edit")]
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Vuelo vuelo = db.Vuelo.Find(id);
            if (vuelo == null)
            {
                return HttpNotFound();
            }
            ViewBag.AeronaveId = new SelectList(db.Aeronave, "AeronaveId", "Matricula", vuelo.AeronaveId);
            ViewBag.EstadoVueloCodigo = new SelectList(db.EstadoVuelo, "Codigo", "Descripcion", vuelo.EstadoVueloCodigo);
            ViewBag.FrecuenciaId = new SelectList(db.Frecuencia, "FrecuenciaId", "FrecuenciaId", vuelo.FrecuenciaId);
            return View(vuelo);
        }

        // POST: Vuelos/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Edit")]
        public ActionResult Edit([Bind(Include = "VueloId,FrecuenciaId,FechaOperacion,AeronaveId,EstadoVueloCodigo")] Vuelo vuelo)
        {
            if (ModelState.IsValid)
            {
                try
                {
                    db.Entry(vuelo).State = EntityState.Modified;
                    db.SaveChanges();
                    TempData["Exito"] = "Cambios guardados.";
                    return RedirectToAction("Index");
                }
                catch (Exception ex)
                {
                    ModelState.AddModelError("", "No se guardaron los cambios: " + MensajeInterno(ex));
                }
            }
            ViewBag.AeronaveId = new SelectList(db.Aeronave, "AeronaveId", "Matricula", vuelo.AeronaveId);
            ViewBag.EstadoVueloCodigo = new SelectList(db.EstadoVuelo, "Codigo", "Descripcion", vuelo.EstadoVueloCodigo);
            ViewBag.FrecuenciaId = new SelectList(db.Frecuencia, "FrecuenciaId", "FrecuenciaId", vuelo.FrecuenciaId);
            return View(vuelo);
        }

        // GET: Vuelos/Delete/5
        [Authorize(Roles = "Delete")]
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Vuelo vuelo = db.Vuelo.Find(id);
            if (vuelo == null)
            {
                return HttpNotFound();
            }
            return View(vuelo);
        }

        // POST: Vuelos/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        [Authorize(Roles = "Delete")]
        public ActionResult DeleteConfirmed(int id)
        {
            Vuelo vuelo = db.Vuelo.Find(id);
            try
            {
                db.Vuelo.Remove(vuelo);
                db.SaveChanges();
                TempData["Exito"] = "Vuelo eliminado.";
            }
            catch (Exception)
            {
                TempData["Error"] = "No se puede eliminar: el vuelo tiene reservas o tramos asociados (integridad referencial).";
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