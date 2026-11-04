using Con_Autenticacion.Models;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Con_Autenticacion.Negocio
{
    // Lógica de negocio de compras y pagos (Pantallas 11 y 12 · MOD-06).
    // El pago es SIMULADO (DP-06): no se guardan el número completo, el vencimiento
    // ni el código de seguridad de la tarjeta.
    public class PagoServicio
    {
        private readonly AerolineaVIP_Context db;

        public PagoServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // Total a pagar: suma del importe de cada tramo y pasajero
        public static decimal TotalReserva(Reserva r)
        {
            return r.ReservaPasajeroVuelo.Sum(a => (decimal?)a.ImporteTramo) ?? 0m;
        }

        // Devuelve null si la reserva puede pagarse, o el motivo por el que no.
        // Si el plazo de retención venció, la reserva pasa a EXPIRADA y sus asientos a LIBERADA (DP-07)
        public string MotivoNoPagable(Reserva r)
        {
            if (r.EstadoReservaCodigo == "CONFIRMADA")
                return "Esta reserva ya fue pagada.";

            if (r.EstadoReservaCodigo != "PENDIENTE")
                return "Esta reserva no se encuentra pendiente de pago.";

            if (r.FechaExpiracion.HasValue && r.FechaExpiracion.Value < DateTime.Now)
            {
                r.EstadoReservaCodigo = "EXPIRADA";
                foreach (var asig in r.ReservaPasajeroVuelo.Where(a => a.EstadoAsignacionCodigo == "PENDIENTE"))
                {
                    asig.EstadoAsignacionCodigo = "LIBERADA";
                }
                db.SaveChanges();   // libera los asientos en la base

                return "El plazo de retención para pagar esta reserva venció el "
                     + r.FechaExpiracion.Value.ToString("dd/MM/yyyy HH:mm")
                     + ". Los asientos fueron liberados; realice una nueva reserva.";
            }

            if (!r.ReservaPasajeroVuelo.Any())
                return "La reserva no tiene pasajeros asignados.";

            return null;
        }

        // Procesa el pago simulado de una reserva.
        // Devuelve null si el pago fue aprobado, o el mensaje de error para mostrar.
        public string ProcesarPago(Reserva reserva, string metodoPago, string marcaTarjeta, string numeroTarjeta,
                                   string titularTarjeta, string vencimiento, string codigoSeguridad)
        {
            // 1. Validación del formulario en el servidor (DP-06)
            var metodo = db.MetodoPago.Find(metodoPago ?? "");
            if (metodo == null || !metodo.Activo)
                return "Seleccione el método de pago.";

            var marca = db.MarcaTarjeta.Find(marcaTarjeta ?? "");
            if (marca == null || !marca.Activa)
                return "Seleccione la marca de la tarjeta.";

            string numLimpio = (numeroTarjeta ?? "").Replace(" ", "").Trim();
            if (numLimpio.Length != marca.LongitudNumero || !numLimpio.All(char.IsDigit))
                return "El número de tarjeta debe tener " + marca.LongitudNumero + " dígitos para " + marca.Nombre + ".";

            string titular = (titularTarjeta ?? "").Trim();
            if (!Regex.IsMatch(titular, @"^[A-Za-zÁÉÍÓÚáéíóúÑñÜü ]{2,100}$"))
                return "Ingrese el nombre del titular (solo letras y espacios).";

            var mVence = Regex.Match((vencimiento ?? "").Trim(), @"^(0[1-9]|1[0-2])/(\d{2})$");
            if (!mVence.Success)
                return "El vencimiento debe tener el formato MM/AA.";

            var primerDiaMesSiguiente = new DateTime(2000 + int.Parse(mVence.Groups[2].Value), int.Parse(mVence.Groups[1].Value), 1).AddMonths(1);
            if (primerDiaMesSiguiente <= DateTime.Today)
                return "La tarjeta está vencida.";

            if (!Regex.IsMatch((codigoSeguridad ?? "").Trim(), @"^\d{" + marca.LongitudCodigoSeguridad + "}$"))
                return "El código de seguridad debe tener " + marca.LongitudCodigoSeguridad + " dígitos.";

            // 2. Rechazo simulado (DP-06): termina en 0000. La reserva sigue PENDIENTE para reintentar
            if (numLimpio.EndsWith("0000"))
                return "Pago simulado rechazado. La reserva sigue pendiente; puede intentar con otra tarjeta.";

            // 3. Pago aprobado: todo en UNA transacción (RG-18)
            return RegistrarPagoAprobado(reserva, metodo, marca, numLimpio, titular);
        }

        // Compra + Pago + Reserva confirmada + Comprobantes en una sola transacción.
        // Devuelve null si todo se guardó, o el mensaje de error.
        private string RegistrarPagoAprobado(Reserva reserva, MetodoPago metodo, MarcaTarjeta marca,
                                             string numLimpio, string titular)
        {
            decimal total = TotalReserva(reserva);
            string ultimos4 = numLimpio.Substring(numLimpio.Length - 4);
            string opSimulada = "SIM-" + (DateTime.Now.Ticks % 100000000).ToString("D8");

            using (var transaccion = db.Database.BeginTransaction())
            {
                try
                {
                    // A. Compra (se reutiliza si ya existía una PENDIENTE)
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
                    db.SaveChanges();   // genera CompraId

                    // B. Pago simulado (sin número completo, vencimiento ni código de seguridad)
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

                    // C. Reserva y asientos confirmados (asientos en rojo)
                    reserva.EstadoReservaCodigo = "CONFIRMADA";
                    foreach (var rp in reserva.ReservaPasajeroVuelo.Where(a => a.EstadoAsignacionCodigo == "PENDIENTE"))
                    {
                        rp.EstadoAsignacionCodigo = "CONFIRMADA";
                    }

                    // D. Comprobantes académicos (DP-05)
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
                    transaccion.Commit();   // un solo Commit
                    return null;
                }
                catch (Exception ex)
                {
                    transaccion.Rollback();
                    return "No se pudo procesar el pago: " + Utilidades.MensajeInterno(ex);
                }
            }
        }
    }
}