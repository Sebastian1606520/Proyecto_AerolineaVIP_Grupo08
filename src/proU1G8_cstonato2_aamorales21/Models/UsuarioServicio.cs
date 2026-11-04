using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using System.Linq;
using System.Text.RegularExpressions;

namespace Con_Autenticacion.Negocio
{
    // Lógica de negocio de usuarios y roles (Pantalla 20 · MOD-01 y MOD-10).
    // Trabaja sobre la base AerolineaVIP_Identity mediante UserManager.
    // Quién hace la petición se recibe como parámetro (el servicio no depende de HTTP).
    public class UsuarioServicio
    {
        // Roles de negocio (los roles CRUD Create/Edit/Details/Delete son aparte)
        public static readonly string[] RolesPrincipales = { "SuperUsuario", "Administrador", "Counter", "Cliente" };

        private static UserManager<ApplicationUser> CrearUserManager(ApplicationDbContext identity)
        {
            return new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(identity));
        }

        // Cambia el correo de un usuario.
        // Regla: un Administrador solo edita cuentas de Cliente o Counter;
        //        las cuentas administrativas solo las modifica el SuperUsuario.
        // Devuelve null si se guardó (mensajeExito queda con el texto), o el mensaje de error.
        // volverAEdicion indica si, ante un error, se debe reabrir el formulario.
        public string EditarCorreo(string userId, string email, bool solicitanteEsSuperUsuario,
                                   out string mensajeExito, out bool volverAEdicion)
        {
            mensajeExito = null;
            volverAEdicion = true;

            email = (email ?? "").Trim();
            if (!Regex.IsMatch(email, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                return "Ingrese un correo electrónico válido.";

            using (var identity = new ApplicationDbContext())
            {
                var userManager = CrearUserManager(identity);
                var usuario = userManager.FindById(userId);
                if (usuario == null)
                {
                    volverAEdicion = false;
                    return "El usuario no existe.";
                }

                bool cuentaElevada = userManager.IsInRole(userId, "SuperUsuario") || userManager.IsInRole(userId, "Administrador");
                if (cuentaElevada && !solicitanteEsSuperUsuario)
                {
                    volverAEdicion = false;
                    return "Solo el SuperUsuario puede modificar cuentas administrativas.";
                }

                usuario.Email = email;
                var resultado = userManager.Update(usuario);   // valida correo único (RequireUniqueEmail)
                if (!resultado.Succeeded)
                    return "No se guardaron los cambios: " + string.Join(" ", resultado.Errors);

                mensajeExito = "Cambios guardados para " + usuario.UserName + ".";
            }
            return null;
        }

        // Asigna el rol principal de un usuario (solo SuperUsuario).
        // Quita los otros roles principales; los roles CRUD no se tocan.
        // Devuelve null si se guardó (mensajeExito queda con el texto), o el mensaje de error.
        public string AsignarRol(string userId, string rol, string solicitanteId, bool solicitanteEsSuperUsuario,
                                 out string mensajeExito, out bool volverAEdicion)
        {
            mensajeExito = null;
            volverAEdicion = false;

            if (!solicitanteEsSuperUsuario)
                return "Solo el SuperUsuario puede asignar roles.";

            volverAEdicion = true;

            if (!RolesPrincipales.Contains(rol))
                return "Seleccione un rol válido.";

            // El SuperUsuario no puede quitarse su propio rol (evita quedar sin acceso)
            if (userId == solicitanteId && rol != "SuperUsuario")
                return "No puede quitarse su propio rol de SuperUsuario.";

            using (var identity = new ApplicationDbContext())
            {
                var userManager = CrearUserManager(identity);
                var usuario = userManager.FindById(userId);
                if (usuario == null)
                {
                    volverAEdicion = false;
                    return "El usuario no existe.";
                }

                foreach (var r in RolesPrincipales)
                {
                    if (r != rol && userManager.IsInRole(userId, r))
                        userManager.RemoveFromRole(userId, r);
                }
                if (!userManager.IsInRole(userId, rol))
                    userManager.AddToRole(userId, rol);

                mensajeExito = "Rol de " + usuario.UserName + " actualizado a " + rol + ". El cambio se aplica en su próximo inicio de sesión.";
            }
            return null;
        }
    }
}