using Con_Autenticacion.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Con_Autenticacion.Negocio
{
    // Lógica de negocio del check-in (Pantalla 15 · MOD-07).
    // El controlador solo recibe la petición, verifica permisos y muestra el resultado.
    public class CheckInServicio
    {
        private readonly AerolineaVIP_Context db;

        public CheckInServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // Validación de formato en el servidor.
        // Devuelve null si los datos son correctos o el mensaje de error.
        public string ValidarFormato(string codigo, string documento)
        {
            if (!Regex.IsMatch(codigo, @"^RV\d{8}$"))
            {
                return "El código de reserva debe tener el formato RV seguido de 8 dígitos.";
            }
            if (documento.Length < 5 || documento.Length > 20)
            {
                return "El número de documento debe tener entre 5 y 20 caracteres.";
            }
            return null;
        }

        // RG-37: el código y el documento deben corresponder a la misma reserva.
        // Devuelve null si la reserva no existe o si el documento no coincide.
        public Reserva BuscarReserva(string codigo, string documento)
        {
            var reserva = db.Reserva.FirstOrDefault(r => r.CodigoReserva == codigo);
            if (reserva == null)
            {
                return null;
            }

            bool documentoCoincide = reserva.ReservaPasajeroVuelo
                .Any(a => a.Pasajero.NumeroDocumento.ToUpper() == documento);

            return documentoCoincide ? reserva : null;
        }

        // Devuelve la elegibilidad de una asignación para el check-in.
        // Retorna "ELEGIBLE" o el texto que explica por qué no es elegible.
        public string EvaluarElegibilidad(ReservaPasajeroVuelo a)
        {
            int aperturaHoras = Utilidades.LeerParametro("CheckInAperturaHoras", 48);
            int cierreMin = Utilidades.LeerParametro("CheckInCierreMin", 90);

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

        // Asignaciones de la reserva ordenadas por tramo y pasajero (para mostrarlas)
        public List<ReservaPasajeroVuelo> ObtenerAsignacionesOrdenadas(Reserva reserva)
        {
            return reserva.ReservaPasajeroVuelo
                .OrderBy(a => a.Vuelo.FechaOperacion).ThenBy(a => a.Vuelo.Frecuencia.HoraSalida)
                .ThenBy(a => a.Pasajero.Apellidos)
                .ToList();
        }

        // Cuántas asignaciones pueden registrar check-in ahora
        public static int ContarElegibles(Dictionary<int, string> elegibilidad)
        {
            return elegibilidad.Values.Count(e => e == "ELEGIBLE");
        }

        // Elegibilidad de cada asignación (pasajero + vuelo) de la reserva
        public Dictionary<int, string> EvaluarReserva(Reserva reserva)
        {
            var elegibilidad = new Dictionary<int, string>();
            foreach (var a in reserva.ReservaPasajeroVuelo)
            {
                elegibilidad[a.AsignacionId] = EvaluarElegibilidad(a);
            }
            return elegibilidad;
        }

        // Código visible del asiento (tabla Asiento) de cada asignación
        public Dictionary<int, string> ObtenerCodigosAsiento(Reserva reserva)
        {
            var codigosAsiento = new Dictionary<int, string>();
            var asientos = db.Asiento.ToList();

            foreach (var a in reserva.ReservaPasajeroVuelo)
            {
                int modeloId = a.Vuelo.Aeronave != null ? a.Vuelo.Aeronave.ModeloAeronaveId : 0;
                var asiento = asientos.FirstOrDefault(s => s.NumeroAsiento == a.NumeroAsiento
                                                        && (modeloId == 0 || s.ModeloAeronaveId == modeloId));
                codigosAsiento[a.AsignacionId] = asiento != null ? asiento.Codigo : a.NumeroAsiento.ToString();
            }
            return codigosAsiento;
        }

        // Registra el check-in de las asignaciones marcadas.
        // Cada una se vuelve a validar en el servidor (la ventana pudo cerrarse).
        // Llena las listas con los pasajeros registrados y rechazados.
        public void Registrar(Reserva reserva, List<int> asignaciones,
                              List<string> registrados, List<string> rechazados)
        {
            foreach (int asignacionId in asignaciones.Distinct())
            {
                // Solo asignaciones que pertenecen a esta reserva
                var a = reserva.ReservaPasajeroVuelo.FirstOrDefault(x => x.AsignacionId == asignacionId);
                if (a == null) continue;

                string nombre = a.Pasajero.Nombres + " " + a.Pasajero.Apellidos + " en VIP" + a.VueloId.ToString("D5");

                if (EvaluarElegibilidad(a) != "ELEGIBLE")
                {
                    rechazados.Add(nombre);
                    continue;
                }

                db.CheckIn.Add(new CheckIn
                {
                    AsignacionId = a.AsignacionId,
                    FechaRegistro = DateTime.Now,
                    EstadoCheckInCodigo = "COMPLETADO"
                });
                registrados.Add(nombre);
            }

            db.SaveChanges();
        }
    }
}