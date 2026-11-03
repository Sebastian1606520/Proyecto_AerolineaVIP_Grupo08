using System;
using System.Collections.Generic;
using System.Linq;
using System.Web;
using System.Web.Mvc;
using Con_Autenticacion.Models;

namespace Con_Autenticacion.Controllers
{
    public class HomeController : Controller
    {
        private AerolineaVIP_Context db = new AerolineaVIP_Context();

        public ActionResult Index()
        {
            var aeropuertos = db.Aeropuerto
                .Where(a => a.Activo)
                .OrderBy(a => a.Localidad)
                .Select(a => new
                {
                    Id = a.AeropuertoId,
                    Texto = a.Localidad + " (" + a.CodigoIATA + ")",
                    a.CodigoIATA
                })
                .ToList();

            var quito = aeropuertos.FirstOrDefault(a => a.CodigoIATA == "UIO");

            ViewBag.Origenes = new SelectList(aeropuertos, "Id", "Texto", quito != null ? (object)quito.Id : null);
            ViewBag.Destinos = new SelectList(aeropuertos, "Id", "Texto");

            return View();
        }

        public ActionResult About()
        {
            ViewBag.Message = "Your application description page.";
            return View();
        }

        public ActionResult Contact()
        {
            ViewBag.Message = "Your contact page.";
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