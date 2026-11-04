using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;
using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web.Mvc;

namespace Con_Autenticacion.Controllers
{
    [Authorize]
    public class ComprasController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        public ActionResult Index()
        {
            var compra = db.Compra.Include(c => c.EstadoCompra).Include(c => c.Moneda).Include(c => c.Reserva);
            return View(compra.ToList());
        }

        private static readonly Dictionary<int, string> CodigosAsiento = new Dictionary<int, string>
        {
            { 1, "1D" }, { 2, "2D" }, { 3, "2I" }, { 4, "3D" }, { 5, "3I" }, { 6, "4D" }, { 7, "4I" }
        };

        private bool PuedePagar(Reserva r)
        {
            return r.UsuarioId == User.Identity.GetUserId() || User.IsInRole("Counter");
        }

        private bool PuedeVer(Reserva r)
        {
            return PuedePagar(r) || User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
        }

        // VALIDACIÓN CENTRAL DE RETENCIÓN Y EXPIRACIÓN
        private string MotivoNoPagable(Reserva r)
        {
            if (r.EstadoReservaCodigo == "CONFIRMADA")
                return "Esta reserva ya fue pagada.";

            if (r.EstadoReservaCodigo != "PENDIENTE")
                return "Esta reserva no se encuentra pendiente de pago.";

            // Si venció la ventana de 2 horas (o tope de vuelo), se cancela formalmente en BD
            if (r.FechaExpiracion.HasValue && r.FechaExpiracion.Value < DateTime.Now)
            {
                r.EstadoReservaCodigo = "EXPIRADA";
                foreach (var asig in r.ReservaPasajeroVuelo)
                {
                    if (asig.EstadoAsignacionCodigo == "PENDIENTE")
                    {
                        asig.EstadoAsignacionCodigo = "CANCELADA";
                    }
                }
                db.SaveChanges(); // Libera los cupos en SQL Server

                return "El plazo de retención de 2 horas para pagar esta reserva ha expirado. Los asientos fueron liberados.";
            }

            if (!r.ReservaPasajeroVuelo.Any())
                return "La reserva no tiene pasajeros asignados.";

            return null;
        }

        private decimal TotalReserva(Reserva r)
        {
            return r.ReservaPasajeroVuelo.Sum(a => (decimal?)a.ImporteTramo) ?? 0m;
        }

        private void CargarResumen(Reserva r)
        {
            var tramos = r.ReservaPasajeroVuelo
                .Select(a => a.Vuelo)
                .Distinct()
                .OrderBy(v => v.FechaOperacion).ThenBy(v => v.Frecuencia.HoraSalida)
                .ToList();

            if (tramos.Any())
            {
                var primero = tramos.First();
                var ultimo = tramos.Last();

                // Orientación correcta: Aeropuerto1 es Origen y Aeropuerto es Destino
                ViewBag.CodigoRuta = primero.Frecuencia.Ruta.Aeropuerto1.CodigoIATA + " → " + ultimo.Frecuencia.Ruta.Aeropuerto.CodigoIATA;
                ViewBag.RutaStr = primero.Frecuencia.Ruta.Aeropuerto1.Localidad + " (" + primero.Frecuencia.Ruta.Aeropuerto1.CodigoIATA + ") → "
                                + ultimo.Frecuencia.Ruta.Aeropuerto.Localidad + " (" + ultimo.Frecuencia.Ruta.Aeropuerto.CodigoIATA + ")";
                ViewBag.FechaVuelo = primero.FechaOperacion.ToString("dd/MM/yyyy");
                ViewBag.HorarioStr = primero.Frecuencia.HoraSalida.ToString(@"hh\:mm") + " – " + ultimo.Frecuencia.HoraLlegada.ToString(@"hh\:mm")
                                   + (tramos.Count > 1 ? " (" + tramos.Count + " tramos, escala en GYE)" : "");
            }
            else
            {
                ViewBag.CodigoRuta = "—";
                ViewBag.RutaStr = "—";
                ViewBag.FechaVuelo = "—";
                ViewBag.HorarioStr = "—";
            }

            ViewBag.TramosIds = tramos.Select(t => t.VueloId).ToList();
            ViewBag.NumTramos = tramos.Count;
            ViewBag.NumPasajeros = r.ReservaPasajeroVuelo.Select(a => a.PasajeroId).Distinct().Count();
            ViewBag.TotalPagar = TotalReserva(r);
        }

