using System.Web.Http;
using Autofac.Integration.WebApi;
using Owin;

namespace OMS.Api {
    public class Startup {
        // This code configures Web API. The Startup class is specified as a type
        // parameter in the WebApp.Start method.
        public void Configuration(IAppBuilder appBuilder) {
            // Configure Web API for self-host. 
            HttpConfiguration config = new HttpConfiguration();
            config.MapHttpAttributeRoutes();


            config.DependencyResolver = new AutofacWebApiDependencyResolver(Program.Container);

            appBuilder.UseWebApi(config);
        }
    }
}