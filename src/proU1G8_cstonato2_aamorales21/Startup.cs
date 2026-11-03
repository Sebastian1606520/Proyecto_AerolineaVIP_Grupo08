using Microsoft.Owin;
using Owin;

[assembly: OwinStartupAttribute(typeof(Con_Autenticacion.Startup))]
namespace Con_Autenticacion
{
    public partial class Startup
    {
        public void Configuration(IAppBuilder app)
        {
            ConfigureAuth(app);
        }
    }
}