        private ActionResult VolverAPagar(int reservaId, string mensaje)
        {
            TempData["Error"] = mensaje;
            return RedirectToAction("Pagar", new { id = reservaId });
        }

        // GET: /Compras/Pagar/{id}
        [HttpGet]
        public ActionResult Pagar(int? id)
        {
            var reserva = id.HasValue ? db.Reserva.Find(id.Value) : null;
            if (reserva == null || !PuedePagar(reserva))
            {
                TempData["Error"] = "No se encontró la reserva solicitada.";
                return RedirectToAction("MisReservas", "Reservas");
            }

            string motivo = MotivoNoPagable(reserva);
            if (motivo != null)
            {
                TempData["Error"] = motivo;
                return reserva.EstadoReservaCodigo == "CONFIRMADA"
                    ? RedirectToAction("Confirmacion", new { id = reserva.ReservaId })
                    : RedirectToAction("MisReservas", "Reservas");
            }

            CargarResumen(reserva);
            return View(reserva);
        }

        // POST: /Compras/Pagar
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Pagar(int reservaId, string metodoPago, string marcaTarjeta, string numeroTarjeta,
                                  string titularTarjeta, string vencimiento, string codigoSeguridad)
        {
            var reserva = db.Reserva.Find(reservaId);
            if (reserva == null || !PuedePagar(reserva))
            {
                TempData["Error"] = "No se encontró la reserva solicitada.";
                return RedirectToAction("MisReservas", "Reservas");
            }

            // Validar plazo de expiración antes de procesar el cobro
            string motivo = MotivoNoPagable(reserva);
            if (motivo != null)
            {
                TempData["Error"] = motivo;
                return reserva.EstadoReservaCodigo == "CONFIRMADA"
                    ? RedirectToAction("Confirmacion", new { id = reservaId })
                    : RedirectToAction("MisReservas", "Reservas");
            }

            var metodo = db.MetodoPago.Find(metodoPago ?? "");
            if (metodo == null || !metodo.Activo)
                return VolverAPagar(reservaId, "Seleccione el método de pago.");

            var marca = db.MarcaTarjeta.Find(marcaTarjeta ?? "");
            if (marca == null || !marca.Activa)
                return VolverAPagar(reservaId, "Seleccione la marca de la tarjeta.");

            string numLimpio = (numeroTarjeta ?? "").Replace(" ", "").Trim();
            if (numLimpio.Length != marca.LongitudNumero || !numLimpio.All(char.IsDigit))
                return VolverAPagar(reservaId, "El número de tarjeta debe tener " + marca.LongitudNumero + " dígitos para " + marca.Nombre + ".");

            string titular = (titularTarjeta ?? "").Trim();
            if (!System.Text.RegularExpressions.Regex.IsMatch(titular, @"^[A-Za-zÁÉÍÓÚáéíóúÑñÜü ]{2,100}$"))
                return VolverAPagar(reservaId, "Ingrese el nombre del titular (solo letras y espacios).");

            var mVence = System.Text.RegularExpressions.Regex.Match((vencimiento ?? "").Trim(), @"^(0[1-9]|1[0-2])/(\d{2})$");
            if (!mVence.Success)
                return VolverAPagar(reservaId, "El vencimiento debe tener el formato MM/AA.");

            var primerDiaMesSiguiente = new DateTime(2000 + int.Parse(mVence.Groups[2].Value), int.Parse(mVence.Groups[1].Value), 1).AddMonths(1);
            if (primerDiaMesSiguiente <= DateTime.Today)
                return VolverAPagar(reservaId, "La tarjeta está vencida.");

            if (!System.Text.RegularExpressions.Regex.IsMatch((codigoSeguridad ?? "").Trim(), @"^\d{" + marca.LongitudCodigoSeguridad + "}$"))
                return VolverAPagar(reservaId, "El código de seguridad debe tener " + marca.LongitudCodigoSeguridad + " dígitos.");

            // Rechazo simulado al terminar en 0000
            if (numLimpio.EndsWith("0000"))
                return VolverAPagar(reservaId, "Pago simulado rechazado. La reserva sigue pendiente; puede intentar con otra tarjeta.");

            decimal total = TotalReserva(reserva);
            string ultimos4 = numLimpio.Substring(numLimpio.Length - 4);
            string opSimulada = "SIM-" + (DateTime.Now.Ticks % 100000000).ToString("D8");

            using (var transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    var compra = reserva.Compra.FirstOrDefault(c => c.EstadoCompraCodigo == "PENDIENTE");
                    if (compra == null)
                    {
                        compra = new Compra
                        {
                            ReservaId = reserva.ReservaId,
                            FechaCompra = DateTime.Now,
                            Importe = total,
                            MonedaCodigo = "USD",
                            EstadoCompraCodigo = "PAGADA",
                            ClaveIdempotencia = Guid.NewGuid()
                        };
                        db.Compra.Add(compra);
                    }
                    else
                    {
                        compra.Importe = total;
                        compra.MonedaCodigo = "USD";
                        compra.EstadoCompraCodigo = "PAGADA";
                    }
                    db.SaveChanges();

                    db.Pago.Add(new Pago
                    {
                        CompraId = compra.CompraId,
                        MetodoPagoCodigo = metodo.Codigo,
                        MarcaTarjetaCodigo = marca.Codigo,
                        UltimosCuatroDigitos = ultimos4,
                        NombreTitular = titular.ToUpper(),
                        CodigoOperacion = opSimulada,
                        Importe = total,
                        FechaPago = DateTime.Now,
                        EsSimulado = true,
                        EstadoPagoCodigo = "APROBADO",
                        MensajeResultado = "Pago simulado aprobado."
                    });

                    // Confirmar reserva y asientos
                    reserva.EstadoReservaCodigo = "CONFIRMADA";
                    foreach (var rp in reserva.ReservaPasajeroVuelo.Where(a => a.EstadoAsignacionCodigo == "PENDIENTE"))
                    {
                        rp.EstadoAsignacionCodigo = "CONFIRMADA";
                    }

                    int numConfirmacion = db.Comprobante.Count(c => c.TipoComprobanteCodigo == "CONFIRMACION") + 1;
                    int numVenta = db.Comprobante.Count(c => c.TipoComprobanteCodigo == "FACTURA") + 1;

                    db.Comprobante.Add(new Comprobante
                    {
                        CompraId = compra.CompraId,
                        TipoComprobanteCodigo = "CONFIRMACION",
                        NumeroComprobante = "CNF-" + numConfirmacion.ToString("D6"),
                        FechaEmision = DateTime.Now,
                        EstadoComprobanteCodigo = "EMITIDO"
                    });
                    db.Comprobante.Add(new Comprobante
                    {
                        CompraId = compra.CompraId,
                        TipoComprobanteCodigo = "FACTURA",
                        NumeroComprobante = "ACAD-" + numVenta.ToString("D6"),
                        FechaEmision = DateTime.Now,
                        EstadoComprobanteCodigo = "EMITIDO"
                    });

                    db.SaveChanges();
                    transaccion.Commit();
                }
                catch (Exception ex)
                {
                    transaccion.Rollback();
                    Exception inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    return VolverAPagar(reservaId, "No se pudo procesar el pago: " + inner.Message);
                }
            }

