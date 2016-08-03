using System;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using OMS.Business.Services;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.WebCore.BaseControllers;
using Tangent.CeviriDukkani.Domain.Dto.Sale;
using Tangent.CeviriDukkani.Domain.Dto.Enums;

namespace OMS.Api.Controllers {
    [RoutePrefix("api/orderapi")]
    public class OrderApiController : BaseApiController {
        private readonly IOrderManagementService _orderManagementService;

        public OrderApiController(IOrderManagementService orderManagementService) {
            _orderManagementService = orderManagementService;
        }

        [HttpPost, Route("createOrderForTranslation")]
        public HttpResponseMessage CreateOrderForTranslation([FromBody] CreateTranslationOrderRequestDto requestDto) {
            var serviceResult = _orderManagementService.CreateOrder(requestDto);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult.Data);
        }

        [HttpGet, Route("getOrders")]
        public HttpResponseMessage GetOrders() {
            var serviceResult = _orderManagementService.GetOrders();
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("getOrderById")]
        public HttpResponseMessage GetOrderById([FromUri]int orderId) {
            var serviceResult = _orderManagementService.GetOrderById(orderId);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpPost, Route("createTranslationOrder")]
        public HttpResponseMessage CreateTranslationOrder([FromBody] CreateTranslationOrderRequestDto request) {
            var serviceResult = _orderManagementService.CreateOrder(request);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpPost, Route("updateTranslationOrder")]
        public HttpResponseMessage UpdateTranslationOrder([FromBody] OrderDto order) {
            var serviceResult = _orderManagementService.UpdateOrder(order);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("deactivateOrder")]
        public HttpResponseMessage DeactivateOrder([FromUri]int orderId) {
            var serviceResult = _orderManagementService.DeactivateOrder(orderId);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("getWaitingOrders")]
        public HttpResponseMessage GetWaitingOrders() {
            var serviceResult = _orderManagementService.GetOrdersByQuery(a => a.OrderStatusId == (int)OrderStatusEnum.Created);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("getResponsePendingOrders")]
        public HttpResponseMessage GetResponsePendingOrders() {
            var serviceResult = _orderManagementService.GetResponsePendingOrders();
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }
    }
}