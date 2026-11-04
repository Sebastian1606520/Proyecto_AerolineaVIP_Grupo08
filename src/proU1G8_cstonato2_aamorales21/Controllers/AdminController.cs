using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using Con_Autenticacion.Models;
using Con_Autenticacion.Negocio;
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
                ViewBag.Conflictos = new VueloServicio(db).BuscarConflictos(todos);
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
        // Las reglas operativas están en Negocio/VueloServicio.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarVuelo(int vueloId, int? aeronaveId, string estadoVueloCodigo, string desde, string hasta)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            string mensajeExito;
            bool volverAEdicion;
            string error = new VueloServicio(db).ActualizarVuelo(vueloId, aeronaveId, estadoVueloCodigo,
                                                                 out mensajeExito, out volverAEdicion);
            if (error != null)
            {
                TempData["Error"] = error;
                return volverAEdicion
                    ? RedirectToAction("Vuelos", new { desde = desde, hasta = hasta, editar = vueloId })
                    : RedirectToAction("Vuelos", new { desde = desde, hasta = hasta });
            }

            TempData["Exito"] = mensajeExito;
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

            string mensajeExito;
            string error = new VueloServicio(db).GenerarVuelos(desde, hasta, out mensajeExito);
            if (error != null)
                TempData["Error"] = error;
            else
                TempData["Exito"] = mensajeExito;

            if (!desde.HasValue || !hasta.HasValue)
                return RedirectToAction("Vuelos");

            return RedirectToAction("Vuelos", new { desde = desde.Value.ToString("yyyy-MM-dd"), hasta = hasta.Value.ToString("yyyy-MM-dd") });
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
                    .OrderBy(v => VueloServicio.Salida(v))
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
        // Las validaciones están en Negocio/AeronaveServicio.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarAeronave(int aeronaveId, string matricula, int modeloAeronaveId, string estadoAeronaveCodigo)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            string mensajeExito;
            bool noExiste;
            string error = new AeronaveServicio(db).EditarAeronave(aeronaveId, matricula, modeloAeronaveId, estadoAeronaveCodigo,
                                                                   out mensajeExito, out noExiste);
            if (error != null)
            {
                TempData["Error"] = error;
                return noExiste
                    ? RedirectToAction("Aeronaves")
                    : RedirectToAction("Aeronaves", new { id = aeronaveId, editar = true });
            }

            TempData["Exito"] = mensajeExito;
            return RedirectToAction("Aeronaves", new { id = aeronaveId });
        }

        // =========================================================================
        // PANTALLA 20: GESTIÓN DE USUARIOS, PASAJEROS Y RESERVAS (/Admin/Gestion)
        // =========================================================================

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
                        string principal = UsuarioServicio.RolesPrincipales.FirstOrDefault(rp => nombres.Contains(rp)) ?? "Sin rol";
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
        // Las reglas están en Negocio/UsuarioServicio.cs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EditarUsuario(string userId, string email)
        {
            if (!EsAdministrativo())
            {
                TempData["Error"] = "No tiene permisos para acceder a esta sección.";
                return RedirectToAction("Index");
            }

            string mensajeExito;
            bool volverAEdicion;
            string error = new UsuarioServicio().EditarCorreo(userId, email, User.IsInRole("SuperUsuario"),
                                                              out mensajeExito, out volverAEdicion);
            if (error != null)
            {
                TempData["Error"] = error;
                return volverAEdicion
                    ? RedirectToAction("Gestion", new { tab = "usuarios", editarUsuario = userId })
                    : RedirectToAction("Gestion", new { tab = "usuarios" });
            }

            TempData["Exito"] = mensajeExito;
            return RedirectToAction("Gestion", new { tab = "usuarios" });
        }

        // POST: /Admin/AsignarRol  (solo SuperUsuario)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult AsignarRol(string userId, string rol)
        {
            string mensajeExito;
            bool volverAEdicion;
            string error = new UsuarioServicio().AsignarRol(userId, rol, User.Identity.GetUserId(), User.IsInRole("SuperUsuario"),
                                                            out mensajeExito, out volverAEdicion);
            if (error != null)
            {
                TempData["Error"] = error;
                return volverAEdicion
                    ? RedirectToAction("Gestion", new { tab = "usuarios", editarUsuario = userId })
                    : RedirectToAction("Gestion", new { tab = "usuarios" });
            }

            TempData["Exito"] = mensajeExito;
            return RedirectToAction("Gestion", new { tab = "usuarios" });
        }

        // POST: /Admin/GuardarPasajero  (crear o editar)
        // Las validaciones están en Negocio/PasajeroServicio.cs
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

            string documento;
            string error = new PasajeroServicio(db).GuardarPasajero(pasajeroId, tipoDocumentoCodigo, numeroDocumento, nombres,
                                                                    apellidos, fechaNacimiento, correo, telefono, out documento);
            if (error != null)
            {
                TempData["Error"] = error;
                return pasajeroId.HasValue
                    ? RedirectToAction("Gestion", new { tab = "pasajeros", editarPasajero = pasajeroId })
                    : RedirectToAction("Gestion", new { tab = "pasajeros", nuevoPasajero = true });
            }

            TempData["Exito"] = "Cambios guardados.";
            return RedirectToAction("Gestion", new { tab = "pasajeros", buscar = documento });
        }

        // POST: /Admin/EliminarPasajero  (solo quien tiene el permiso Delete)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult EliminarPasajero(int pasajeroId)
        {
            if (!User.IsInRole("Delete"))
            {
                TempData["Error"] = "No tiene permisos para modificar pasajeros.";
                return RedirectToAction("Gestion", new { tab = "pasajeros" });
            }

            string mensajeExito;
            string error = new PasajeroServicio(db).EliminarPasajero(pasajeroId, out mensajeExito);
            if (error != null)
                TempData["Error"] = error;
            else
                TempData["Exito"] = mensajeExito;

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
            estado = estado ?? "";

            // Las consultas están en Negocio/ReporteServicio.cs
            var servicio = new ReporteServicio(db);

            DateTime fDesde, fHasta;
            ViewBag.ErrorFiltro = servicio.NormalizarRango(desde, hasta, out fDesde, out fHasta);

            if (consulta == "ocupacion")
            {
                Dictionary<int, int> reservados, ocupados;
                ViewBag.Vuelos = servicio.OcupacionPorVuelo(fDesde, fHasta, rutaId, aeronaveId, estado, out reservados, out ocupados);
                ViewBag.Reservados = reservados;
                ViewBag.OcupadosConfirmados = ocupados;
            }

            if (consulta == "reservas")
            {
                List<object[]> resumen;
                ViewBag.Reservas = servicio.ReservasPorEstado(fDesde, fHasta, rutaId, aeronaveId, estado, out resumen);
                ViewBag.ResumenReservas = resumen;
            }

            if (consulta == "pagos")
            {
                ViewBag.Compras = servicio.ComprasYPagos(fDesde, fHasta, rutaId, aeronaveId, estado);
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