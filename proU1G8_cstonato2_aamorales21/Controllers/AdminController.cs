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
        // =========================================================================
        // PANTALLA 19: ADMINISTRACIÓN DE AERONAVES (/Admin/Aeronaves)
        // =========================================================================

        // GET: /Admin/Aeronaves  (id = aeronave consultada; editar = mostrar formulario)
        [HttpGet]
        public ActionResult Aeronaves(int? id, bool editar = false)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            DateTime hoy = DateTime.Today;
            var aeronaves = db.Aeronave
                .Include(a => a.ModeloAeronave)
                .OrderBy(a => a.Matricula)
                .ToList();

            // Vuelos próximos (desde hoy, no cancelados ni completados) por aeronave
            var proximos = db.Vuelo
                .Where(v => v.AeronaveId != null && v.FechaOperacion >= hoy
                         && v.EstadoVueloCodigo != "CANCELADO" && v.EstadoVueloCodigo != "COMPLETADO")
                .GroupBy(v => v.AeronaveId.Value)
                .ToDictionary(g => g.Key, g => g.Count());
            ViewBag.Proximos = proximos;

            // Aeronave seleccionada: detalle, distribución y próximas asignaciones
            Aeronave seleccionada = null;
            if (id.HasValue)
            {
                seleccionada = aeronaves.FirstOrDefault(a => a.AeronaveId == id.Value);
                if (seleccionada == null)
                {
                    TempData["Error"] = "La aeronave solicitada no existe.";
                    return RedirectToAction("Aeronaves");
                }

                ViewBag.Asientos = db.Asiento
                    .Where(s => s.ModeloAeronaveId == seleccionada.ModeloAeronaveId)
                    .OrderBy(s => s.Fila).ThenBy(s => s.Lado)
                    .ToList();

                ViewBag.Asignaciones = db.Vuelo
                    .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                    .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                    .Where(v => v.AeronaveId == seleccionada.AeronaveId && v.FechaOperacion >= hoy
                             && v.EstadoVueloCodigo != "CANCELADO")
                    .ToList()
                    .OrderBy(v => Salida(v))
                    .ToList();

                if (editar)
                {
                    ViewBag.ModelosLista = new SelectList(
                        db.ModeloAeronave.ToList().Select(m => new { m.ModeloAeronaveId, Texto = m.Fabricante + " " + m.Nombre }),
                        "ModeloAeronaveId", "Texto", seleccionada.ModeloAeronaveId);
                    ViewBag.EstadosLista = new SelectList(db.EstadoAeronave.ToList(), "Codigo", "Descripcion", seleccionada.EstadoAeronaveCodigo);
                }
            }

            ViewBag.Seleccionada = seleccionada;
            ViewBag.Editar = editar && seleccionada != null;
            return View(aeronaves);
        }

        // POST: /Admin/EditarAeronave
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarAeronave(int aeronaveId, string matricula, int modeloAeronaveId, string estadoAeronaveCodigo)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            var aeronave = db.Aeronave.Find(aeronaveId);
            if (aeronave == null)
            {
                TempData["Error"] = "La aeronave solicitada no existe.";
                return RedirectToAction("Aeronaves");
            }

            matricula = (matricula ?? "").Trim().ToUpper();

            // Validaciones del servidor
            if (matricula.Length == 0 || matricula.Length > 15)
            {
                TempData["Error"] = "El identificador es obligatorio y admite hasta 15 caracteres.";
                return RedirectToAction("Aeronaves", new { id = aeronaveId, editar = true });
            }
            if (db.Aeronave.Any(a => a.Matricula == matricula && a.AeronaveId != aeronaveId))
            {
                TempData["Error"] = "Ya existe otra aeronave con el identificador " + matricula + ".";
                return RedirectToAction("Aeronaves", new { id = aeronaveId, editar = true });
            }
            var modelo = db.ModeloAeronave.Find(modeloAeronaveId);
            if (modelo == null || modelo.CapacidadAsientos > 7)
            {
                TempData["Error"] = "Seleccione un modelo válido (capacidad máxima de siete asientos, RN-03).";
                return RedirectToAction("Aeronaves", new { id = aeronaveId, editar = true });
            }
            if (string.IsNullOrEmpty(estadoAeronaveCodigo) || !db.EstadoAeronave.Any(e => e.Codigo == estadoAeronaveCodigo))
            {
                TempData["Error"] = "Seleccione un estado operativo válido.";
                return RedirectToAction("Aeronaves", new { id = aeronaveId, editar = true });
            }

            // Vuelos próximos que quedarían afectados si pasa a NO_DISPONIBLE
            DateTime hoy = DateTime.Today;
            int vuelosProximos = db.Vuelo.Count(v => v.AeronaveId == aeronaveId && v.FechaOperacion >= hoy
                                                  && v.EstadoVueloCodigo != "CANCELADO" && v.EstadoVueloCodigo != "COMPLETADO");

            aeronave.Matricula = matricula;
            aeronave.ModeloAeronaveId = modeloAeronaveId;
            aeronave.EstadoAeronaveCodigo = estadoAeronaveCodigo;

            try
            {
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                TempData["Error"] = "No se guardaron los cambios: " + inner.Message;
                return RedirectToAction("Aeronaves", new { id = aeronaveId, editar = true });
            }

            string aviso = (estadoAeronaveCodigo == "NO_DISPONIBLE" && vuelosProximos > 0)
                ? " Atención: tiene " + vuelosProximos + " vuelos próximos asignados; reasígnelos en la pantalla 18."
                : "";
            TempData["Exito"] = "Cambios guardados en la aeronave " + matricula + "." + aviso;
            return RedirectToAction("Aeronaves", new { id = aeronaveId });
        }

        // =========================================================================
        // PANTALLA 20: GESTIÓN DE USUARIOS, PASAJEROS Y RESERVAS (/Admin/Gestion)
        // =========================================================================

        private static readonly string[] RolesPrincipales = { "SuperUsuario", "Administrador", "Counter", "Cliente" };

        private UserManager<ApplicationUser> CrearUserManager(ApplicationDbContext identity)
        {
            return new UserManager<ApplicationUser>(new Microsoft.AspNet.Identity.EntityFramework.UserStore<ApplicationUser>(identity));
        }

        // GET: /Admin/Gestion
        [HttpGet]
        public ActionResult Gestion(string tab, string buscar, string filtro, int pagina = 1,
                                    string verUsuario = null, string editarUsuario = null,
                                    int? editarPasajero = null, bool nuevoPasajero = false)
        {
            bool esAdmin = EsAdministrativo();
            bool esCounter = User.IsInRole("Counter");

            if (!esAdmin && !esCounter)
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            // El Counter no ve la pestaña de usuarios
            tab = (tab == "pasajeros" || tab == "reservas") ? tab : (esAdmin ? "usuarios" : "reservas");
            if (tab == "usuarios" && !esAdmin) tab = "reservas";

            buscar = (buscar ?? "").Trim();
            filtro = filtro ?? "";
            int tamanoPagina = 15;
            if (pagina < 1) pagina = 1;

            ViewBag.Tab = tab;
            ViewBag.Buscar = buscar;
            ViewBag.Filtro = filtro;
            ViewBag.EsAdmin = esAdmin;
            ViewBag.EsSuperUsuario = User.IsInRole("SuperUsuario");

            using (var identity = new ApplicationDbContext())
            {
                // Nombres de rol por Id (para mostrar el rol de cada usuario)
                var roles = identity.Roles.ToList();
                var nombreRol = roles.ToDictionary(r => r.Id, r => r.Name);

                // Nombre de usuario por Id (para la pestaña de reservas)
                var usuarios = identity.Users.ToList();
                ViewBag.NombreUsuario = usuarios.ToDictionary(u => u.Id, u => u.UserName);

                // ---------------- PESTAÑA USUARIOS ----------------
                if (tab == "usuarios")
                {
                    var lista = usuarios.AsEnumerable();
                    if (buscar != "")
                    {
                        string b = buscar.ToLower();
                        lista = lista.Where(u => u.UserName.ToLower().Contains(b) || (u.Email ?? "").ToLower().Contains(b));
                    }

                    // Rol principal de cada usuario
                    var rolesUsuario = new Dictionary<string, string>();
                    foreach (var u in usuarios)
                    {
                        var nombres = u.Roles.Select(r => nombreRol[r.RoleId]).ToList();
                        string principal = RolesPrincipales.FirstOrDefault(rp => nombres.Contains(rp)) ?? "Sin rol";
                        rolesUsuario[u.Id] = principal;
                    }
                    if (filtro != "")
                    {
                        lista = lista.Where(u => rolesUsuario[u.Id] == filtro);
                    }

                    var filtrados = lista.OrderBy(u => u.UserName).ToList();
                    ViewBag.TotalPaginas = Math.Max(1, (int)Math.Ceiling(filtrados.Count / (double)tamanoPagina));
                    ViewBag.Usuarios = filtrados.Skip((pagina - 1) * tamanoPagina).Take(tamanoPagina).ToList();
                    ViewBag.RolesUsuario = rolesUsuario;

                    // Historial de un usuario
                    if (!string.IsNullOrEmpty(verUsuario))
                    {
                        var usuarioVer = usuarios.FirstOrDefault(u => u.Id == verUsuario);
                        if (usuarioVer != null)
                        {
                            ViewBag.UsuarioVer = usuarioVer;
                            ViewBag.HistorialReservas = db.Reserva
                                .Where(r => r.UsuarioId == usuarioVer.Id)
                                .OrderByDescending(r => r.FechaCreacion)
                                .ToList();
                        }
                    }

                    // Edición de un usuario
                    if (!string.IsNullOrEmpty(editarUsuario))
                    {
                        var usuarioEditar = usuarios.FirstOrDefault(u => u.Id == editarUsuario);
                        if (usuarioEditar != null)
                        {
                            ViewBag.UsuarioEditar = usuarioEditar;
                            ViewBag.RolActual = rolesUsuario[usuarioEditar.Id];
                        }
                    }
                }
            }

            // ---------------- PESTAÑA PASAJEROS ----------------
            if (tab == "pasajeros")
            {
                var consulta = db.Pasajero.AsQueryable();
                if (buscar != "")
                {
                    consulta = consulta.Where(p => p.NumeroDocumento.Contains(buscar) || p.Nombres.Contains(buscar)
                                                || p.Apellidos.Contains(buscar) || p.Correo.Contains(buscar));
                }
                if (filtro == "CEDULA" || filtro == "PASAPORTE")
                {
                    consulta = consulta.Where(p => p.TipoDocumentoCodigo == filtro);
                }

                int total = consulta.Count();
                ViewBag.TotalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)tamanoPagina));
                ViewBag.Pasajeros = consulta
                    .OrderBy(p => p.Apellidos).ThenBy(p => p.Nombres)
                    .Skip((pagina - 1) * tamanoPagina).Take(tamanoPagina)
                    .ToList();

                // Número de asignaciones de cada pasajero (para saber si se puede eliminar)
                ViewBag.AsignacionesPasajero = db.ReservaPasajeroVuelo
                    .GroupBy(a => a.PasajeroId)
                    .ToDictionary(g => g.Key, g => g.Count());

                // Formulario de creación / edición (solo administrativos)
                if (esAdmin && (nuevoPasajero || editarPasajero.HasValue))
                {
                    var pasajero = editarPasajero.HasValue ? db.Pasajero.Find(editarPasajero.Value) : new Pasajero { TipoDocumentoCodigo = "CEDULA" };
                    if (pasajero != null)
                    {
                        ViewBag.PasajeroForm = pasajero;
                        ViewBag.TiposDocumento = new SelectList(db.TipoDocumentoIdentidad.ToList(), "Codigo", "Descripcion", pasajero.TipoDocumentoCodigo);
                    }
                }
            }

            // ---------------- PESTAÑA RESERVAS ----------------
            if (tab == "reservas")
            {
                var consulta = db.Reserva.Include(r => r.Compra).AsQueryable();
                if (buscar != "")
                {
                    string b = buscar.ToUpper();
                    consulta = consulta.Where(r => r.CodigoReserva.Contains(b)
                        || r.ReservaPasajeroVuelo.Any(a => a.Pasajero.NumeroDocumento.Contains(b)));
                }
                if (filtro != "")
                {
                    consulta = consulta.Where(r => r.EstadoReservaCodigo == filtro);
                }

                int total = consulta.Count();
                ViewBag.TotalPaginas = Math.Max(1, (int)Math.Ceiling(total / (double)tamanoPagina));
                ViewBag.Reservas = consulta
                    .OrderByDescending(r => r.FechaCreacion)
                    .Skip((pagina - 1) * tamanoPagina).Take(tamanoPagina)
                    .ToList();
            }

            ViewBag.Pagina = pagina;
            return View();
        }

        // POST: /Admin/EditarUsuario  (correo electrónico)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarUsuario(string userId, string email)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            email = (email ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                TempData["Error"] = "Ingrese un correo electrónico válido.";
                return RedirectToAction("Gestion", new { tab = "usuarios", editarUsuario = userId });
            }

            using (var identity = new ApplicationDbContext())
            {
                var userManager = CrearUserManager(identity);
                var usuario = userManager.FindById(userId);
                if (usuario == null)
                {
                    TempData["Error"] = "El usuario no existe.";
                    return RedirectToAction("Gestion", new { tab = "usuarios" });
                }

                // Un Administrador solo edita cuentas de Cliente o Counter
                bool cuentaElevada = userManager.IsInRole(userId, "SuperUsuario") || userManager.IsInRole(userId, "Administrador");
                if (cuentaElevada && !User.IsInRole("SuperUsuario"))
                {
                    TempData["Error"] = "Solo el SuperUsuario puede modificar cuentas administrativas.";
                    return RedirectToAction("Gestion", new { tab = "usuarios" });
                }

                usuario.Email = email;
                var resultado = userManager.Update(usuario);   // valida correo único (RequireUniqueEmail)
                if (!resultado.Succeeded)
                {
                    TempData["Error"] = "No se guardaron los cambios: " + string.Join(" ", resultado.Errors);
                    return RedirectToAction("Gestion", new { tab = "usuarios", editarUsuario = userId });
                }

                TempData["Exito"] = "Cambios guardados para " + usuario.UserName + ".";
            }
            return RedirectToAction("Gestion", new { tab = "usuarios" });
        }

        // POST: /Admin/AsignarRol  (solo SuperUsuario)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AsignarRol(string userId, string rol)
        {
            if (!User.IsInRole("SuperUsuario"))
            {
                TempData["Error"] = "Solo el SuperUsuario puede asignar roles.";
                return RedirectToAction("Gestion", new { tab = "usuarios" });
            }

            if (!RolesPrincipales.Contains(rol))
            {
                TempData["Error"] = "Seleccione un rol válido.";
                return RedirectToAction("Gestion", new { tab = "usuarios", editarUsuario = userId });
            }

            // El SuperUsuario no puede quitarse su propio rol (evita quedar sin acceso)
            if (userId == User.Identity.GetUserId() && rol != "SuperUsuario")
            {
                TempData["Error"] = "No puede quitarse su propio rol de SuperUsuario.";
                return RedirectToAction("Gestion", new { tab = "usuarios", editarUsuario = userId });
            }

            using (var identity = new ApplicationDbContext())
            {
                var userManager = CrearUserManager(identity);
                var usuario = userManager.FindById(userId);
                if (usuario == null)
                {
                    TempData["Error"] = "El usuario no existe.";
                    return RedirectToAction("Gestion", new { tab = "usuarios" });
                }

                // Quita los otros roles principales y asigna el nuevo (los roles CRUD no se tocan)
                foreach (var r in RolesPrincipales)
                {
                    if (r != rol && userManager.IsInRole(userId, r))
                        userManager.RemoveFromRole(userId, r);
                }
                if (!userManager.IsInRole(userId, rol))
                    userManager.AddToRole(userId, rol);

                TempData["Exito"] = "Rol de " + usuario.UserName + " actualizado a " + rol + ". El cambio se aplica en su próximo inicio de sesión.";
            }
            return RedirectToAction("Gestion", new { tab = "usuarios" });
        }

        // POST: /Admin/GuardarPasajero  (crear o editar)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult GuardarPasajero(int? pasajeroId, string tipoDocumentoCodigo, string numeroDocumento, string nombres,
                                            string apellidos, DateTime? fechaNacimiento, string correo, string telefono)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para modificar pasajeros.";
                return RedirectToAction("Gestion", new { tab = "pasajeros" });
            }

            numeroDocumento = (numeroDocumento ?? "").Trim().ToUpper();
            nombres = (nombres ?? "").Trim();
            apellidos = (apellidos ?? "").Trim();
            correo = string.IsNullOrWhiteSpace(correo) ? null : correo.Trim();
            telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();

            // Validaciones del servidor (coinciden con las restricciones CHECK de la tabla Pasajero)
            string error = null;
            if (tipoDocumentoCodigo != "CEDULA" && tipoDocumentoCodigo != "PASAPORTE")
                error = "Seleccione el tipo de documento.";
            else if (tipoDocumentoCodigo == "CEDULA" && !System.Text.RegularExpressions.Regex.IsMatch(numeroDocumento, @"^\d{10}$"))
                error = "La cédula debe contener exactamente 10 dígitos numéricos.";
            else if (numeroDocumento.Length < 5 || numeroDocumento.Length > 20)
                error = "El número de documento debe tener entre 5 y 20 caracteres.";
            else if (nombres.Length == 0 || nombres.Length > 100 || apellidos.Length == 0 || apellidos.Length > 100)
                error = "Nombres y apellidos son obligatorios (máximo 100 caracteres).";
            else if (correo != null && !System.Text.RegularExpressions.Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                error = "El correo electrónico no tiene un formato válido.";
            else if (telefono != null && !System.Text.RegularExpressions.Regex.IsMatch(telefono, @"^[0-9+ ]{7,20}$"))
                error = "El teléfono solo admite dígitos, espacios y el signo + (7 a 20 caracteres).";
            else if (fechaNacimiento.HasValue && (fechaNacimiento.Value.Year <= 1900 || fechaNacimiento.Value.Date > DateTime.Today))
                error = "La fecha de nacimiento no es válida.";
            else if (db.Pasajero.Any(p => p.TipoDocumentoCodigo == tipoDocumentoCodigo && p.NumeroDocumento == numeroDocumento
                                       && (!pasajeroId.HasValue || p.PasajeroId != pasajeroId.Value)))
                error = "Ya existe un pasajero con ese documento.";

            if (error != null)
            {
                TempData["Error"] = error;
                return pasajeroId.HasValue
                    ? RedirectToAction("Gestion", new { tab = "pasajeros", editarPasajero = pasajeroId })
                    : RedirectToAction("Gestion", new { tab = "pasajeros", nuevoPasajero = true });
            }

            Pasajero pasajero;
            if (pasajeroId.HasValue)
            {
                pasajero = db.Pasajero.Find(pasajeroId.Value);
                if (pasajero == null)
                {
                    TempData["Error"] = "El pasajero no existe.";
                    return RedirectToAction("Gestion", new { tab = "pasajeros" });
                }
            }
            else
            {
                pasajero = new Pasajero { FechaRegistro = DateTime.Now };
                db.Pasajero.Add(pasajero);
            }

            pasajero.TipoDocumentoCodigo = tipoDocumentoCodigo;
            pasajero.NumeroDocumento = numeroDocumento;
            pasajero.Nombres = nombres;
            pasajero.Apellidos = apellidos;
            pasajero.FechaNacimiento = fechaNacimiento;
            pasajero.Correo = correo;
            pasajero.Telefono = telefono;

            try
            {
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                TempData["Error"] = "No se guardó el pasajero: " + inner.Message;
                return RedirectToAction("Gestion", new { tab = "pasajeros" });
            }

            TempData["Exito"] = "Cambios guardados.";
            return RedirectToAction("Gestion", new { tab = "pasajeros", buscar = numeroDocumento });
        }

        // POST: /Admin/EliminarPasajero
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EliminarPasajero(int pasajeroId)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para modificar pasajeros.";
                return RedirectToAction("Gestion", new { tab = "pasajeros" });
            }

            var pasajero = db.Pasajero.Find(pasajeroId);
            if (pasajero == null)
            {
                TempData["Error"] = "El pasajero no existe.";
                return RedirectToAction("Gestion", new { tab = "pasajeros" });
            }

            // Integridad referencial: no se elimina un pasajero con reservas
            if (db.ReservaPasajeroVuelo.Any(a => a.PasajeroId == pasajeroId))
            {
                TempData["Error"] = "No se puede eliminar: el pasajero tiene reservas asociadas.";
                return RedirectToAction("Gestion", new { tab = "pasajeros" });
            }

            db.Pasajero.Remove(pasajero);
            db.SaveChanges();
            TempData["Exito"] = "Pasajero " + pasajero.Nombres + " " + pasajero.Apellidos + " eliminado.";
            return RedirectToAction("Gestion", new { tab = "pasajeros" });
        }

        // =========================================================================
        // PANTALLA 21: CONSULTAS Y REPORTES ADMINISTRATIVOS (/Admin/Reportes)
        // =========================================================================
        [HttpGet]
        public ActionResult Reportes(string consulta, DateTime? desde, DateTime? hasta, int? rutaId, int? aeronaveId, string estado)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            consulta = (consulta == "reservas" || consulta == "pagos") ? consulta : "ocupacion";
            DateTime fDesde = (desde ?? DateTime.Today.AddDays(-30)).Date;
            DateTime fHasta = (hasta ?? DateTime.Today.AddDays(30)).Date;
            estado = estado ?? "";

            if (fHasta < fDesde)
            {
                ViewBag.ErrorFiltro = "La fecha «Hasta» no puede ser anterior a la fecha «Desde».";
                fHasta = fDesde.AddDays(30);
            }
            if ((fHasta - fDesde).TotalDays > 366)
            {
                ViewBag.ErrorFiltro = "El rango no puede superar 366 días.";
                fHasta = fDesde.AddDays(366);
            }
            DateTime hastaExclusivo = fHasta.AddDays(1);   // incluye todo el día "hasta"

            // ---------------- OCUPACIÓN POR VUELO ----------------
            if (consulta == "ocupacion")
            {
                var vuelos = db.Vuelo
                    .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                    .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                    .Include(v => v.Aeronave.ModeloAeronave)
                    .Where(v => v.FechaOperacion >= fDesde && v.FechaOperacion <= fHasta);

                if (rutaId.HasValue) vuelos = vuelos.Where(v => v.Frecuencia.RutaId == rutaId.Value);
                if (aeronaveId.HasValue) vuelos = vuelos.Where(v => v.AeronaveId == aeronaveId.Value);
                if (estado != "") vuelos = vuelos.Where(v => v.EstadoVueloCodigo == estado);

                var lista = vuelos.ToList()
                    .OrderBy(v => v.FechaOperacion).ThenBy(v => v.Frecuencia.HoraSalida)
                    .ToList();

                // Reservados (PENDIENTE) y ocupados (CONFIRMADA) por vuelo
                var ids = lista.Select(v => v.VueloId).ToList();
                var asignaciones = db.ReservaPasajeroVuelo
                    .Where(a => ids.Contains(a.VueloId) && (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA"))
                    .Select(a => new { a.VueloId, a.EstadoAsignacionCodigo })
                    .ToList();

                ViewBag.Reservados = asignaciones.Where(a => a.EstadoAsignacionCodigo == "PENDIENTE")
                    .GroupBy(a => a.VueloId).ToDictionary(g => g.Key, g => g.Count());
                ViewBag.OcupadosConfirmados = asignaciones.Where(a => a.EstadoAsignacionCodigo == "CONFIRMADA")
                    .GroupBy(a => a.VueloId).ToDictionary(g => g.Key, g => g.Count());
                ViewBag.Vuelos = lista;
            }

            // ---------------- RESERVAS POR ESTADO ----------------
            if (consulta == "reservas")
            {
                var reservas = db.Reserva
                    .Include(r => r.Compra)
                    .Where(r => r.FechaCreacion >= fDesde && r.FechaCreacion < hastaExclusivo);

                if (rutaId.HasValue)
                    reservas = reservas.Where(r => r.ReservaPasajeroVuelo.Any(a => a.Vuelo.Frecuencia.RutaId == rutaId.Value));
                if (aeronaveId.HasValue)
                    reservas = reservas.Where(r => r.ReservaPasajeroVuelo.Any(a => a.Vuelo.AeronaveId == aeronaveId.Value));
                if (estado != "")
                    reservas = reservas.Where(r => r.EstadoReservaCodigo == estado);

                var lista = reservas.OrderByDescending(r => r.FechaCreacion).ToList();
                ViewBag.Reservas = lista;

                // Resumen por estado: [estado, cantidad, pasajeros, importe]
                ViewBag.ResumenReservas = lista
                    .GroupBy(r => r.EstadoReservaCodigo)
                    .Select(g => new object[]
                    {
                        g.Key,
                        g.Count(),
                        g.Sum(r => r.ReservaPasajeroVuelo.Select(a => a.PasajeroId).Distinct().Count()),
                        g.Sum(r => r.ReservaPasajeroVuelo.Sum(a => a.ImporteTramo))
                    })
                    .OrderBy(x => (string)x[0])
                    .ToList();
            }

            // ---------------- COMPRAS Y PAGOS SIMULADOS ----------------
            if (consulta == "pagos")
            {
                var compras = db.Compra
                    .Include(c => c.Reserva)
                    .Include(c => c.Pago)
                    .Where(c => c.FechaCompra >= fDesde && c.FechaCompra < hastaExclusivo);

                if (rutaId.HasValue)
                    compras = compras.Where(c => c.Reserva.ReservaPasajeroVuelo.Any(a => a.Vuelo.Frecuencia.RutaId == rutaId.Value));
                if (aeronaveId.HasValue)
                    compras = compras.Where(c => c.Reserva.ReservaPasajeroVuelo.Any(a => a.Vuelo.AeronaveId == aeronaveId.Value));
                if (estado != "")
                    compras = compras.Where(c => c.EstadoCompraCodigo == estado);

                ViewBag.Compras = compras.OrderByDescending(c => c.FechaCompra).ToList();
            }

            // Listas de filtros
            ViewBag.RutasLista = new SelectList(
                db.Ruta.ToList().Select(r => new { r.RutaId, Texto = r.Aeropuerto.CodigoIATA + " → " + r.Aeropuerto1.CodigoIATA }),
                "RutaId", "Texto", rutaId);
            ViewBag.AeronavesLista = new SelectList(db.Aeronave.OrderBy(a => a.Matricula).ToList(), "AeronaveId", "Matricula", aeronaveId);

            ViewBag.Consulta = consulta;
            ViewBag.Desde = fDesde.ToString("yyyy-MM-dd");
            ViewBag.Hasta = fHasta.ToString("yyyy-MM-dd");
            ViewBag.RutaId = rutaId;
            ViewBag.AeronaveId = aeronaveId;
            ViewBag.Estado = estado;
            ViewBag.FechaCorte = DateTime.Now;

            return View();
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