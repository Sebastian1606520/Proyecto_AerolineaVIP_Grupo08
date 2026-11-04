using Con_Autenticacion.Models;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Con_Autenticacion.Negocio
{
    // Lógica de negocio de la administración de pasajeros (Pantalla 20 · MOD-04).
    public class PasajeroServicio
    {
        private readonly AerolineaVIP_Context db;

        public PasajeroServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // Crea (pasajeroId = null) o edita un pasajero.
        // Las validaciones coinciden con las restricciones CHECK de la tabla Pasajero.
        // Devuelve null si se guardó, o el mensaje de error.
        // documentoGuardado devuelve el documento normalizado (para buscarlo después).
        public string GuardarPasajero(int? pasajeroId, string tipoDocumentoCodigo, string numeroDocumento, string nombres,
                                      string apellidos, DateTime? fechaNacimiento, string correo, string telefono,
                                      out string documentoGuardado)
        {
            numeroDocumento = (numeroDocumento ?? "").Trim().ToUpper();
            nombres = (nombres ?? "").Trim();
            apellidos = (apellidos ?? "").Trim();
            correo = string.IsNullOrWhiteSpace(correo) ? null : correo.Trim();
            telefono = string.IsNullOrWhiteSpace(telefono) ? null : telefono.Trim();
            documentoGuardado = numeroDocumento;

            // Validaciones del servidor
            if (tipoDocumentoCodigo != "CEDULA" && tipoDocumentoCodigo != "PASAPORTE")
                return "Seleccione el tipo de documento.";
            if (tipoDocumentoCodigo == "CEDULA" && !Regex.IsMatch(numeroDocumento, @"^\d{10}$"))
                return "La cédula debe contener exactamente 10 dígitos numéricos.";
            if (numeroDocumento.Length < 5 || numeroDocumento.Length > 20)
                return "El número de documento debe tener entre 5 y 20 caracteres.";
            if (nombres.Length == 0 || nombres.Length > 100 || apellidos.Length == 0 || apellidos.Length > 100)
                return "Nombres y apellidos son obligatorios (máximo 100 caracteres).";
            if (correo != null && !Regex.IsMatch(correo, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                return "El correo electrónico no tiene un formato válido.";
            if (telefono != null && !Regex.IsMatch(telefono, @"^[0-9+ ]{7,20}$"))
                return "El teléfono solo admite dígitos, espacios y el signo + (7 a 20 caracteres).";
            if (fechaNacimiento.HasValue && (fechaNacimiento.Value.Year <= 1900 || fechaNacimiento.Value.Date > DateTime.Today))
                return "La fecha de nacimiento no es válida.";
            if (db.Pasajero.Any(p => p.TipoDocumentoCodigo == tipoDocumentoCodigo && p.NumeroDocumento == numeroDocumento
                                  && (!pasajeroId.HasValue || p.PasajeroId != pasajeroId.Value)))
                return "Ya existe un pasajero con ese documento.";

            Pasajero pasajero;
            if (pasajeroId.HasValue)
            {
                pasajero = db.Pasajero.Find(pasajeroId.Value);
                if (pasajero == null)
                    return "El pasajero no existe.";
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
                return "No se guardó el pasajero: " + Utilidades.MensajeInterno(ex);
            }
            return null;
        }

        // Elimina un pasajero que no tenga reservas (integridad referencial).
        // Devuelve null si se eliminó (mensajeExito queda con el texto), o el mensaje de error.
        public string EliminarPasajero(int pasajeroId, out string mensajeExito)
        {
            mensajeExito = null;

            var pasajero = db.Pasajero.Find(pasajeroId);
            if (pasajero == null)
                return "El pasajero no existe.";

            if (db.ReservaPasajeroVuelo.Any(a => a.PasajeroId == pasajeroId))
                return "No se puede eliminar: el pasajero tiene reservas asociadas.";

            db.Pasajero.Remove(pasajero);
            db.SaveChanges();
            mensajeExito = "Pasajero " + pasajero.Nombres + " " + pasajero.Apellidos + " eliminado.";
            return null;
        }
    }
}