            return RedirectToAction("Confirmacion", new { id = reservaId });
        }

        // GET: /Compras/Confirmacion/{id}
        [HttpGet]
        public ActionResult Confirmacion(int? id)
        {
            var reserva = id.HasValue ? db.Reserva.Find(id.Value) : null;
            if (reserva == null || !PuedeVer(reserva))
            {
                TempData["Error"] = "No se encontró la operación solicitada.";
                return RedirectToAction("MisReservas", "Reservas");
            }

            var compra = reserva.Compra.OrderByDescending(c => c.FechaCompra).FirstOrDefault();
            Pago ultimoPago = compra != null ? compra.Pago.OrderByDescending(p => p.FechaPago).FirstOrDefault() : null;

            bool vencida = reserva.EstadoReservaCodigo == "PENDIENTE" && reserva.FechaExpiracion.HasValue && reserva.FechaExpiracion.Value < DateTime.Now;
            bool esPagada = compra != null && compra.EstadoCompraCodigo == "PAGADA" && ultimoPago != null && ultimoPago.EstadoPagoCodigo == "APROBADO";
            bool esRechazada = !esPagada && ultimoPago != null && ultimoPago.EstadoPagoCodigo == "RECHAZADO";

            ViewBag.EsPagada = esPagada;
            ViewBag.EsRechazada = esRechazada;
            ViewBag.EsSoloReserva = !esPagada && !esRechazada && reserva.EstadoReservaCodigo == "PENDIENTE" && !vencida;
            ViewBag.PuedePagar = MotivoNoPagable(reserva) == null && PuedePagar(reserva);

            ViewBag.EstadoReservaTexto = vencida ? "EXPIRADA" : reserva.EstadoReservaCodigo;
            ViewBag.EstadoCompraTexto = compra != null ? compra.EstadoCompraCodigo : "SIN COMPRA";
            ViewBag.EstadoPagoTexto = ultimoPago != null ? ultimoPago.EstadoPagoCodigo : "SIN PAGO";

            ViewBag.TienePago = ultimoPago != null;
            ViewBag.CodigoOperacion = ultimoPago != null ? ultimoPago.CodigoOperacion : "";
            ViewBag.MetodoPagoTexto = ultimoPago != null ? (ultimoPago.MetodoPagoCodigo == "TARJETA_DEBITO" ? "Tarjeta de débito" : "Tarjeta de crédito") : "";
            ViewBag.TarjetaTexto = ultimoPago != null ? ultimoPago.MarcaTarjetaCodigo + " **** " + ultimoPago.UltimosCuatroDigitos : "";

            var comprobante = compra != null
                ? compra.Comprobante.Where(c => c.TipoComprobanteCodigo == "FACTURA").OrderByDescending(c => c.FechaEmision).FirstOrDefault()
                : null;
            ViewBag.ComprobanteId = comprobante != null ? (int?)comprobante.ComprobanteId : null;

            var detalle = reserva.ReservaPasajeroVuelo
                .OrderBy(a => a.Vuelo.FechaOperacion).ThenBy(a => a.Vuelo.Frecuencia.HoraSalida)
                .ThenBy(a => a.Pasajero.Apellidos)
                .ToList();
            ViewBag.Detalle = detalle;
            ViewBag.CodigosAsiento = detalle.ToDictionary(
                a => a.AsignacionId,
                a => CodigosAsiento.ContainsKey(a.NumeroAsiento) ? CodigosAsiento[a.NumeroAsiento] : a.NumeroAsiento.ToString());

            CargarResumen(reserva);
            return View(reserva);
        }

        // GET: /Compras/Comprobantes
        [HttpGet]
        public ActionResult Comprobantes(DateTime? desde, DateTime? hasta, string tipo, int? ver)
        {
            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;
            bool esPersonal = User.IsInRole("Counter") || User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");

            if (desde.HasValue && hasta.HasValue && hasta.Value.Date < desde.Value.Date)
            {
                ViewBag.ErrorFiltro = "La fecha «Hasta» no puede ser anterior a la fecha «Desde».";
                hasta = null;
            }

            var tiposValidos = new List<string> { "CONFIRMACION", "FACTURA", "NOTA_CREDITO" };
            if (!string.IsNullOrEmpty(tipo) && !tiposValidos.Contains(tipo))
            {
                tipo = null;
            }

            var consulta = db.Comprobante
                .Include(c => c.Compra.Reserva)
                .Include(c => c.TipoComprobante)
                .AsQueryable();

            if (!esPersonal)
            {
                consulta = consulta.Where(c => c.Compra.Reserva.UsuarioId == currentUserId);
            }

            if (desde.HasValue)
            {
                DateTime d = desde.Value.Date;
                consulta = consulta.Where(c => c.FechaEmision >= d);
            }
            if (hasta.HasValue)
            {
                DateTime h = hasta.Value.Date.AddDays(1);
                consulta = consulta.Where(c => c.FechaEmision < h);
            }
            if (!string.IsNullOrEmpty(tipo))
            {
                consulta = consulta.Where(c => c.TipoComprobanteCodigo == tipo);
            }

            var comprobantes = consulta
                .OrderByDescending(c => c.FechaEmision)
                .ThenBy(c => c.TipoComprobanteCodigo)
                .ToList();

            ViewBag.Desde = desde.HasValue ? desde.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Hasta = hasta.HasValue ? hasta.Value.ToString("yyyy-MM-dd") : "";
            ViewBag.Tipo = tipo ?? "";
            ViewBag.HayFiltros = desde.HasValue || hasta.HasValue || !string.IsNullOrEmpty(tipo);

            Comprobante seleccionado = null;
            if (ver.HasValue)
            {
                seleccionado = comprobantes.FirstOrDefault(c => c.ComprobanteId == ver.Value);
                if (seleccionado == null)
                {
                    seleccionado = db.Comprobante.Find(ver.Value);
                    if (seleccionado != null && !esPersonal && seleccionado.Compra.Reserva.UsuarioId != currentUserId)
                    {
                        seleccionado = null;
                    }
                }
                if (seleccionado == null)
                {
                    ViewBag.ErrorFiltro = "No se encontró el comprobante solicitado.";
                }
            }

            ViewBag.Seleccionado = seleccionado;

            if (seleccionado != null)
            {
                var compra = seleccionado.Compra;
                var reserva = compra.Reserva;

                ViewBag.Emisor = db.DatosEmisor.FirstOrDefault();

                ViewBag.Pago = compra.Pago
                    .Where(p => p.EstadoPagoCodigo == "APROBADO" || p.EstadoPagoCodigo == "REVERSADO")
                    .OrderByDescending(p => p.FechaPago)
                    .FirstOrDefault();

                using (var identity = new ApplicationDbContext())
                {
                    var usuario = identity.Users.FirstOrDefault(u => u.Id == reserva.UsuarioId);
                    ViewBag.ClienteNombre = usuario != null ? usuario.UserName : "Usuario de la reserva";
                    ViewBag.ClienteCorreo = usuario != null ? usuario.Email : "";
                }

                var detalle = reserva.ReservaPasajeroVuelo
                    .OrderBy(a => a.Vuelo.FechaOperacion).ThenBy(a => a.Vuelo.Frecuencia.HoraSalida)
                    .ThenBy(a => a.Pasajero.Apellidos)
                    .ToList();

                var asientos = db.Asiento.ToList();
                var codigos = new Dictionary<int, string>();
                foreach (var a in detalle)
                {
                    int modeloId = a.Vuelo.Aeronave != null ? a.Vuelo.Aeronave.ModeloAeronaveId : 0;
                    var asiento = asientos.FirstOrDefault(s => s.NumeroAsiento == a.NumeroAsiento && (modeloId == 0 || s.ModeloAeronaveId == modeloId));
                    codigos[a.AsignacionId] = asiento != null ? asiento.Codigo : a.NumeroAsiento.ToString();
                }

                ViewBag.Detalle = detalle;
                ViewBag.CodigosAsiento = codigos;
            }

            return View(comprobantes);
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