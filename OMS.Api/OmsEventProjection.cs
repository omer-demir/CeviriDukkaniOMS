using System;
using log4net;
using OMS.Business.Services;
using RabbitMQ.Client;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Event.DocumentEvents;
using Tangent.CeviriDukkani.Event.OrderEvents;
using Tangent.CeviriDukkani.Messaging.Consumer;

namespace OMS.Api {
    public class OmsEventProjection {
        private readonly IOrderManagementService _orderManagementService;
        private readonly ILog _logger;
        private readonly RabbitMqSubscription _consumer;

        public OmsEventProjection(IConnection connection, IOrderManagementService orderManagementService,ILog logger) {
            _orderManagementService = orderManagementService;
            _logger = logger;
            _consumer = new RabbitMqSubscription(connection, "Cev-Exchange", _logger);
            _consumer
                .WithAppName("oms-projection")
                .WithEvent<CreateOrderDetailEvent>(Handle);
        }

        public void Start() {
            _consumer.Subscribe();
        }

        public void Stop() {
            _consumer.StopSubscriptionTasks();
        }

        public void Handle(CreateOrderDetailEvent orderDetailEvent) {
            var result = _orderManagementService.CreateOrderDetails(orderDetailEvent.TranslationOperations, orderDetailEvent.OrderId);
            if (result.ServiceResultType != ServiceResultType.Success) {
                Console.WriteLine($"Error occured in {orderDetailEvent.GetType().Name}");
            }
        }
    }
}