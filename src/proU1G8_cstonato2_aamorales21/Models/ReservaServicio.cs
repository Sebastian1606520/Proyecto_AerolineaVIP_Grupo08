using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;

namespace Con_Autenticacion.Negocio
{
    // Lógica de negocio de reservas (Pantallas 8, 10 y 14 · MOD-05).
    // Confirmación de la reserva (con control de sobreventa) y cancelación.
    public class ReservaServicio
    {
        private readonly AerolineaVIP_Context db;

        public ReservaServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // =====================================================================
        // TRAMOS Y TARIFAS
        // =====================================================================

        // Tarifa de ejemplo de un tramo (DP-04): Ruta.TarifaReferencial
        public static decimal TarifaTramo(Vuelo v)
        {
            return v.Frecuencia.Ruta.TarifaReferencial > 0 ? v.Frecuencia.Ruta.TarifaReferencial : 150.00m;
        }

        // Devuelve los tramos que se venden juntos: 1 vuelo directo, o los 2 tramos del
        // itinerario dominical de Galápagos (DP-09). null si el itinerario está incompleto.
        public List<Vuelo> ObtenerTramos(Vuelo vuelo)
        {
            if (vuelo.Frecuencia.DiaSemana != 7)
            {
                return new List<Vuelo> { vuelo };
            }

            int uioId = db.Aeropuerto.Where(a => a.CodigoIATA == "UIO").Select(a => a.AeropuertoId).FirstOrDefault();
            int gyeId = db.Aeropuerto.Where(a => a.CodigoIATA == "GYE").Select(a => a.AeropuertoId).FirstOrDefault();
            int gpsId = db.Aeropuerto.Where(a => a.CodigoIATA == "GPS").Select(a => a.AeropuertoId).FirstOrDefault();

            // Ida: UIO → GYE → GPS   ·   Regreso: GPS → GYE → UIO
            // (se compara por Id: en el .edmx Aeropuerto = DESTINO y Aeropuerto1 = ORIGEN)
            var ruta = vuelo.Frecuencia.Ruta;
            bool esIda = ruta.AeropuertoOrigenId == uioId || ruta.AeropuertoDestinoId == gpsId;
            int origenId = esIda ? uioId : gpsId;
            int destinoId = esIda ? gpsId : uioId;

            DateTime fecha = vuelo.FechaOperacion.Date;
            var vuelosDia = db.Vuelo
                .Include(v => v.Frecuencia.Ruta.Aeropuerto)
                .Include(v => v.Frecuencia.Ruta.Aeropuerto1)
                .Include(v => v.Aeronave)
                .Include(v => v.ReservaPasajeroVuelo)
                .Where(v => v.FechaOperacion == fecha)
                .ToList();

            var tramo1 = vuelosDia.FirstOrDefault(v => v.Frecuencia.Ruta.AeropuertoOrigenId == origenId
                                                    && v.Frecuencia.Ruta.AeropuertoDestinoId == gyeId);
            var tramo2 = vuelosDia.FirstOrDefault(v => v.Frecuencia.Ruta.AeropuertoOrigenId == gyeId
                                                    && v.Frecuencia.Ruta.AeropuertoDestinoId == destinoId);

            return (tramo1 != null && tramo2 != null) ? new List<Vuelo> { tramo1, tramo2 } : null;
        }

        // =====================================================================
        // CONFIRMACIÓN DE LA RESERVA (Pantalla 10)
        // =====================================================================

        // 1. Revalida cada tramo en el servidor (RNP-04): vuelo vigente, asientos libres
        //    y pasajeros sin otro asiento activo en el mismo vuelo.
        //    Devuelve null si todo está bien, o el mensaje de error.
        //    volverA indica a qué pantalla regresar: "Buscar", "SeleccionAsiento" o "Confirmar".
        public string RevalidarTramos(List<Vuelo> tramos, List<List<int>> asientosPorTramo,
                                      List<int> pasajerosIds, out string volverA)
        {
            volverA = null;

            for (int k = 0; k < tramos.Count; k++)
            {
                var t = tramos[k];
                string codVuelo = "VIP" + t.VueloId.ToString("D5");

                if ((t.EstadoVueloCodigo != "PROGRAMADO" && t.EstadoVueloCodigo != "CONFIRMADO") || t.AeronaveId == null)
                {
                    volverA = "Buscar";
                    return "El vuelo " + codVuelo + " ya no admite reservas.";
                }

                int idTramo = t.VueloId;
                var activos = db.ReservaPasajeroVuelo
                    .Where(r => r.VueloId == idTramo && (r.EstadoAsignacionCodigo == "PENDIENTE" || r.EstadoAsignacionCodigo == "CONFIRMADA"))
                    .Select(r => new { r.NumeroAsiento, r.PasajeroId })
                    .ToList();

                // Asiento tomado por otra persona mientras se llenaban los datos
                if (asientosPorTramo[k].Any(n => activos.Any(a => a.NumeroAsiento == n)))
                {
                    volverA = "SeleccionAsiento";
                    return "Uno de los asientos elegidos en el vuelo " + codVuelo + " acaba de ser reservado. Elija otros asientos.";
                }

                // Pasajero que ya tiene un asiento activo en ese vuelo (UX_RPV_PasajeroActivo)
                int repetido = pasajerosIds.FirstOrDefault(pid => activos.Any(a => a.PasajeroId == pid));
                if (repetido != 0)
                {
                    var pas = db.Pasajero.Find(repetido);
                    volverA = "Confirmar";
                    return "El pasajero " + (pas != null ? pas.Nombres + " " + pas.Apellidos : "")
                         + " ya tiene un asiento reservado o comprado en el vuelo " + codVuelo + ".";
                }
            }

            return null;
        }

