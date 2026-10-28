using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;
using Microsoft.AspNet.Identity.EntityFramework;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using System.Web.Optimization;
using System.Web.Routing;

namespace Con_Autenticacion
{
    public class MvcApplication : System.Web.HttpApplication
    {
        protected void Application_Start()
        {
            AreaRegistration.RegisterAllAreas();
            FilterConfig.RegisterGlobalFilters(GlobalFilters.Filters);
            RouteConfig.RegisterRoutes(RouteTable.Routes);
            BundleConfig.RegisterBundles(BundleTable.Bundles);

            ApplicationDbContext db = new ApplicationDbContext();

            CrearRoles(db);
            CrearSuperUsuario(db);
            CrearAdministrador(db);
            CrearCounter(db);
            CrearCliente(db);
            AsignarPermisos(db);

            db.Dispose();
        }

        private void CrearRoles(ApplicationDbContext db)
        {
            var rolemanager = new RoleManager<IdentityRole>(new RoleStore<IdentityRole>(db));

            // Roles del sistema (mismos nombres que usan AccountController y _Layout)
            if (!rolemanager.RoleExists("SuperUsuario"))
            {
                rolemanager.Create(new IdentityRole("SuperUsuario"));
            }

            if (!rolemanager.RoleExists("Administrador"))
            {
                rolemanager.Create(new IdentityRole("Administrador"));
            }

            if (!rolemanager.RoleExists("Counter"))
            {
                rolemanager.Create(new IdentityRole("Counter"));
            }

            // Rol que reciben los usuarios que se registran desde la interfaz pública
            if (!rolemanager.RoleExists("Cliente"))
            {
                rolemanager.Create(new IdentityRole("Cliente"));
            }

            // Permisos CRUD para el control de accesos limitados
            if (!rolemanager.RoleExists("Create"))
            {
                rolemanager.Create(new IdentityRole("Create"));
            }

            if (!rolemanager.RoleExists("Edit"))
            {
                rolemanager.Create(new IdentityRole("Edit"));
            }

            if (!rolemanager.RoleExists("Details"))
            {
                rolemanager.Create(new IdentityRole("Details"));
            }

            if (!rolemanager.RoleExists("Delete"))
            {
                rolemanager.Create(new IdentityRole("Delete"));
            }
        }

        // Creación mediante código de un superusuario con acceso a todas las funciones
        private void CrearSuperUsuario(ApplicationDbContext db)
        {
            var usermanager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            var user = usermanager.FindByName("SuperUsuario");
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = "SuperUsuario",
                    Email = "superusuario@gmail.com"
                };
                var resultado = usermanager.Create(user, "Superadmin123!");
                VerificarResultado(resultado, "SuperUsuario");
            }
        }

        // Creación mediante código de un administrador con acceso limitado a operaciones CRUD
        private void CrearAdministrador(ApplicationDbContext db)
        {
            var usermanager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            var user = usermanager.FindByName("Administrador");
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = "Administrador",
                    Email = "admin@gmail.com"
                };
                var resultado = usermanager.Create(user, "Admin123!");
                VerificarResultado(resultado, "Administrador");
            }
        }

        // Usuario inicial de counter
        private void CrearCounter(ApplicationDbContext db)
        {
            var usermanager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            var user = usermanager.FindByName("Counter01");
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = "Counter01",
                    Email = "counter@gmail.com"
                };
                var resultado = usermanager.Create(user, "Counter123!");
                VerificarResultado(resultado, "Counter01");
            }
        }

        // Usuario inicial de cliente
        private void CrearCliente(ApplicationDbContext db)
        {
            var usermanager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));
            var user = usermanager.FindByName("Cliente01");
            if (user == null)
            {
                user = new ApplicationUser
                {
                    UserName = "Cliente01",
                    Email = "cliente@gmail.com"
                };
                var resultado = usermanager.Create(user, "Cliente123!");
                VerificarResultado(resultado, "Cliente01");
            }
        }

        // Si la creación falla (correo repetido, contraseña inválida...) se muestra el motivo
        private void VerificarResultado(IdentityResult resultado, string nombreUsuario)
        {
            if (!resultado.Succeeded)
            {
                throw new Exception("No se pudo crear el usuario " + nombreUsuario + ": " + string.Join(" ", resultado.Errors));
            }
        }

        // Asignación de roles a los usuarios iniciales mediante código
        private void AsignarPermisos(ApplicationDbContext db)
        {
            var userManager = new UserManager<ApplicationUser>(new UserStore<ApplicationUser>(db));

            // SuperUsuario: acceso total a las funciones y a todo el CRUD (Create, Edit, Details, Delete)
            var superUser = userManager.FindByName("SuperUsuario");
            if (superUser != null)
            {
                if (!userManager.IsInRole(superUser.Id, "SuperUsuario"))
                {
                    userManager.AddToRole(superUser.Id, "SuperUsuario");
                }
                if (!userManager.IsInRole(superUser.Id, "Create"))
                {
                    userManager.AddToRole(superUser.Id, "Create");
                }
                if (!userManager.IsInRole(superUser.Id, "Edit"))
                {
                    userManager.AddToRole(superUser.Id, "Edit");
                }
                if (!userManager.IsInRole(superUser.Id, "Details"))
                {
                    userManager.AddToRole(superUser.Id, "Details");
                }
                if (!userManager.IsInRole(superUser.Id, "Delete"))
                {
                    userManager.AddToRole(superUser.Id, "Delete");
                }
            }

            // Administrador: acceso limitado a las operaciones CRUD que le corresponden (SIN Delete)
            var admin = userManager.FindByName("Administrador");
            if (admin != null)
            {
                if (!userManager.IsInRole(admin.Id, "Administrador"))
                {
                    userManager.AddToRole(admin.Id, "Administrador");
                }
                if (!userManager.IsInRole(admin.Id, "Create"))
                {
                    userManager.AddToRole(admin.Id, "Create");
                }
                if (!userManager.IsInRole(admin.Id, "Edit"))
                {
                    userManager.AddToRole(admin.Id, "Edit");
                }
                if (!userManager.IsInRole(admin.Id, "Details"))
                {
                    userManager.AddToRole(admin.Id, "Details");
                }
            }

            // Counter: atiende clientes; en administración solo consulta
            var counter = userManager.FindByName("Counter01");
            if (counter != null)
            {
                if (!userManager.IsInRole(counter.Id, "Counter"))
                {
                    userManager.AddToRole(counter.Id, "Counter");
                }
                if (!userManager.IsInRole(counter.Id, "Details"))
                {
                    userManager.AddToRole(counter.Id, "Details");
                }
            }

            // Cliente de prueba
            var cliente = userManager.FindByName("Cliente01");
            if (cliente != null)
            {
                if (!userManager.IsInRole(cliente.Id, "Cliente"))
                {
                    userManager.AddToRole(cliente.Id, "Cliente");
                }
            }
        }
    }
}