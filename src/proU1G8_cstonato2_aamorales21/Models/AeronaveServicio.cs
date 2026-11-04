using Con_Autenticacion.Models;
using System;
using System.Linq;

namespace Con_Autenticacion.Negocio
{
    // Lógica de negocio de la flota (Pantalla 19 · MOD-03).
    public class AeronaveServicio
    {
        private readonly AerolineaVIP_Context db;

        public AeronaveServicio(AerolineaVIP_Context db)
        {
            this.db = db;
        }

        // Validaciones del servidor para crear o editar una aeronave.
        // aeronaveIdExcluir: Id de la aeronave que se edita (0 al crear una nueva),
        // para que la matrícula no choque consigo misma.
        // Devuelve null si los datos son válidos, o el mensaje de error.
        public string ValidarDatos(string matricula, int modeloAeronaveId, string estadoAeronaveCodigo, int aeronaveIdExcluir)
        {
            matricula = (matricula ?? "").Trim().ToUpper();

            if (matricula.Length == 0 || matricula.Length > 15)
                return "El identificador es obligatorio y admite hasta 15 caracteres.";

            if (db.Aeronave.Any(a => a.Matricula == matricula && a.AeronaveId != aeronaveIdExcluir))
                return "Ya existe otra aeronave con el identificador " + matricula + ".";

            var modelo = db.ModeloAeronave.Find(modeloAeronaveId);
            if (modelo == null || modelo.CapacidadAsientos > 7)
                return "Seleccione un modelo válido (capacidad máxima de siete asientos, RN-03).";

            if (string.IsNullOrEmpty(estadoAeronaveCodigo) || !db.EstadoAeronave.Any(e => e.Codigo == estadoAeronaveCodigo))
                return "Seleccione un estado operativo válido.";

            return null;
        }

        // Vuelos próximos (desde hoy, no cancelados ni completados) asignados a la aeronave
        public int ContarVuelosProximos(int aeronaveId)
        {
            DateTime hoy = DateTime.Today;
            return db.Vuelo.Count(v => v.AeronaveId == aeronaveId && v.FechaOperacion >= hoy
                                    && v.EstadoVueloCodigo != "CANCELADO" && v.EstadoVueloCodigo != "COMPLETADO");
        }

        // Edita matrícula, modelo y estado de una aeronave.
        // Devuelve null si se guardó (mensajeExito queda con el texto para mostrar), o el mensaje de error.
        // noExiste indica que la aeronave no se encontró.
        public string EditarAeronave(int aeronaveId, string matricula, int modeloAeronaveId, string estadoAeronaveCodigo,
                                     out string mensajeExito, out bool noExiste)
        {
            mensajeExito = null;
            noExiste = false;

            var aeronave = db.Aeronave.Find(aeronaveId);
            if (aeronave == null)
            {
                noExiste = true;
                return "La aeronave solicitada no existe.";
            }

            matricula = (matricula ?? "").Trim().ToUpper();

            string error = ValidarDatos(matricula, modeloAeronaveId, estadoAeronaveCodigo, aeronaveId);
            if (error != null)
                return error;

            // Vuelos próximos que quedarían afectados si pasa a NO_DISPONIBLE
            int vuelosProximos = ContarVuelosProximos(aeronaveId);

            aeronave.Matricula = matricula;
            aeronave.ModeloAeronaveId = modeloAeronaveId;
            aeronave.EstadoAeronaveCodigo = estadoAeronaveCodigo;

            try
            {
                db.SaveChanges();
            }
            catch (Exception ex)
            {
                return "No se guardaron los cambios: " + Utilidades.MensajeInterno(ex);
            }

            string aviso = (estadoAeronaveCodigo == "NO_DISPONIBLE" && vuelosProximos > 0)
                ? " Atención: tiene " + vuelosProximos + " vuelos próximos asignados; reasígnelos en la pantalla 18."
                : "";
            mensajeExito = "Cambios guardados en la aeronave " + matricula + "." + aviso;
            return null;
        }
    }
}