        // 2. Vencimiento de la retención (DP-07 / RG-19): parámetro, con tope en el cierre del check-in.
        //    Devuelve null si la reserva todavía puede hacerse, o el mensaje de error.
        public string CalcularExpiracion(List<Vuelo> tramos, DateTime ahora, out DateTime expiracion)
        {
            expiracion = ahora.AddMinutes(Utilidades.LeerParametro("RetencionReservaMin", 120));
            DateTime salida = tramos[0].FechaOperacion.Add(tramos[0].Frecuencia.HoraSalida);
            DateTime cierreCheckIn = salida.AddMinutes(-Utilidades.LeerParametro("CheckInCierreMin", 90));
            if (cierreCheckIn < expiracion) expiracion = cierreCheckIn;

            if (expiracion <= ahora.AddMinutes(1))
                return "El vuelo sale muy pronto y ya no admite reservas.";

            return null;
        }

        // Reserva hecha por un Counter: busca la cuenta del cliente por su correo.
        // Solo cuentas con rol Cliente: el counter no reserva a nombre de personal.
        // Devuelve null si lo encontró (clienteId queda con su Id), o el mensaje de error.
        public string BuscarClienteParaCounter(string correoCliente, out string clienteId)
        {
            clienteId = null;
            correoCliente = (correoCliente ?? "").Trim();

            if (correoCliente == "")
                return "Ingrese el correo del cliente al que pertenece la reserva.";

            using (var identity = new ApplicationDbContext())
            {
                var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(identity));
                var cliente = userManager.FindByEmail(correoCliente);

                if (cliente == null || !userManager.IsInRole(cliente.Id, "Cliente"))
                {
                    return "No existe un cliente registrado con el correo " + correoCliente
                         + ". Si no tiene cuenta, primero debe registrarse.";
                }

                clienteId = cliente.Id;
            }
            return null;
        }

        // 3. Reserva y asientos en UNA transacción (no quedan reservas sin asientos).
        //    Devuelve null si se guardó (reserva queda con la nueva reserva), o el mensaje de error.
        public string CrearReserva(string usuarioId, List<Vuelo> tramos, List<List<int>> asientosPorTramo,
                                   List<int> pasajerosIds, DateTime ahora, DateTime expiracion, out Reserva reserva)
        {
            reserva = null;

            using (var transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    var nueva = new Reserva
                    {
                        UsuarioId = usuarioId,
                        FechaCreacion = ahora,
                        FechaExpiracion = expiracion,
                        EstadoReservaCodigo = "PENDIENTE"
                    };
                    db.Reserva.Add(nueva);
                    db.SaveChanges();   // genera ReservaId y CodigoReserva (columna calculada)

                    // Una asignación por pasajero y por tramo (DP-09), en morado (PENDIENTE)
                    for (int k = 0; k < tramos.Count; k++)
                    {
                        for (int i = 0; i < pasajerosIds.Count; i++)
                        {
                            db.ReservaPasajeroVuelo.Add(new ReservaPasajeroVuelo
                            {
                                ReservaId = nueva.ReservaId,
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
                    reserva = nueva;
                    return null;
                }
                catch (Exception ex)
                {
                    transaccion.Rollback();
                    return "No se pudo registrar la reserva: " + Utilidades.MensajeInterno(ex);
                }
            }
        }

        // =====================================================================
        // CANCELACIÓN (Pantalla 14 · DP-08)
        // =====================================================================

        // Cancela la reserva: libera asientos, trata la compra y el pago simulado
        // y emite la nota de crédito cuando corresponde. Todo en una transacción.
        // Devuelve null si se canceló (mensajeExito queda con el texto para mostrar),
        // o el mensaje de error.
        public string CancelarReserva(Reserva reserva, string motivo, out string mensajeExito)
        {
            mensajeExito = null;

            // Se vuelve a evaluar en el servidor: el estado pudo cambiar mientras se revisaba
            bool conReembolso;
            double horasRestantes;
            string motivoNoCancelable = reserva.EvaluarCancelacion(out conReembolso, out horasRestantes);
            if (motivoNoCancelable != null)
                return motivoNoCancelable;

            // Motivo obligatorio (10-300 caracteres) para reservas pagadas
            motivo = (motivo ?? "").Trim();
            if (reserva.EstadoReservaCodigo == "CONFIRMADA" && (motivo.Length < 10 || motivo.Length > 300))
                return "Describa el motivo de la cancelación (entre 10 y 300 caracteres).";

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
                    return "No se pudo cancelar la reserva: " + Utilidades.MensajeInterno(ex);
                }
            }

            string detalle = "";
            if (estabaPagada)
            {
                detalle = conReembolso
                    ? " Se registró un reembolso simulado total y una nota de crédito académica."
                    : " Por la cercanía del vuelo, la cancelación no genera reembolso.";
            }
            mensajeExito = "Reserva " + reserva.CodigoReserva + " cancelada. Los asientos fueron liberados." + detalle;
            return null;
        }
    }
}