using Con_Autenticacion.Models;
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

        private List<Vuelo> ObtenerTramos(Vuelo vuelo)
        {
            if (vuelo.Frecuencia.DiaSemana != 7)
            {
                return new List<Vuelo> { vuelo };
            }

            string origen = vuelo.Frecuencia.Ruta.Aeropuerto.CodigoIATA;
            string destino = vuelo.Frecuencia.Ruta.Aeropuerto1.CodigoIATA;
            bool esIda = origen == "UIO" || destino == "GPS";
            string[] rutasItinerario = esIda
                ? new[] { "UIO-GYE", "GYE-GPS" }
                : new[] { "GPS-GYE", "GYE-UIO" };

            DateTime fecha = vuelo.FechaOperacion;
            var tramos = db.Vuelo
                .Where(v => v.FechaOperacion == fecha)
                .ToList()
                .Where(v => rutasItinerario.Contains(v.Frecuencia.Ruta.Aeropuerto.CodigoIATA + "-" + v.Frecuencia.Ruta.Aeropuerto1.CodigoIATA))
                .OrderBy(v => v.Frecuencia.HoraSalida)
                .ToList();

            return tramos.Count == 2 ? tramos : null;
        }

        private int LibresEnVuelo(Vuelo v)
        {
            return 7 - v.ReservaPasajeroVuelo.Count(r => r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA");
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

            var tramos = ObtenerTramos(vuelo);
            if (tramos == null)
            {
                TempData["Error"] = "El itinerario de este vuelo no está completo; no puede reservarse.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Cada tramo debe estar operativo y con aeronave (RNP-01)
            foreach (var t in tramos)
            {
                if (t.EstadoVueloCodigo == "CANCELADO")
                {
                    TempData["Error"] = "El vuelo seleccionado no está disponible o ha sido cancelado.";
                    return RedirectToAction("Buscar", "Vuelos");
                }
                if (t.AeronaveId == null)
                {
                    TempData["Error"] = "El vuelo no tiene aeronave asignada; sus asientos no pueden reservarse.";
                    return RedirectToAction("Buscar", "Vuelos");
                }
            }

            int cantidadPasajeros = (pasajeros >= 1 && pasajeros <= 7) ? pasajeros : 1;

            // Cupo suficiente en TODOS los tramos (mínimo entre tramos, DP-09)
            int disponibles = tramos.Min(t => LibresEnVuelo(t));
            if (disponibles < cantidadPasajeros)
            {
                TempData["Error"] = "No quedan suficientes asientos disponibles para la cantidad de pasajeros requerida.";
                return RedirectToAction("Resultados", "Vuelos", new
                {
                    origenId = tramos.First().Frecuencia.Ruta.AeropuertoOrigenId,
                    destinoId = tramos.Last().Frecuencia.Ruta.AeropuertoDestinoId,
                    fecha = vuelo.FechaOperacion.ToString("yyyy-MM-dd"),
                    pasajeros = cantidadPasajeros
                });
            }

            ViewBag.Pasajeros = cantidadPasajeros;
            ViewBag.HoraCorte = DateTime.Now.ToString("HH:mm");
            ViewBag.Tramos = tramos;

            // El modelo es siempre el primer tramo
            return View(tramos.First());
        }

        // POST: /Reservas/SeleccionAsiento
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult SeleccionAsiento(int vueloId, int pasajeros, List<int> asientosSeleccionados, List<int> asientosTramo2)
        {
            var vuelo = db.Vuelo.Find(vueloId);
            var tramos = vuelo != null ? ObtenerTramos(vuelo) : null;
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
            ViewBag.CodigosPorPasajero = codigosPorPasajero;
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

            var soloLetras = new System.Text.RegularExpressions.Regex(@"^[A-Za-zÁÉÍÓÚáéíóúÑñÜü ]{1,100}$");
            var pasaporte = new System.Text.RegularExpressions.Regex(@"^[A-Za-z0-9]{5,20}$");
            var telefono = new System.Text.RegularExpressions.Regex(@"^[0-9 +]{1,20}$");
            var validadorCorreo = new System.ComponentModel.DataAnnotations.EmailAddressAttribute();

            // 1. Validaciones del wireframe (pantalla 9, puntos 9 a 13), repetidas en el servidor
            for (int i = 0; i < pasajeros.Count; i++)
            {
                var p = pasajeros[i];
                string n = "Pasajero " + (i + 1) + ": ";

                p.NumeroDocumento = (p.NumeroDocumento ?? "").Trim().ToUpper();
                p.Nombres = (p.Nombres ?? "").Trim();
                p.Apellidos = (p.Apellidos ?? "").Trim();
                p.Correo = string.IsNullOrWhiteSpace(p.Correo) ? null : p.Correo.Trim();
                p.Telefono = string.IsNullOrWhiteSpace(p.Telefono) ? null : p.Telefono.Trim();

                if (p.TipoDocumentoCodigo != "CEDULA" && p.TipoDocumentoCodigo != "PASAPORTE")
                    return VolverAPasajeros(pasajeros, n + "seleccione el tipo de documento.");

                if (p.TipoDocumentoCodigo == "CEDULA" && (p.NumeroDocumento.Length != 10 || !p.NumeroDocumento.All(char.IsDigit)))
                    return VolverAPasajeros(pasajeros, n + "la cédula debe tener 10 dígitos.");

                if (p.TipoDocumentoCodigo == "PASAPORTE" && !pasaporte.IsMatch(p.NumeroDocumento))
                    return VolverAPasajeros(pasajeros, n + "el pasaporte debe tener entre 5 y 20 letras o números.");

                if (!soloLetras.IsMatch(p.Nombres))
                    return VolverAPasajeros(pasajeros, n + "los nombres son obligatorios y solo admiten letras y espacios.");

                if (!soloLetras.IsMatch(p.Apellidos))
                    return VolverAPasajeros(pasajeros, n + "los apellidos son obligatorios y solo admiten letras y espacios.");

                if (p.FechaNacimiento.HasValue && (p.FechaNacimiento.Value.Date <= new DateTime(1900, 1, 1) || p.FechaNacimiento.Value.Date > DateTime.Today))
                    return VolverAPasajeros(pasajeros, n + "la fecha de nacimiento debe ser posterior a 01/01/1900 y no futura.");

                if (p.Correo != null && (p.Correo.Length > 256 || !validadorCorreo.IsValid(p.Correo)))
                    return VolverAPasajeros(pasajeros, n + "el correo no tiene un formato válido.");

                if (p.Telefono != null && !telefono.IsMatch(p.Telefono))
                    return VolverAPasajeros(pasajeros, n + "el teléfono solo admite dígitos, espacios y «+» (máximo 20).");
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

        // Tarifa de ejemplo de un tramo (DP-04): Ruta.TarifaReferencial
        private decimal TarifaTramo(Vuelo v)
        {
            return v.Frecuencia.Ruta.TarifaReferencial > 0 ? v.Frecuencia.Ruta.TarifaReferencial : 150.00m;
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

            decimal importePorPasajero = tramos.Sum(t => TarifaTramo(t));

            ViewBag.Tramos = tramos;
            ViewBag.Pasajeros = pasajeros;
            ViewBag.CodigosPorPasajero = codigos;
            ViewBag.TarifasTramo = tramos.Select(t => TarifaTramo(t)).ToList();
            ViewBag.ImportePorPasajero = importePorPasajero;
            ViewBag.TotalPagar = importePorPasajero * pasajeros.Count;
            ViewBag.RetencionMin = LeerParametro("RetencionReservaMin", 120);
            ViewBag.EsCounter = User.IsInRole("Counter");
            ViewBag.CorreoCliente = TempData["CorreoCliente"] as string ?? "";

            return View(tramos.First());
        }

        // POST: /Reservas/Confirmar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Confirmar(bool? aceptarCondiciones, string accionBoton, string correoCliente)
        {
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

            // 1. Revalidar cada tramo en el servidor (RNP-04)
            for (int k = 0; k < tramos.Count; k++)
            {
                var t = tramos[k];
                string codVuelo = "VIP" + t.VueloId.ToString("D5");

                if ((t.EstadoVueloCodigo != "PROGRAMADO" && t.EstadoVueloCodigo != "CONFIRMADO") || t.AeronaveId == null)
                {
                    TempData["Error"] = "El vuelo " + codVuelo + " ya no admite reservas.";
                    return RedirectToAction("Buscar", "Vuelos");
                }

                int idTramo = t.VueloId;
                var activos = db.ReservaPasajeroVuelo
                    .Where(r => r.VueloId == idTramo && (r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA"))
                    .Select(r => new { r.NumeroAsiento, r.PasajeroId })
                    .ToList();

                // Asiento tomado por otra persona mientras se llenaban los datos
                if (asientosPorTramo[k].Any(n => activos.Any(a => a.NumeroAsiento == n)))
                {
                    TempData["Error"] = "Uno de los asientos elegidos en el vuelo " + codVuelo + " acaba de ser reservado. Elija otros asientos.";
                    return RedirectToAction("SeleccionAsiento", new { id = tramos[0].VueloId, pasajeros = pasajerosIds.Count });
                }

                // Pasajero que ya tiene un asiento activo en ese vuelo (UX_RPV_PasajeroActivo)
                int repetido = pasajerosIds.FirstOrDefault(pid => activos.Any(a => a.PasajeroId == pid));
                if (repetido != 0)
                {
                    var pas = db.Pasajero.Find(repetido);
                    TempData["Error"] = "El pasajero " + (pas != null ? pas.Nombres + " " + pas.Apellidos : "") + " ya tiene un asiento reservado o comprado en el vuelo " + codVuelo + ".";
                    return RedirectToAction("Confirmar");
                }
            }

            // 2. Vencimiento de la retención (DP-07 / RG-19): parámetro, con tope en el cierre del check-in
            DateTime ahora = DateTime.Now;
            DateTime expiracion = ahora.AddMinutes(LeerParametro("RetencionReservaMin", 120));
            DateTime salida = tramos[0].FechaOperacion.Add(tramos[0].Frecuencia.HoraSalida);
            DateTime cierreCheckIn = salida.AddMinutes(-LeerParametro("CheckInCierreMin", 90));
            if (cierreCheckIn < expiracion) expiracion = cierreCheckIn;

            if (expiracion <= ahora.AddMinutes(1))
            {
                TempData["Error"] = "El vuelo sale muy pronto y ya no admite reservas.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            string currentUserId = User.Identity.GetUserId();

            if (User.IsInRole("Counter"))
            {
                correoCliente = (correoCliente ?? "").Trim();
                if (correoCliente == "")
                {
                    TempData["Error"] = "Ingrese el correo del cliente al que pertenece la reserva.";
                    return RedirectToAction("Confirmar");
                }

                using (var identity = new ApplicationDbContext())
                {
                    var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(identity));
                    var cliente = userManager.FindByEmail(correoCliente);

                    // Solo cuentas con rol Cliente: el counter no reserva a nombre de personal
                    if (cliente == null || !userManager.IsInRole(cliente.Id, "Cliente"))
                    {
                        TempData["Error"] = "No existe un cliente registrado con el correo " + correoCliente
                                          + ". Si no tiene cuenta, primero debe registrarse.";
                        TempData["CorreoCliente"] = correoCliente;
                        return RedirectToAction("Confirmar");
                    }

                    currentUserId = cliente.Id;
                }
            }
            Reserva reserva;

            // 3. Reserva y asientos en UNA transacción (no quedan reservas sin asientos)
            using (var transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    reserva = new Reserva
                    {
                        UsuarioId = currentUserId,
                        FechaCreacion = ahora,
                        FechaExpiracion = expiracion,
                        EstadoReservaCodigo = "PENDIENTE"
                    };
                    db.Reserva.Add(reserva);
                    db.SaveChanges();   // genera ReservaId y CodigoReserva (columna calculada)

                    // Una asignación por pasajero y por tramo (DP-09), en morado (PENDIENTE)
                    for (int k = 0; k < tramos.Count; k++)
                    {
                        for (int i = 0; i < pasajerosIds.Count; i++)
                        {
                            db.ReservaPasajeroVuelo.Add(new ReservaPasajeroVuelo
                            {
                                ReservaId = reserva.ReservaId,
                                PasajeroId = pasajerosIds[i],
                                VueloId = tramos[k].VueloId,
                                NumeroAsiento = (byte)asientosPorTramo[k][i],
                                EstadoAsignacionCodigo = "PENDIENTE",
                                ImporteTramo = TarifaTramo(tramos[k]),
                                FechaAsignacion = ahora
                            });
                        }
                    }

                    db.SaveChanges();
                    transaccion.Commit();
                }
                catch (Exception ex)
                {
                    transaccion.Rollback();
                    Exception inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    TempData["Error"] = "No se pudo registrar la reserva: " + inner.Message;
                    return RedirectToAction("Confirmar");
                }
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

        // Revisa si la reserva se puede cancelar y con qué tratamiento.
        // Devuelve null si se puede cancelar, o el motivo por el que no se puede.
        private string EvaluarCancelacion(Reserva reserva, out bool conReembolso, out double horasRestantes)
        {
            conReembolso = false;
            horasRestantes = 0;

            int limiteHoras = 48;
            int valorConfig;
            if (int.TryParse(System.Configuration.ConfigurationManager.AppSettings["LimiteReembolsoHoras"], out valorConfig))
            {
                limiteHoras = valorConfig;
            }

            var primerVuelo = reserva.ReservaPasajeroVuelo
                .Select(a => a.Vuelo)
                .OrderBy(v => v.FechaOperacion).ThenBy(v => v.Frecuencia.HoraSalida)
                .FirstOrDefault();

            if (primerVuelo == null)
                return "La reserva no tiene tramos asociados.";

            DateTime salida = primerVuelo.FechaOperacion.Add(primerVuelo.Frecuencia.HoraSalida);
            horasRestantes = (salida - DateTime.Now).TotalHours;

            if (reserva.EstadoReservaCodigo == "CANCELADA")
                return "La reserva ya se encuentra cancelada.";

            if (reserva.EstadoReservaCodigo == "EXPIRADA" ||
                (reserva.EstadoReservaCodigo == "PENDIENTE" && reserva.FechaExpiracion.HasValue && reserva.FechaExpiracion.Value < DateTime.Now))
                return "La reserva expiró; sus asientos ya fueron liberados.";

            if (horasRestantes <= 0)
                return "El vuelo ya salió; la reserva no puede cancelarse.";

            bool tieneCheckIn = reserva.ReservaPasajeroVuelo
                .Any(a => a.CheckIn.Any(c => c.EstadoCheckInCodigo == "COMPLETADO"));
            if (tieneCheckIn)
                return "La reserva tiene check-in completado; ya no puede cancelarse.";

            if (reserva.EstadoReservaCodigo == "CONFIRMADA")
                conReembolso = horasRestantes >= limiteHoras;

            return null;
        }

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
            string motivoNoCancelable = EvaluarCancelacion(reserva, out conReembolso, out horasRestantes);
            ViewBag.PuedeCancelar = motivoNoCancelable == null;
            ViewBag.MotivoNoCancelable = motivoNoCancelable;
            ViewBag.ConReembolso = conReembolso;
            ViewBag.HorasRestantes = horasRestantes;

            return View(reserva);
        }

        // POST: /Reservas/Gestionar/5  (Confirmar solicitud de cancelación)
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

            // Se vuelve a evaluar en el servidor: el estado pudo cambiar mientras se revisaba
            bool conReembolso;
            double horasRestantes;
            string motivoNoCancelable = EvaluarCancelacion(reserva, out conReembolso, out horasRestantes);
            if (motivoNoCancelable != null)
            {
                TempData["Error"] = motivoNoCancelable;
                return RedirectToAction("Gestionar", new { id = id });
            }

            // Motivo obligatorio (10-300 caracteres) para reservas pagadas
            motivo = (motivo ?? "").Trim();
            if (reserva.EstadoReservaCodigo == "CONFIRMADA" && (motivo.Length < 10 || motivo.Length > 300))
            {
                TempData["Error"] = "Describa el motivo de la cancelación (entre 10 y 300 caracteres).";
                return RedirectToAction("Gestionar", new { id = id });
            }

            var compra = reserva.Compra.OrderByDescending(c => c.FechaCompra).FirstOrDefault();
            bool estabaPagada = compra != null && compra.EstadoCompraCodigo == "PAGADA";

            using (var transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    // 1. Liberar asientos (vuelven a verde)
                    foreach (var a in reserva.ReservaPasajeroVuelo)
                    {
                        if (a.EstadoAsignacionCodigo == "PENDIENTE" || a.EstadoAsignacionCodigo == "CONFIRMADA")
                        {
                            a.EstadoAsignacionCodigo = "CANCELADA";
                        }
                    }

                    // 2. Tratamiento de la compra y el pago simulado
                    if (compra != null)
                    {
                        if (compra.EstadoCompraCodigo == "PENDIENTE")
                        {
                            compra.EstadoCompraCodigo = "ANULADA";
                        }
                        else if (compra.EstadoCompraCodigo == "PAGADA" && conReembolso)
                        {
                            var pagoAprobado = compra.Pago.FirstOrDefault(p => p.EstadoPagoCodigo == "APROBADO");
                            if (pagoAprobado != null)
                            {
                                pagoAprobado.EstadoPagoCodigo = "REVERSADO";
                                string mensaje = "Reembolso simulado por cancelación: " + motivo;
                                pagoAprobado.MensajeResultado = mensaje.Length > 200 ? mensaje.Substring(0, 200) : mensaje;
                            }
                            compra.EstadoCompraCodigo = "ANULADA";

                            // Nota de crédito académica (DP-05)
                            int siguiente = db.Comprobante.Count(c => c.TipoComprobanteCodigo == "NOTA_CREDITO") + 1;
                            db.Comprobante.Add(new Comprobante
                            {
                                CompraId = compra.CompraId,
                                TipoComprobanteCodigo = "NOTA_CREDITO",
                                NumeroComprobante = "NC-ACAD-" + siguiente.ToString("D6"),
                                FechaEmision = DateTime.Now,
                                EstadoComprobanteCodigo = "EMITIDO"
                            });
                        }
                        // PAGADA sin reembolso (< 48 h): la compra se mantiene PAGADA como penalidad
                    }

                    // 3. Reserva cancelada
                    reserva.EstadoReservaCodigo = "CANCELADA";

                    db.SaveChanges();
                    transaccion.Commit();
                }
                catch (Exception ex)
                {
                    transaccion.Rollback();
                    Exception inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    TempData["Error"] = "No se pudo cancelar la reserva: " + inner.Message;
                    return RedirectToAction("Gestionar", new { id = id });
                }
            }

            string detalle = "";
            if (estabaPagada)
            {
                detalle = conReembolso
                    ? " Se registró un reembolso simulado total y una nota de crédito académica."
                    : " Por la cercanía del vuelo, la cancelación no genera reembolso.";
            }
            TempData["Exito"] = "Reserva " + reserva.CodigoReserva + " cancelada. Los asientos fueron liberados." + detalle;
            return RedirectToAction("Gestionar", new { id = id });
        }
        // =========================================================================
        // PANTALLA 15: CHECK-IN (/Reservas/CheckIn)
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

        // Devuelve la elegibilidad de una asignación para el check-in.
        // Retorna "ELEGIBLE" o el texto que explica por qué no es elegible.
        private string EvaluarCheckIn(ReservaPasajeroVuelo a)
        {
            int aperturaHoras = LeerParametro("CheckInAperturaHoras", 48);
            int cierreMin = LeerParametro("CheckInCierreMin", 90);

            DateTime salida = a.Vuelo.FechaOperacion.Add(a.Vuelo.Frecuencia.HoraSalida);
            DateTime apertura = salida.AddHours(-aperturaHoras);
            DateTime cierre = salida.AddMinutes(-cierreMin);
            DateTime ahora = DateTime.Now;

            var checkInVigente = a.CheckIn.FirstOrDefault(c => c.EstadoCheckInCodigo == "COMPLETADO");
            if (checkInVigente != null)
                return "Registrado el " + checkInVigente.FechaRegistro.ToString("dd/MM/yyyy HH:mm");

            if (a.Vuelo.EstadoVueloCodigo == "CANCELADO")
                return "Vuelo cancelado";

            if (a.EstadoAsignacionCodigo != "CONFIRMADA")
                return "Requiere compra pagada";

            if (ahora < apertura)
                return "Aún no habilitado (abre el " + apertura.ToString("dd/MM/yyyy HH:mm") + ")";

            if (ahora > cierre)
                return "Check-in cerrado";

            return "ELEGIBLE";
        }

        // Usa PuedeGestionar(...) de la pantalla 14: titular o Counter / Administrador / SuperUsuario.

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

            // Validación de formato en el servidor
            if (!System.Text.RegularExpressions.Regex.IsMatch(codigo, @"^RV\d{8}$"))
            {
                ViewBag.ErrorBusqueda = "El código de reserva debe tener el formato RV seguido de 8 dígitos.";
                return View((Reserva)null);
            }
            if (documento.Length < 5 || documento.Length > 20)
            {
                ViewBag.ErrorBusqueda = "El número de documento debe tener entre 5 y 20 caracteres.";
                return View((Reserva)null);
            }

            // RG-37: el código y el documento deben corresponder a la misma reserva
            var reserva = db.Reserva.FirstOrDefault(r => r.CodigoReserva == codigo);
            bool documentoCoincide = reserva != null &&
                reserva.ReservaPasajeroVuelo.Any(a => a.Pasajero.NumeroDocumento.ToUpper() == documento);

            if (reserva == null || !documentoCoincide || !PuedeGestionar(reserva))
            {
                // Mensaje neutral: no se revela si la reserva existe
                ViewBag.ErrorBusqueda = "No se encontró una reserva con esos datos.";
                return View((Reserva)null);
            }

            // Elegibilidad y código de asiento por asignación
            var elegibilidad = new Dictionary<int, string>();
            var codigosAsiento = new Dictionary<int, string>();
            var asientos = db.Asiento.ToList();

            foreach (var a in reserva.ReservaPasajeroVuelo)
            {
                elegibilidad[a.AsignacionId] = EvaluarCheckIn(a);

                int modeloId = a.Vuelo.Aeronave != null ? a.Vuelo.Aeronave.ModeloAeronaveId : 0;
                var asiento = asientos.FirstOrDefault(s => s.NumeroAsiento == a.NumeroAsiento && (modeloId == 0 || s.ModeloAeronaveId == modeloId));
                codigosAsiento[a.AsignacionId] = asiento != null ? asiento.Codigo : a.NumeroAsiento.ToString();
            }

            ViewBag.Elegibilidad = elegibilidad;
            ViewBag.CodigosAsiento = codigosAsiento;
            return View(reserva);
        }

        // POST: /Reservas/CheckIn  (Confirmar check-in de las asignaciones marcadas)
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult CheckIn(string codigo, string documento, List<int> asignaciones)
        {
            codigo = (codigo ?? "").Trim().ToUpper();
            documento = (documento ?? "").Trim().ToUpper();

            var reserva = db.Reserva.FirstOrDefault(r => r.CodigoReserva == codigo);
            bool documentoCoincide = reserva != null &&
                reserva.ReservaPasajeroVuelo.Any(a => a.Pasajero.NumeroDocumento.ToUpper() == documento);

            if (reserva == null || !documentoCoincide || !PuedeGestionar(reserva))
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

            foreach (int asignacionId in asignaciones.Distinct())
            {
                // Solo asignaciones que pertenecen a esta reserva
                var a = reserva.ReservaPasajeroVuelo.FirstOrDefault(x => x.AsignacionId == asignacionId);
                if (a == null) continue;

                string nombre = a.Pasajero.Nombres + " " + a.Pasajero.Apellidos + " en VIP" + a.VueloId.ToString("D5");

                // Se vuelve a validar en el servidor (la ventana pudo cerrarse)
                if (EvaluarCheckIn(a) != "ELEGIBLE")
                {
                    rechazados.Add(nombre);
                    continue;
                }

                db.CheckIn.Add(new Con_Autenticacion.Models.CheckIn   // nombre completo: la acción también se llama CheckIn
                {
                    AsignacionId = a.AsignacionId,
                    FechaRegistro = DateTime.Now,
                    EstadoCheckInCodigo = "COMPLETADO"
                });
                registrados.Add(nombre);
            }

            try
            {
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                Exception inner = ex;
                while (inner.InnerException != null) inner = inner.InnerException;
                TempData["Error"] = "No se pudo registrar el check-in: " + inner.Message;
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