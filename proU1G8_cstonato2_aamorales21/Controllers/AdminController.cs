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
    // Controlador único del área administrativa (pantallas 17 a 21).
    // Cada acción valida el rol en el servidor (RNP-09, RG-38).
    [Authorize]
    public class AdminController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // Administrador o SuperUsuario
        private bool EsAdministrativo()
        {
            return User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
        }

        // =========================================================================
        // PANTALLA 17: PANEL DEL ADMINISTRADOR (/Admin/Index)
        // =========================================================================
        [HttpGet]
        public ActionResult Index()
        {
            if (!EsAdministrativo())
            {
                ViewBag.SinPermiso = true;
                return View();
            }

            ViewBag.SinPermiso = false;
            ViewBag.EsSuperUsuario = User.IsInRole("SuperUsuario");
            return View();
        }
        // =========================================================================
        // PANTALLA 18: ADMINISTRACIÓN DE VUELOS E ITINERARIOS (/Admin/Vuelos)
        // =========================================================================

        // Lee un parámetro entero de Web.config (appSettings) o usa el valor por defecto
        private int LeerParametro(string clave, int valorPorDefecto)
        {
            int valor;
            if (int.TryParse(System.Configuration.ConfigurationManager.AppSettings[clave], out valor))
            {
                return valor;
            }
            return valorPorDefecto;
        }

        private DateTime Salida(Vuelo v)
        {
            return v.FechaOperacion.Add(v.Frecuencia.HoraSalida);
        }

        private DateTime Llegada(Vuelo v)
        {
            return v.FechaOperacion.Add(v.Frecuencia.HoraLlegada);
        }

        // Revisa los vuelos de cada aeronave y devuelve la lista de conflictos y advertencias.
        // Cada elemento: [0] = "CONFLICTO" o "ADVERTENCIA", [1] = texto.
        private List<string[]> BuscarConflictos(List<Vuelo> vuelos)
        {
            int tiempoEnTierra = LeerParametro("TiempoMinimoEnTierraMin", 45);
            var resultado = new List<string[]>();

            var porAeronave = vuelos
                .Where(v => v.AeronaveId != null && v.EstadoVueloCodigo != "CANCELADO")
                .GroupBy(v => v.Aeronave.Matricula);

            foreach (var grupo in porAeronave)
            {
                var lista = grupo.OrderBy(v => Salida(v)).ToList();
                for (int i = 1; i < lista.Count; i++)
                {
                    var anterior = lista[i - 1];
                    var actual = lista[i];
                    string codAnt = "VIP" + anterior.VueloId.ToString("D5");
                    string codAct = "VIP" + actual.VueloId.ToString("D5");
                    string destinoAnt = anterior.Frecuencia.Ruta.Aeropuerto1.CodigoIATA;
                    string origenAct = actual.Frecuencia.Ruta.Aeropuerto.CodigoIATA;

                    if (anterior.FechaOperacion == actual.FechaOperacion)
                    {
                        double minutos = (Salida(actual) - Llegada(anterior)).TotalMinutes;
                        if (minutos < tiempoEnTierra)
                        {
                            resultado.Add(new[] { "CONFLICTO", "La aeronave " + grupo.Key + " tiene " + codAnt + " y " + codAct
                                + " el " + actual.FechaOperacion.ToString("dd/MM/yyyy") + " con solo " + minutos + " min en tierra (mínimo " + tiempoEnTierra + ")." });
                        }
                        if (destinoAnt != origenAct)
                        {
                            resultado.Add(new[] { "CONFLICTO", "La aeronave " + grupo.Key + " termina " + codAnt + " en " + destinoAnt
                                + " pero " + codAct + " sale de " + origenAct + " el mismo día." });
                        }
                    }
                    else if (destinoAnt != origenAct)
                    {
                        resultado.Add(new[] { "ADVERTENCIA", "La aeronave " + grupo.Key + " pernocta en " + destinoAnt + " tras " + codAnt
                            + " (" + anterior.FechaOperacion.ToString("dd/MM") + ") y su siguiente vuelo " + codAct + " sale de " + origenAct
                            + " (" + actual.FechaOperacion.ToString("dd/MM") + "). Requiere traslado sin pasajeros." });
                    }
                }
            }
            return resultado;
        }

        // GET: /Admin/Vuelos
        [HttpGet]
        public ActionResult Vuelos(string tab, DateTime? desde, DateTime? hasta, int? rutaId, string estado, int? editar, bool conflictos = false)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            tab = (tab == "frecuencias" || tab == "itinerarios") ? tab : "vuelos";
            DateTime fDesde = (desde ?? DateTime.Today).Date;
            DateTime fHasta = (hasta ?? DateTime.Today.AddDays(13)).Date;

            if (fHasta < fDesde)
            {
                ViewBag.ErrorFiltro = "La fecha «Hasta» no puede ser anterior a la fecha «Desde».";
                fHasta = fDesde.AddDays(13);
            }
            if ((fHasta - fDesde).TotalDays > 366)
            {
                ViewBag.ErrorFiltro = "El rango no puede superar 366 días.";
                fHasta = fDesde.AddDays(366);
            }

            // ---- Vuelos del rango ----
            var consulta = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave)
                .Where(v => v.FechaOperacion >= fDesde && v.FechaOperacion <= fHasta);

            if (rutaId.HasValue)
                consulta = consulta.Where(v => v.Frecuencia.RutaId == rutaId.Value);
            if (!string.IsNullOrEmpty(estado))
                consulta = consulta.Where(v => v.EstadoVueloCodigo == estado);

            var vuelos = consulta.ToList()
                .OrderBy(v => v.FechaOperacion).ThenBy(v => v.Frecuencia.HoraSalida)
                .ToList();

            // Asientos ocupados por vuelo (PENDIENTE o CONFIRMADA)
            var idsVuelos = vuelos.Select(v => v.VueloId).ToList();
            var ocupados = db.ReservaPasajeroVuelo
                .Where(a => idsVuelos.Contains(a.VueloId) && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA"))
                .GroupBy(a => a.VueloId)
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.Ocupados = ocupados;

            // ---- Consultar conflictos (sobre todos los vuelos del rango, sin filtros de ruta/estado) ----
            if (conflictos)
            {
                var todos = db.Vuelo
                    .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                    .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                    .Include(v => v.Aeronave)
                    .Where(v => v.FechaOperacion >= fDesde && v.FechaOperacion <= fHasta)
                    .ToList();
                ViewBag.Conflictos = BuscarConflictos(todos);
            }

            // ---- Vuelo en edición ----
            if (editar.HasValue)
            {
                var vueloEditar = db.Vuelo.Find(editar.Value);
                if (vueloEditar != null)
                {
                    ViewBag.VueloEditar = vueloEditar;
                    ViewBag.AeronavesLista = new SelectList(
                        db.Aeronave.OrderBy(a => a.Matricula).ToList()
                          .Select(a => new { a.AeronaveId, Texto = a.Matricula + (a.EstadoAeronaveCodigo == "DISPONIBLE" ? "" : " (no disponible)") }),
                        "AeronaveId", "Texto", vueloEditar.AeronaveId);
                    ViewBag.EstadosLista = new SelectList(db.EstadoVuelo.ToList(), "Codigo", "Descripcion", vueloEditar.EstadoVueloCodigo);
                }
            }

            // ---- Pestaña frecuencias ----
            if (tab == "frecuencias")
            {
                ViewBag.Frecuencias = db.Frecuencia
                    .Include(f => f.Ruta.Aeropuerto)
                    .Include(f => f.Ruta.Aeropuerto1)
                    .ToList()
                    .OrderBy(f => f.DiaSemana).ThenBy(f => f.HoraSalida)
                    .ToList();
            }

            // ---- Pestaña itinerarios (los que tienen tramos en el rango) ----
            if (tab == "itinerarios")
            {
                ViewBag.Itinerarios = db.Itinerario
                    .Where(i => i.TramoItinerario.Any(t => t.Vuelo.FechaOperacion >= fDesde && t.Vuelo.FechaOperacion <= fHasta))
                    .ToList()
                    .OrderBy(i => i.TramoItinerario.Min(t => t.Vuelo.FechaOperacion))
                    .ToList();
            }

            // Listas de filtros
            ViewBag.RutasLista = new SelectList(
                db.Ruta.ToList().Select(r => new { r.RutaId, Texto = r.Aeropuerto.CodigoIATA + " → " + r.Aeropuerto1.CodigoIATA }),
                "RutaId", "Texto", rutaId);
            ViewBag.EstadosFiltro = new SelectList(db.EstadoVuelo.ToList(), "Codigo", "Descripcion", estado);

            ViewBag.Tab = tab;
            ViewBag.Desde = fDesde.ToString("yyyy-MM-dd");
            ViewBag.Hasta = fHasta.ToString("yyyy-MM-dd");
            ViewBag.RutaId = rutaId;
            ViewBag.Estado = estado ?? "";
            ViewBag.MostrarConflictos = conflictos;

            return View(vuelos);
        }

        // POST: /Admin/EditarVuelo  (cambiar aeronave o estado)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarVuelo(int vueloId, int? aeronaveId, string estadoVueloCodigo, string desde, string hasta)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            var vuelo = db.Vuelo.Find(vueloId);
            if (vuelo == null)
            {
                TempData["Error"] = "El vuelo no existe.";
                return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta });
            }

            string codigo = "VIP" + vuelo.VueloId.ToString("D5");

            // No se modifican vuelos COMPLETADOS
            if (vuelo.EstadoVueloCodigo == "COMPLETADO")
            {
                TempData["Error"] = "El vuelo " + codigo + " está completado y no puede modificarse.";
                return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta });
            }

            if (string.IsNullOrEmpty(estadoVueloCodigo) || !db.EstadoVuelo.Any(e => e.Codigo == estadoVueloCodigo))
            {
                TempData["Error"] = "Seleccione un estado válido.";
                return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId });
            }

            bool cambiaAeronave = vuelo.AeronaveId != aeronaveId;

            // RG-05: un vuelo cancelado no admite cambio de aeronave
            if (cambiaAeronave && vuelo.EstadoVueloCodigo == "CANCELADO")
            {
                TempData["Error"] = "El vuelo " + codigo + " está cancelado y no admite cambios de aeronave.";
                return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId });
            }

            // Un vuelo con asientos vendidos no puede quedar sin aeronave (RG-03 / RG-04)
            int asientosVigentes = vuelo.ReservaPasajeroVuelo.Count(a => a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA");
            if (!aeronaveId.HasValue && asientosVigentes > 0)
            {
                TempData["Error"] = "El vuelo " + codigo + " tiene " + asientosVigentes + " asientos vigentes; no puede quedar sin aeronave.";
                return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId });
            }

            // RG-06 y RG-07: tiempo en tierra y continuidad con los vuelos del mismo día de esa aeronave
            if (aeronaveId.HasValue && cambiaAeronave && estadoVueloCodigo != "CANCELADO")
            {
                int tiempoEnTierra = LeerParametro("TiempoMinimoEnTierraMin", 45);
                var mismoDia = db.Vuelo
                    .Where(v => v.AeronaveId == aeronaveId.Value && v.FechaOperacion == vuelo.FechaOperacion
                             && v.VueloId != vuelo.VueloId && v.EstadoVueloCodigo != "CANCELADO")
                    .ToList();

                var anterior = mismoDia.Where(v => Salida(v) < Salida(vuelo)).OrderByDescending(v => Salida(v)).FirstOrDefault();
                var siguiente = mismoDia.Where(v => Salida(v) > Salida(vuelo)).OrderBy(v => Salida(v)).FirstOrDefault();
                string matricula = db.Aeronave.Find(aeronaveId.Value).Matricula;

                if (anterior != null)
                {
                    double minutos = (Salida(vuelo) - Llegada(anterior)).TotalMinutes;
                    if (minutos < tiempoEnTierra || anterior.Frecuencia.Ruta.AeropuertoDestinoId != vuelo.Frecuencia.Ruta.AeropuertoOrigenId)
                    {
                        TempData["Error"] = "Conflicto operativo: la aeronave " + matricula + " ya tiene el vuelo VIP" + anterior.VueloId.ToString("D5")
                            + " (" + anterior.Frecuencia.HoraSalida.ToString(@"hh\:mm") + "–" + anterior.Frecuencia.HoraLlegada.ToString(@"hh\:mm")
                            + ") y no cumple el tiempo en tierra o la continuidad de aeropuerto. No se guardó la asignación.";
                        return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId });
                    }
                }
                if (siguiente != null)
                {
                    double minutos = (Salida(siguiente) - Llegada(vuelo)).TotalMinutes;
                    if (minutos < tiempoEnTierra || vuelo.Frecuencia.Ruta.AeropuertoDestinoId != siguiente.Frecuencia.Ruta.AeropuertoOrigenId)
                    {
                        TempData["Error"] = "Conflicto operativo: la aeronave " + matricula + " ya tiene el vuelo VIP" + siguiente.VueloId.ToString("D5")
                            + " (" + siguiente.Frecuencia.HoraSalida.ToString(@"hh\:mm") + "–" + siguiente.Frecuencia.HoraLlegada.ToString(@"hh\:mm")
                            + ") y no cumple el tiempo en tierra o la continuidad de aeropuerto. No se guardó la asignación.";
                        return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId });
                    }
                }
            }

            vuelo.AeronaveId = aeronaveId;
            vuelo.EstadoVueloCodigo = estadoVueloCodigo;

            try
            {
                db.SaveChanges();   // el trigger trg_Vuelo_Validar revalida día, aeronave disponible y superposición
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                TempData["Error"] = "No se guardó el cambio: " + inner.Message;
                return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId });
            }

            string aviso = (estadoVueloCodigo == "CANCELADO" && asientosVigentes > 0)
                ? " Atención: el vuelo tiene " + asientosVigentes + " asientos vigentes; revise las reservas afectadas en la pantalla 20."
                : "";
            TempData["Exito"] = "Cambios guardados en el vuelo " + codigo + "." + aviso;
            return RedirectToAction("Vuelos", new { desde = desde, hasta = hasta });
        }

        // POST: /Admin/GenerarVuelos  (ejecuta usp_GenerarVuelos)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GenerarVuelos(DateTime? desde, DateTime? hasta)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            if (!desde.HasValue || !hasta.HasValue || hasta.Value < desde.Value || (hasta.Value - desde.Value).TotalDays > 365)
            {
                TempData["Error"] = "Indique un rango de fechas válido (máximo 366 días) para generar vuelos.";
                return RedirectToAction("Vuelos");
            }

            DateTime d = desde.Value.Date;
            DateTime h = hasta.Value.Date;

            try
            {
                int antes = db.Vuelo.Count();
                db.Database.ExecuteSqlCommand("EXEC dbo.usp_GenerarVuelos @p0, @p1", d, h);
                int generados = db.Vuelo.Count() - antes;

                TempData["Exito"] = generados > 0
                    ? "Se generaron " + generados + " vuelos. Quedan PROGRAMADOS y sin aeronave hasta que se asignen."
                    : "No se generaron vuelos nuevos: el rango ya estaba programado.";
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                TempData["Error"] = "No se pudieron generar los vuelos: " + inner.Message;
            }

            return RedirectToAction("Vuelos", new { desde = d.ToString("yyyy-MM-dd"), hasta = h.ToString("yyyy-MM-dd") });
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