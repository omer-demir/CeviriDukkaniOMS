using System;
using System.Configuration;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Integration.WebApi;
using Microsoft.Owin.Hosting;
using OMS.Business.ExternalClients;
using OMS.Business.Service;
using RabbitMQ.Client;
using Tangent.CeviriDukkani.Data.Model;
using Tangent.CeviriDukkani.Domain.Mappers;
using Tangent.CeviriDukkani.Messaging;
using Tangent.CeviriDukkani.Messaging.Producer;

namespace OMS.Api {
    class Program {
        static void Main(string[] args) {
            string baseAddress = "http://localhost:8000/";
            Bootstrapper();

            var webApp = WebApp.Start<Startup>(url: baseAddress);
            Container.Resolve<OmsEventProjection>().Start();
            Console.ReadLine();

            Container.Resolve<IConnection>().Close();
        }

        public static void Bootstrapper() {
            var builder = new ContainerBuilder();
            builder.RegisterCommons();

            var settings = builder.RegisterSettings();
            builder.RegisterEvents(settings);
            builder.RegisterApiControllers(Assembly.GetExecutingAssembly());


            builder.RegisterType<OmsEventProjection>().AsSelf().SingleInstance();

            Container = builder.Build();
        }

        public static IContainer Container { get; set; }
    }

    public static class AutofacExtensions {
        public static void RegisterCommons(this ContainerBuilder builder) {

            builder.RegisterType<CeviriDukkaniModel>().AsSelf().InstancePerLifetimeScope();
            builder.RegisterType<CustomMapperConfiguration>().As<ICustomMapperConfiguration>().InstancePerLifetimeScope();
        }

        public static void RegisterBusiness(this ContainerBuilder builder) {
            builder.RegisterType<IDocumentServiceClient>().As<DocumentServiceClient>().InstancePerLifetimeScope();
            builder.RegisterType<OrderManagementService>().As<IOrderManagementService>().InstancePerLifetimeScope();
        }

        public static void RegisterEvents(this ContainerBuilder builder, Settings settings) {
            var connection = new RabbitMqConnectionFactory(settings.RabbitHost, settings.RabbitPort, settings.RabbitUserName, settings.RabbitPassword).CreateConnection();
            var dispatcher = new RabbitMqDispatcherFactory(connection, settings.RabbitExchangeName).CreateDispatcher();

            builder.RegisterInstance<IConnection>(connection);
            builder.RegisterInstance<IDispatchCommits>(dispatcher);
        }

        public static Settings RegisterSettings(this ContainerBuilder builder) {
            var settings = new Settings {
                RabbitExchangeName = ConfigurationManager.AppSettings["RabbitExchangeName"],
                RabbitHost = ConfigurationManager.AppSettings["RabbitHost"],
                RabbitPassword = ConfigurationManager.AppSettings["RabbitPassword"],
                RabbitPort = int.Parse(ConfigurationManager.AppSettings["RabbitPort"]),
                RabbitUserName = ConfigurationManager.AppSettings["RabbitUserName"],
                DocumentServiceEndpoint = ConfigurationManager.AppSettings["DocumentServiceEndpoint"]
            };

            builder.RegisterInstance(settings);
            return settings;
        }
    }
}
