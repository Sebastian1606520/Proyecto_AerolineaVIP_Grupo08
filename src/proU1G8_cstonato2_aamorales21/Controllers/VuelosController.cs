using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;
using Con_Autenticacion.Models;

namespace Con_Autenticacion.Controllers
{
    public class VuelosController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // GET: Vuelos/Buscar (Pantalla 1)
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

        // GET: Vuelos/Resultados (Pantalla 2)
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

            // 3. Consulta base: vuelos activos y con aeronave asignada para la fecha
            var consulta = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave)
                .Include(v => v.ReservaPasajeroVuelo)
                .Where(v => v.FechaOperacion == fechaBusqueda
                         && v.EstadoVueloCodigo != "CANCELADO"
                         && v.AeronaveId != null);

            List<Vuelo> vuelosResultado = new List<Vuelo>();

            if (esItinerario)
            {
                // Rutas estrictas y unidireccionales para el itinerario con escala en Guayaquil (GYE)
                string rutaTramo1 = esIdaGalapagos ? "UIO-GYE" : "GPS-GYE";
                string rutaTramo2 = esIdaGalapagos ? "GYE-GPS" : "GYE-UIO";

                var vuelosDia = consulta.OrderBy(v => v.Frecuencia.HoraSalida).ToList();

                var tramo1 = vuelosDia.FirstOrDefault(v =>
                    $"{v.Frecuencia.Ruta.Aeropuerto.CodigoIATA}-{v.Frecuencia.Ruta.Aeropuerto1.CodigoIATA}" == rutaTramo1);

                var tramo2 = vuelosDia.FirstOrDefault(v =>
                    $"{v.Frecuencia.Ruta.Aeropuerto.CodigoIATA}-{v.Frecuencia.Ruta.Aeropuerto1.CodigoIATA}" == rutaTramo2);

                // Ambos tramos deben existir y tener cupo suficiente
                if (tramo1 != null && tramo2 != null)
                {
                    int libresT1 = 7 - tramo1.ReservaPasajeroVuelo.Count(r => r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA");
                    int libresT2 = 7 - tramo2.ReservaPasajeroVuelo.Count(r => r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA");

                    if (libresT1 >= totalPasajeros && libresT2 >= totalPasajeros)
                    {
                        vuelosResultado.Add(tramo1);
                        vuelosResultado.Add(tramo2);
                    }
                }
            }
            else
            {
                // Vuelo directo: Origen y Destino estrictos en un solo sentido
                int oId = origenId.Value;
                int dId = destinoId.Value;

                var candidatos = consulta
                    .Where(v => v.Frecuencia.DiaSemana != 7
                             && v.Frecuencia.Ruta.AeropuertoOrigenId == oId
                             && v.Frecuencia.Ruta.AeropuertoDestinoId == dId)
                    .OrderBy(v => v.Frecuencia.HoraSalida)
                    .ToList();

                // Filtrar por disponibilidad real de cupos
                foreach (var v in candidatos)
                {
                    int ocupados = v.ReservaPasajeroVuelo.Count(r =>
                        r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA");

                    if ((7 - ocupados) >= totalPasajeros)
                    {
                        vuelosResultado.Add(v);
                    }
                }
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

        // GET: Vuelos/Disponibilidad/5 (Mapa de Asientos)
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

            // Manejo de la escala dominical de Galápagos
            if (vuelo.Frecuencia.DiaSemana == 7)
            {
                var uio = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "UIO");
                var gps = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "GPS");

                if (uio != null && gps != null)
                {
                    bool esIda = vuelo.Frecuencia.Ruta.Aeropuerto.CodigoIATA == "UIO"
                              || vuelo.Frecuencia.Ruta.Aeropuerto1.CodigoIATA == "GPS";

                    origenBusqueda = esIda ? uio.AeropuertoId : gps.AeropuertoId;
                    destinoBusqueda = esIda ? gps.AeropuertoId : uio.AeropuertoId;
                }
            }

            ViewBag.OrigenBusqueda = origenBusqueda;
            ViewBag.DestinoBusqueda = destinoBusqueda;

            return View(vuelo);
        }

        // =========================================================================
        // CRUD ADMINISTRATIVO (Scaffolding protegido)
        // =========================================================================

        // GET: Vuelos
        [Authorize(Roles = "Details")]
        public ActionResult Index()
        {
            var vuelos = db.Vuelo
                .Include(v => v.Aeronave)
                .Include(v => v.EstadoVuelo)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .OrderByDescending(v => v.FechaOperacion)
                .ThenBy(v => v.Frecuencia.HoraSalida)
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
                db.Vuelo.Add(vuelo);
                db.SaveChanges();
                return RedirectToAction("Index");
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
                db.Entry(vuelo).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
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
                TempData["Error"] = "No se puede eliminar: el vuelo tiene reservas o registros asociados (integridad referencial).";
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