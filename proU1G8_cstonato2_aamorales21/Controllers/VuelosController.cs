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

        // GET: Vuelos
        public ActionResult Buscar()
        {
            var aeropuertos = db.Aeropuerto
                .Where(a => a.Activo)
                .OrderBy(a => a.Localidad)
                .Select(a => new {
                    Id = a.AeropuertoId,
                    Texto = a.Localidad + " (" + a.CodigoIATA + ")"
                })
                .ToList();

            ViewBag.Aeropuertos = new SelectList(aeropuertos, "Id", "Texto");

            return View();
        }


        [HttpGet]
        [AllowAnonymous]
        public ActionResult Resultados(int? origenId, int? destinoId, DateTime? fecha, int pasajeros = 1)
        {
            if (!origenId.HasValue || !destinoId.HasValue || !fecha.HasValue)
            {
                TempData["Error"] = "Debe especificar origen, destino y fecha para consultar vuelos.";
                return RedirectToAction("Buscar");
            }

            if (origenId.Value == destinoId.Value)
            {
                TempData["Error"] = "El aeropuerto de origen y destino deben ser diferentes.";
                return RedirectToAction("Buscar");
            }

            DateTime fechaBusqueda = fecha.Value.Date;
            var aeroOrigen = db.Aeropuerto.Find(origenId.Value);
            var aeroDestino = db.Aeropuerto.Find(destinoId.Value);

            string iataOrigen = aeroOrigen != null ? aeroOrigen.CodigoIATA : "";
            string iataDestino = aeroDestino != null ? aeroDestino.CodigoIATA : "";

            List<Vuelo> vuelosResultado = new List<Vuelo>();

            // Ida UIO→GYE→GPS o regreso GPS→GYE→UIO
            bool esItinerario = (iataOrigen == "UIO" && iataDestino == "GPS")
                             || (iataOrigen == "GPS" && iataDestino == "UIO");

            if (esItinerario)
            {
                ViewBag.EsItinerarioEscala = true;

                var gye = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "GYE");
                int gyeId = gye != null ? gye.AeropuertoId : 0;
                int oId = origenId.Value;
                int dId = destinoId.Value;

                // Tramo 1: origen → GYE   ·   Tramo 2: GYE → destino   (mismo día)
                vuelosResultado = db.Vuelo
                    .Where(v => v.FechaOperacion == fechaBusqueda
                             && ((v.Frecuencia.Ruta.AeropuertoOrigenId == oId && v.Frecuencia.Ruta.AeropuertoDestinoId == gyeId)
                              || (v.Frecuencia.Ruta.AeropuertoOrigenId == gyeId && v.Frecuencia.Ruta.AeropuertoDestinoId == dId)))
                    .OrderBy(v => v.Frecuencia.HoraSalida)
                    .ToList();

                // El itinerario solo se ofrece completo (los dos tramos); si no es domingo, no hay resultado
                if (vuelosResultado.Count != 2)
                {
                    vuelosResultado = new List<Vuelo>();
                }
            }
            else
            {
                ViewBag.EsItinerarioEscala = false;

                // Vuelos directos de lunes a sábado. Los tramos del domingo no se venden sueltos (DP-09)
                vuelosResultado = db.Vuelo
                    .Where(v => v.FechaOperacion == fechaBusqueda
                             && v.Frecuencia.DiaSemana != 7
                             && v.Frecuencia.Ruta.AeropuertoOrigenId == origenId.Value
                             && v.Frecuencia.Ruta.AeropuertoDestinoId == destinoId.Value)
                    .OrderBy(v => v.Frecuencia.HoraSalida)
                    .ToList();
            }

            ViewBag.OrigenId = origenId.Value;
            ViewBag.DestinoId = destinoId.Value;
            ViewBag.CiudadOrigen = aeroOrigen != null ? aeroOrigen.Localidad + " (" + aeroOrigen.CodigoIATA + ")" : "Origen";
            ViewBag.CiudadDestino = aeroDestino != null ? aeroDestino.Localidad + " (" + aeroDestino.CodigoIATA + ")" : "Destino";
            ViewBag.Fecha = fechaBusqueda;
            ViewBag.Pasajeros = (pasajeros >= 1 && pasajeros <= 7) ? pasajeros : 1;

            return View(vuelosResultado);
        }



        [HttpGet]
        [AllowAnonymous]
        public ActionResult Disponibilidad(int? id, int pasajeros = 1)
        {
            if (!id.HasValue)
            {
                TempData["Error"] = "Identificador de vuelo no válido.";
                return RedirectToAction("Buscar");
            }

            var vuelo = db.Vuelo.Find(id.Value);
            if (vuelo == null || vuelo.EstadoVueloCodigo == "CANCELADO")
            {
                TempData["Error"] = "El vuelo seleccionado no está disponible o ha sido cancelado.";
                return RedirectToAction("Buscar");
            }

            // Regla RNP-01: No comercializar vuelos sin aeronave
            if (vuelo.AeronaveId == null)
            {
                TempData["Error"] = "Este vuelo aún no tiene aeronave asignada; sus asientos no pueden consultarse.";
                return RedirectToAction("Buscar");
            }

            ViewBag.HoraCorte = DateTime.Now.ToString("HH:mm");

            // se conserva la cantidad de pasajeros de la búsqueda
            ViewBag.Pasajeros = (pasajeros >= 1 && pasajeros <= 7) ? pasajeros : 1;

            // del itinerario completo (DP-09: los tramos del domingo no se venden sueltos)
            int origenBusqueda = vuelo.Frecuencia.Ruta.AeropuertoOrigenId;
            int destinoBusqueda = vuelo.Frecuencia.Ruta.AeropuertoDestinoId;

            if (vuelo.Frecuencia.DiaSemana == 7)
            {
                var uio = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "UIO");
                var gps = db.Aeropuerto.FirstOrDefault(a => a.CodigoIATA == "GPS");
                if (uio != null && gps != null)
                {
                    // Ida: UIO→GYE o GYE→GPS   ·   Regreso: GPS→GYE o GYE→UIO
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


        // GET: Vuelos  (CRUD de scaffolding)
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
        public ActionResult Create()
        {
            ViewBag.AeronaveId = new SelectList(db.Aeronave, "AeronaveId", "Matricula");
            ViewBag.EstadoVueloCodigo = new SelectList(db.EstadoVuelo, "Codigo", "Descripcion");
            ViewBag.FrecuenciaId = new SelectList(db.Frecuencia, "FrecuenciaId", "FrecuenciaId");
            return View();
        }

        // POST: Vuelos/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
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
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
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
