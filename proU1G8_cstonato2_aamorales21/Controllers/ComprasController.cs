using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;
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
    public class ComprasController : Controller
    {
        
        private AerolineaVIP_Context db = new AerolineaVIP_Context();
        [Authorize]
        // GET: Compras
        public ActionResult Index()
        {
            var compra = db.Compra.Include(c => c.EstadoCompra).Include(c => c.Moneda).Include(c => c.Reserva);
            return View(compra.ToList());
        }


        [HttpGet]
        public ActionResult Pagar(int? id)
        {
            if (!id.HasValue)
            {
                TempData["Error"] = "Identificador de reserva no válido.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var reserva = db.Reserva.Find(id.Value);
            if (reserva == null)
            {
                TempData["Error"] = "La reserva solicitada no existe.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Validación de titularidad: Solo el titular o personal autorizado
            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;
            if (reserva.UsuarioId != currentUserId && !User.IsInRole("Empleado"))
            {
                TempData["Error"] = "No tiene permisos para procesar esta reserva.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Estado no elegible: solo reservas pendientes pueden pagarse
            if (reserva.EstadoReservaCodigo != "PENDIENTE")
            {
                TempData["Error"] = "Esta reserva ya fue procesada o no se encuentra pendiente de pago.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // Cálculo del importe total estrictamente en servidor
            decimal totalCalculado = reserva.ReservaPasajeroVuelo.Sum(r => (decimal?)r.ImporteTramo) ?? 0m;
            ViewBag.TotalPagar = totalCalculado;

            // Datos formateados del itinerario para la vista
            var primerDetalle = reserva.ReservaPasajeroVuelo.FirstOrDefault();
            if (primerDetalle != null && primerDetalle.Vuelo != null)
            {
                var v = primerDetalle.Vuelo;
                ViewBag.CodigoRuta = v.Frecuencia.Ruta.Aeropuerto.CodigoIATA + " → " + v.Frecuencia.Ruta.Aeropuerto1.CodigoIATA;
                ViewBag.FechaVuelo = v.FechaOperacion.ToString("dd/MM/yyyy");
            }
            else
            {
                ViewBag.CodigoRuta = "Ruta directa";
                ViewBag.FechaVuelo = DateTime.Today.ToString("dd/MM/yyyy");
            }

            return View(reserva);
        }

        // =========================================================================
        // PANTALLA 11: PROCESO DE COMPRA Y PAGO (POST)
        // Persistencia atómica de Compra, Pago simulado y actualización de estados
        // =========================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Pagar(int reservaId, string metodoPago, string marcaTarjeta, string numeroTarjeta, string titularTarjeta, string vencimiento, string codigoSeguridad)
        {
            var reserva = db.Reserva.Find(reservaId);
            if (reserva == null)
            {
                TempData["Error"] = "La reserva no existe.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // 1. Revalidar vigencia de la reserva
            if (reserva.EstadoReservaCodigo != "PENDIENTE")
            {
                TempData["Error"] = "La reserva ya no se encuentra en estado pendiente de pago.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // 2. Validación de formato de campos de la tarjeta
            string numLimpio = (numeroTarjeta ?? "").Replace(" ", "").Trim();
            if (string.IsNullOrWhiteSpace(titularTarjeta) || string.IsNullOrWhiteSpace(numLimpio) || numLimpio.Length < 14)
            {
                TempData["Error"] = "Por favor ingrese datos válidos de la tarjeta.";
                return RedirectToAction("Pagar", new { id = reservaId });
            }

            int longitudEsperada = 16;
            if (marcaTarjeta == "AMEX") longitudEsperada = 15;
            else if (marcaTarjeta == "DINERS") longitudEsperada = 14;

            if (numLimpio.Length != longitudEsperada)
            {
                TempData["Error"] = "El número debe tener " + longitudEsperada + " dígitos para " + marcaTarjeta + ".";
                return RedirectToAction("Pagar", new { id = reservaId });
            }

            // Simulación de rechazo: terminación en 0000
            if (numLimpio.EndsWith("0000"))
            {
                TempData["Error"] = "Pago simulado rechazado. Puede intentar con otra tarjeta.";
                return RedirectToAction("Pagar", new { id = reservaId });
            }

            // 3. Importe y metadatos de la transacción
            decimal totalServidor = reserva.ReservaPasajeroVuelo.Sum(r => (decimal?)r.ImporteTramo) ?? 0m;
            string ultimos4 = numLimpio.Substring(numLimpio.Length - 4);
            string opSimulada = "SIM-" + (DateTime.Now.Ticks % 100000000).ToString("D8");

            // 4. Transacción atómica en SQL Server
            using (var transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    // A. Registrar o actualizar Compra (requiere MonedaCodigo = 'USD')
                    var compra = db.Compra.FirstOrDefault(c => c.ReservaId == reservaId);
                    if (compra == null)
                    {
                        compra = new Compra
                        {
                            ReservaId = reservaId,
                            FechaCompra = DateTime.Now,
                            Importe = totalServidor,
                            MonedaCodigo = "USD",
                            EstadoCompraCodigo = "PAGADA"
                        };
                        db.Compra.Add(compra);
                    }
                    else
                    {
                        compra.Importe = totalServidor;
                        compra.MonedaCodigo = "USD";
                        compra.EstadoCompraCodigo = "PAGADA";
                    }
                    db.SaveChanges();

                    // B. Registrar Pago simulado (sin almacenar datos sensibles PAN ni CVV)
                    var nuevoPago = new Pago
                    {
                        CompraId = compra.CompraId,
                        MetodoPagoCodigo = metodoPago ?? "TARJETA_CREDITO",
                        MarcaTarjetaCodigo = marcaTarjeta ?? "VISA",
                        UltimosCuatroDigitos = ultimos4,
                        NombreTitular = titularTarjeta.Trim().ToUpper(),
                        CodigoOperacion = opSimulada,
                        Importe = totalServidor,
                        FechaPago = DateTime.Now,
                        EsSimulado = true,
                        EstadoPagoCodigo = "APROBADO"
                    };
                    db.Pago.Add(nuevoPago);

                    // C. Actualizar estado de Reserva a CONFIRMADA
                    reserva.EstadoReservaCodigo = "CONFIRMADA";

                    // D. Actualizar asignación de asientos de PENDIENTE a CONFIRMADA
                    foreach (var rp in reserva.ReservaPasajeroVuelo)
                    {
                        rp.EstadoAsignacionCodigo = "CONFIRMADA";
                    }

                    db.SaveChanges();
                    transaccion.Commit();
                }
                catch (Exception ex)
                {
                    transaccion.Rollback();
                    Exception inner = ex;
                    while (inner.InnerException != null) inner = inner.InnerException;
                    TempData["Error"] = "Error al procesar el pago: " + inner.Message;
                    return RedirectToAction("Pagar", new { id = reservaId });
                }
            }

            // 5. Redirección a la Pantalla 12 (Confirmación / Resultado)
            return RedirectToAction("Confirmacion", "Compras", new { id = reservaId });
        }

        // =========================================================================
        // PANTALLA 12: RESULTADO DE LA COMPRA / CONFIRMACIÓN (GET)
        // =========================================================================
        [HttpGet]
        public ActionResult Resultado(int? id)
        {
            if (!id.HasValue)
            {
                return RedirectToAction("Buscar", "Vuelos");
            }

            var compra = db.Compra.Find(id.Value);
            if (compra == null)
            {
                TempData["Error"] = "El comprobante de compra no fue encontrado.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            return View(compra);
        }


        [HttpGet]
        [Authorize]
        public ActionResult Confirmacion(int? id)
        {
            if (!id.HasValue)
            {
                TempData["Error"] = "Identificador de operación no válido.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            var reserva = db.Reserva.Find(id.Value);
            if (reserva == null)
            {
                TempData["Error"] = "No se encontró la operación solicitada.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            string currentUserId = User.Identity.GetUserId() ?? User.Identity.Name;
            if (reserva.UsuarioId != currentUserId && !User.IsInRole("Empleado"))
            {
                TempData["Error"] = "No tiene permisos para acceder a esta información.";
                return RedirectToAction("Buscar", "Vuelos");
            }

            // 1. Obtener Compra y Pago
            var compra = db.Compra.FirstOrDefault(c => c.ReservaId == reserva.ReservaId);
            Pago ultimoPago = null;
            if (compra != null)
            {
                ultimoPago = db.Pago
                    .Where(p => p.CompraId == compra.CompraId)
                    .OrderByDescending(p => p.FechaPago)
                    .FirstOrDefault();
            }

            // 2. Determinar estados booleanos y de texto en el controlador
            bool esPagada = (compra != null && compra.EstadoCompraCodigo == "PAGADA" && ultimoPago != null && ultimoPago.EstadoPagoCodigo == "APROBADO");
            bool esSoloReserva = (compra == null);
            bool esRechazada = (ultimoPago != null && ultimoPago.EstadoPagoCodigo == "RECHAZADO");

            ViewBag.EsPagada = esPagada;
            ViewBag.EsSoloReserva = esSoloReserva;
            ViewBag.EsRechazada = esRechazada;

            ViewBag.EstadoReservaTexto = reserva.EstadoReservaCodigo ?? "PENDIENTE";
            ViewBag.EstadoCompraTexto = compra != null ? compra.EstadoCompraCodigo : "NO GENERADA";
            ViewBag.EstadoPagoTexto = ultimoPago != null ? ultimoPago.EstadoPagoCodigo : "NO INTENTADO";

            ViewBag.TienePago = (ultimoPago != null);
            if (ultimoPago != null)
            {
                ViewBag.CodigoOperacion = ultimoPago.CodigoOperacion;
                ViewBag.MetodoPagoTexto = ultimoPago.MetodoPagoCodigo;
                ViewBag.TarjetaTexto = ultimoPago.MarcaTarjetaCodigo + " **** " + ultimoPago.UltimosCuatroDigitos;
            }
            else
            {
                ViewBag.CodigoOperacion = "";
                ViewBag.MetodoPagoTexto = "";
                ViewBag.TarjetaTexto = "";
            }

            // 3. Importe total
            decimal totalCalculado = reserva.ReservaPasajeroVuelo.Sum(r => (decimal?)r.ImporteTramo) ?? 0m;
            ViewBag.TotalPagar = totalCalculado;

            // 4. Datos del itinerario listos
            var primerTramo = reserva.ReservaPasajeroVuelo.FirstOrDefault();
            if (primerTramo != null && primerTramo.Vuelo != null)
            {
                var v = primerTramo.Vuelo;
                ViewBag.RutaStr = v.Frecuencia.Ruta.Aeropuerto.Localidad + " (" + v.Frecuencia.Ruta.Aeropuerto.CodigoIATA + ") → " +
                                  v.Frecuencia.Ruta.Aeropuerto1.Localidad + " (" + v.Frecuencia.Ruta.Aeropuerto1.CodigoIATA + ")";
                ViewBag.FechaVueloStr = v.FechaOperacion.ToString("dd/MM/yyyy");
                ViewBag.HorarioStr = v.Frecuencia.HoraSalida.ToString(@"hh\:mm") + " – " + v.Frecuencia.HoraLlegada.ToString(@"hh\:mm");
            }
            else
            {
                ViewBag.RutaStr = "Ruta directa";
                ViewBag.FechaVueloStr = "-";
                ViewBag.HorarioStr = "-";
            }

            return View(reserva);
        }

        // GET: Compras/Details/5
        public ActionResult Details(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Compra compra = db.Compra.Find(id);
            if (compra == null)
            {
                return HttpNotFound();
            }
            return View(compra);
        }

        // GET: Compras/Create
        public ActionResult Create()
        {
            ViewBag.EstadoCompraCodigo = new SelectList(db.EstadoCompra, "Codigo", "Descripcion");
            ViewBag.MonedaCodigo = new SelectList(db.Moneda, "Codigo", "Descripcion");
            ViewBag.ReservaId = new SelectList(db.Reserva, "ReservaId", "CodigoReserva");
            return View();
        }

        // POST: Compras/Create
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Create([Bind(Include = "CompraId,ReservaId,FechaCompra,Importe,MonedaCodigo,EstadoCompraCodigo,ClaveIdempotencia")] Compra compra)
        {
            if (ModelState.IsValid)
            {
                db.Compra.Add(compra);
                db.SaveChanges();
                return RedirectToAction("Index");
            }

            ViewBag.EstadoCompraCodigo = new SelectList(db.EstadoCompra, "Codigo", "Descripcion", compra.EstadoCompraCodigo);
            ViewBag.MonedaCodigo = new SelectList(db.Moneda, "Codigo", "Descripcion", compra.MonedaCodigo);
            ViewBag.ReservaId = new SelectList(db.Reserva, "ReservaId", "CodigoReserva", compra.ReservaId);
            return View(compra);
        }

        // GET: Compras/Edit/5
        public ActionResult Edit(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Compra compra = db.Compra.Find(id);
            if (compra == null)
            {
                return HttpNotFound();
            }
            ViewBag.EstadoCompraCodigo = new SelectList(db.EstadoCompra, "Codigo", "Descripcion", compra.EstadoCompraCodigo);
            ViewBag.MonedaCodigo = new SelectList(db.Moneda, "Codigo", "Descripcion", compra.MonedaCodigo);
            ViewBag.ReservaId = new SelectList(db.Reserva, "ReservaId", "CodigoReserva", compra.ReservaId);
            return View(compra);
        }

        // POST: Compras/Edit/5
        // Para protegerse de ataques de publicación excesiva, habilite las propiedades específicas a las que quiere enlazarse. Para obtener 
        // más detalles, vea https://go.microsoft.com/fwlink/?LinkId=317598.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public ActionResult Edit([Bind(Include = "CompraId,ReservaId,FechaCompra,Importe,MonedaCodigo,EstadoCompraCodigo,ClaveIdempotencia")] Compra compra)
        {
            if (ModelState.IsValid)
            {
                db.Entry(compra).State = EntityState.Modified;
                db.SaveChanges();
                return RedirectToAction("Index");
            }
            ViewBag.EstadoCompraCodigo = new SelectList(db.EstadoCompra, "Codigo", "Descripcion", compra.EstadoCompraCodigo);
            ViewBag.MonedaCodigo = new SelectList(db.Moneda, "Codigo", "Descripcion", compra.MonedaCodigo);
            ViewBag.ReservaId = new SelectList(db.Reserva, "ReservaId", "CodigoReserva", compra.ReservaId);
            return View(compra);
        }

        // GET: Compras/Delete/5
        public ActionResult Delete(int? id)
        {
            if (id == null)
            {
                return new HttpStatusCodeResult(HttpStatusCode.BadRequest);
            }
            Compra compra = db.Compra.Find(id);
            if (compra == null)
            {
                return HttpNotFound();
            }
            return View(compra);
        }

        // POST: Compras/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public ActionResult DeleteConfirmed(int id)
        {
            Compra compra = db.Compra.Find(id);
            db.Compra.Remove(compra);
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
