using System;
using System.Text.RegularExpressions;

namespace Con_Autenticacion.Models
{
    public partial class Pasajero
    {
        // Regla: Valida si el documento es válido según su tipo (Cédula ecuatoriana o Pasaporte)
        public bool EsDocumentoValido(string tipoDocumento)
        {
            if (string.IsNullOrWhiteSpace(NumeroDocumento)) return false;

            string doc = NumeroDocumento.Trim();
            string tipo = (tipoDocumento ?? "").Trim().ToUpper();

            if (tipo.Contains("CED") || tipo == "1")
            {
                return ValidarCedulaEcuatoriana(doc);
            }

            if (tipo.Contains("PAS") || tipo == "2")
            {
                return Regex.IsMatch(doc, @"^[a-zA-Z0-9]{6,12}$");
            }

            return false;
        }

        public bool EsDocumentoValidoAuto
        {
            get
            {
                if (string.IsNullOrWhiteSpace(NumeroDocumento)) return false;

                string doc = NumeroDocumento.Trim();

                // Si tiene 10 dígitos numéricos, se valida como cédula ecuatoriana
                if (doc.Length == 10 && long.TryParse(doc, out _))
                {
                    return ValidarCedulaEcuatoriana(doc);
                }

                // De lo contrario, se evalúa formato estándar de pasaporte
                return Regex.IsMatch(doc, @"^[a-zA-Z0-9]{6,12}$");
            }
        }

        // Algoritmo Módulo 10 para cédulas ecuatorianas
        private bool ValidarCedulaEcuatoriana(string cedula)
        {
            if (cedula.Length != 10 || !long.TryParse(cedula, out _))
                return false;

            int provincia = int.Parse(cedula.Substring(0, 2));
            if (provincia < 1 || (provincia > 24 && provincia != 30))
                return false;

            int tercerDigito = int.Parse(cedula.Substring(2, 1));
            if (tercerDigito >= 6)
                return false;

            int[] coeficientes = { 2, 1, 2, 1, 2, 1, 2, 1, 2 };
            int total = 0;

            for (int i = 0; i < 9; i++)
            {
                int valor = int.Parse(cedula[i].ToString()) * coeficientes[i];
                total += (valor >= 10) ? valor - 9 : valor;
            }

            int digitoVerificador = int.Parse(cedula[9].ToString());
            int decenaSuperior = (int)Math.Ceiling(total / 10.0) * 10;
            int calculo = decenaSuperior - total;
            int resultado = (calculo == 10) ? 0 : calculo;

            return resultado == digitoVerificador;
        }
    }
}