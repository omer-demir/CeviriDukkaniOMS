using System;
using OMS.Business.Services;
using RabbitMQ.Client;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Event.DocumentEvents;
using Tangent.CeviriDukkani.Event.OrderEvents;
using Tangent.CeviriDukkani.Messaging.Consumer;

namespace OMS.Api {
    public class OmsEventProjection {
        private readonly IOrderManagementService _orderManagementService;
        private readonly RabbitMqSubscription _consumer;

        public OmsEventProjection(IConnection connection, IOrderManagementService orderManagementService) {
            _orderManagementService = orderManagementService;
            _consumer = new RabbitMqSubscription(connection, "Cev-Exchange");
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