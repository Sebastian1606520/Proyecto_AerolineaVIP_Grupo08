using System;
using System.Linq;

namespace Con_Autenticacion.Models
{
    public partial class Pasajero
    {
        // 1. Nombre completo para mostrar en pantalla o boletos
        public string NombreCompleto
        {
            get { return (Nombres + " " + Apellidos).Trim(); }
        }

        // 2. Validador principal del documento (Cédula o Pasaporte)
        public bool EsDocumentoValido()
        {
            if (string.IsNullOrWhiteSpace(NumeroDocumento))
                return false;

            string doc = NumeroDocumento.Trim();

            // Si es cédula: valida 10 dígitos y el algoritmo Módulo 10
            if (TipoDocumentoCodigo == "CEDULA")
            {
                return ValidarCedula(doc);
            }

            // Si es pasaporte: solo letras y números (entre 5 y 20 caracteres)
            if (TipoDocumentoCodigo == "PASAPORTE")
            {
                return doc.Length >= 5 && doc.Length <= 20 && doc.All(char.IsLetterOrDigit);
            }

            return false;
        }

        // 3. Algoritmo Módulo 10 para cédulas ecuatorianas
        public bool ValidarCedula(string cedula)
        {
            // La cédula debe tener exactamente 10 dígitos numéricos
            if (string.IsNullOrWhiteSpace(cedula) || cedula.Length != 10 || !cedula.All(char.IsDigit))
                return false;

            // Código de provincia: 01 al 24 o 30
            int provincia = int.Parse(cedula.Substring(0, 2));
            if ((provincia < 1 || provincia > 24) && provincia != 30)
                return false;

            // El tercer dígito para personas naturales debe ser menor a 6
            int tercerDigito = int.Parse(cedula.Substring(2, 1));
            if (tercerDigito >= 6)
                return false;

            // Coeficientes fijos para la multiplicación alterna
            int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
            int suma = 0;

            for (int i = 0; i < 9; i++)
            {
                int digito = int.Parse(cedula[i].ToString());
                int producto = digito * coeficientes[i];

                // Si el producto es 10 o más, se le resta 9
                if (producto >= 10)
                {
                    producto = producto - 9;
                }

                suma = suma + producto;
            }

            // Se calcula el dígito verificador esperado con la decena superior
            int digitoVerificador = int.Parse(cedula[9].ToString());
            int residuo = suma % 10;
            int resultado = (residuo == 0) ? 0 : 10 - residuo;

            return resultado == digitoVerificador;
        }

        // 4. Validación paso a paso antes de guardar
        public string ValidarDatosCompletos(int numeroPasajero)
        {
            string prefijo = "Pasajero " + numeroPasajero + ": ";

            // Limpieza básica de espacios
            NumeroDocumento = (NumeroDocumento ?? "").Trim().ToUpper();
            Nombres = (Nombres ?? "").Trim();
            Apellidos = (Apellidos ?? "").Trim();
            Correo = string.IsNullOrWhiteSpace(Correo) ? null : Correo.Trim();
            Telefono = string.IsNullOrWhiteSpace(Telefono) ? null : Telefono.Trim();
            // Tipo de documento obligatorio
            if (TipoDocumentoCodigo != "CEDULA" && TipoDocumentoCodigo != "PASAPORTE")
                return prefijo + "seleccione el tipo de documento.";
            // Validación de documento
            if (!EsDocumentoValido())
            {
                if (TipoDocumentoCodigo == "CEDULA")
                    return prefijo + "la cédula ingresada no es válida.";
                else
                    return prefijo + "el pasaporte debe tener entre 5 y 20 letras o números.";
            }

            // Nombres y apellidos: obligatorios, solo letras y espacios (máx. 100)
            var soloLetras = new System.Text.RegularExpressions.Regex(@"^[A-Za-zÁÉÍÓÚáéíóúÑñÜü ]{1,100}$");

            if (!soloLetras.IsMatch(Nombres))
                return prefijo + "los nombres son obligatorios y solo admiten letras y espacios.";

            if (!soloLetras.IsMatch(Apellidos))
                return prefijo + "los apellidos son obligatorios y solo admiten letras y espacios.";

            // Fecha de nacimiento coherente
            if (FechaNacimiento.HasValue)
            {
                if (FechaNacimiento.Value.Year < 1900 || FechaNacimiento.Value.Date > DateTime.Today)
                    return prefijo + "la fecha de nacimiento no puede ser futura ni anterior a 1900.";
            }

            // Correo (opcional): formato válido y máximo 256 caracteres
            if (Correo != null && (Correo.Length > 256 || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(Correo)))
            {
                return prefijo + "el correo electrónico debe contener un formato válido (ejemplo@correo.com).";
            }

            // Teléfono (opcional): dígitos, espacios y «+», máximo 20
            if (Telefono != null && !System.Text.RegularExpressions.Regex.IsMatch(Telefono, @"^[0-9 +]{1,20}$"))
            {
                return prefijo + "el teléfono solo admite dígitos, espacios y «+» (máximo 20).";
            }

            return null; // Todo correcto
        }
    }
}