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

            // Caso Quito -> Galápagos (Itinerario dominical con escala de ida)
            if (iataOrigen == "UIO" && iataDestino == "GPS")
            {
                ViewBag.EsItinerarioEscala = true;

                // Filtramos ÚNICAMENTE los 2 tramos de ida:
                // 1) UIO -> GYE (09:00) y 2) GYE -> GPS (11:00)
                vuelosResultado = db.TramoItinerario
                    .Where(t => t.Itinerario.EstadoItinerarioCodigo == "ACTIVO"
                             && t.Vuelo.FechaOperacion == fechaBusqueda
                             && (t.Vuelo.Frecuencia.HoraSalida.Hours == 9 || t.Vuelo.Frecuencia.HoraSalida.Hours == 11))
                    .OrderBy(t => t.Vuelo.Frecuencia.HoraSalida)
                    .Select(t => t.Vuelo)
                    .ToList();
            }
            else
            {
                ViewBag.EsItinerarioEscala = false;

                // Vuelos directos estándar
                vuelosResultado = db.Vuelo
                    .Where(v => v.FechaOperacion == fechaBusqueda
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
        public ActionResult Disponibilidad(int? id)
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

            return View(vuelo);
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
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Vuelo vuelo = db.Vuelo.Find(id);
            db.Vuelo.Remove(vuelo);
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
