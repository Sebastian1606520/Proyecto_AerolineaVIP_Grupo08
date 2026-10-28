using System;
using System.Collections.Generic;
using System.Data;
using System.Data.Entity;
using System.Linq;
using System.Net;
using System.Web;
using System.Web.Mvc;
using Con_Autenticacion.Models;
using Microsoft.AspNet.Identity;

namespace Con_Autenticacion.Controllers
{
    // Controlador único del área administrativa (pantallas 17 a 21).
    // Cada acción valida el rol en el servidor (RNP-09, RG-38).
    [Authorize]
    public class AdminController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        // Administrador o SuperUsuario
        private bool EsAdministrativo()
        {
            return User.IsInRole("Administrador") || User.IsInRole("SuperUsuario");
        }

        // =========================================================================
        // PANTALLA 17: PANEL DEL ADMINISTRADOR (/Admin/Index)
        // =========================================================================
        [HttpGet]
        public ActionResult Index()
        {
            if (!EsAdministrativo())
            {
                ViewBag.SinPermiso = true;
                return View();
            }

            ViewBag.SinPermiso = false;
            ViewBag.EsSuperUsuario = User.IsInRole("SuperUsuario");
            return View();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                db.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}