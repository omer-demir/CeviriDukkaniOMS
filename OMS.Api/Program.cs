using System;
using System.Configuration;
using System.IO;
using System.Linq;
using System.Reflection;
using Autofac;
using Autofac.Integration.WebApi;
using log4net;
using Microsoft.Owin.Hosting;
using OMS.Business.ExternalClients;
using OMS.Business.Services;
using RabbitMQ.Client;
using Tangent.CeviriDukkani.Data.Model;
using Tangent.CeviriDukkani.Domain.Mappers;
using Tangent.CeviriDukkani.Logging;
using Tangent.CeviriDukkani.Messaging;
using Tangent.CeviriDukkani.Messaging.Producer;

namespace OMS.Api {
    public class Program {
        static void Main(string[] args) {
            string baseAddress = "http://localhost:8002/";

            Bootstrapper();
            Console.WriteLine("Bootstrapper finished");

            var webApp = WebApp.Start<Startup>(url: baseAddress);
            Console.WriteLine($"OMS is ready in {baseAddress}");

            Container.Resolve<OmsEventProjection>().Start();
            Console.WriteLine("Projection started...");
            Console.ReadLine();

            Console.WriteLine("Starting to close OMS...");

            CustomLogger.Logger.Info($"OMS service is down with projections {DateTime.Today}");

            Container.Resolve<IConnection>().Close();
        }

        public static void Bootstrapper() {
            var builder = new ContainerBuilder();
            builder.RegisterCommons();
            builder.RegisterBusiness();

            var settings = builder.RegisterSettings();
            builder.RegisterEvents(settings);
            builder.RegisterApiControllers(Assembly.GetExecutingAssembly());

            builder.RegisterType<OmsEventProjection>().AsSelf().SingleInstance();

            Container = builder.Build();
            CustomLogger.Logger.Info($"OMS service is up and ready with projections {DateTime.Today}");
        }

        public static IContainer Container { get; set; }
    }

    public static class AutofacExtensions {
        public static void RegisterCommons(this ContainerBuilder builder) {

            builder.RegisterType<CeviriDukkaniModel>().AsSelf().InstancePerLifetimeScope();
            builder.RegisterType<CustomMapperConfiguration>().AsSelf().SingleInstance();
            builder.RegisterInstance(CustomLogger.Logger).As<ILog>().SingleInstance();
        }

        public static void RegisterBusiness(this ContainerBuilder builder) {
            builder.RegisterType<DocumentServiceClient>().As<IDocumentServiceClient>().InstancePerLifetimeScope();
            builder.RegisterType<UserServiceClient>().As<IUserServiceClient>().InstancePerLifetimeScope();
            builder.RegisterType<TranslationService>().As<ITranslationService>().InstancePerLifetimeScope();
            builder.RegisterType<OrderManagementService>().As<IOrderManagementService>().InstancePerLifetimeScope();
        }

        public static void RegisterEvents(this ContainerBuilder builder, Settings settings) {
            var connection = new RabbitMqConnectionFactory(settings.RabbitHost, settings.RabbitPort, settings.RabbitUserName, settings.RabbitPassword).CreateConnection();
            var dispatcher = new RabbitMqDispatcherFactory(connection, settings.RabbitExchangeName,CustomLogger.Logger).CreateDispatcher();

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
