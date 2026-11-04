using System;
using System.Configuration;

namespace Con_Autenticacion.Negocio
{
    // Funciones de apoyo compartidas por las clases de negocio y los controladores
    public static class Utilidades
    {
        // Lee un parámetro entero de Web.config (appSettings) o usa el valor por defecto
        public static int LeerParametro(string clave, int valorPorDefecto)
        {
            int valor;
            if (int.TryParse(ConfigurationManager.AppSettings[clave], out valor))
            {
                return valor;
            }
            return valorPorDefecto;
        }

        // Mensaje real de la base de datos (triggers, restricciones)
        public static string MensajeInterno(Exception ex)
        {
            while (ex.InnerException != null) ex = ex.InnerException;
            return ex.Message;
        }
    }
}