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

            return OK(serviceResult);
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

        /// <summary>
        /// Gets the response pending orders.
        /// </summary>
        /// <returns></returns>
        [HttpGet, Route("getResponsePendingOrders")]
        public HttpResponseMessage GetResponsePendingOrders() {
            var serviceResult = _orderManagementService.GetResponsePendingOrders();
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("getCampaigns")]
        public HttpResponseMessage GetCampaigns() {
            var serviceResult = _orderManagementService.GetCampaigns();
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("getCampaign")]
        public HttpResponseMessage GetCampaign([FromUri] int campaingItemId) {
            var serviceResult = _orderManagementService.GetCampaign(campaingItemId);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        /// <summary>
        /// Updates the campaign.
        /// </summary>
        /// <param name="request">The request.</param>
        /// <returns></returns>
        [HttpPost, Route("updateCampaign")]
        public HttpResponseMessage UpdateCampaign([FromBody]CampaignItemDto request) {
            var serviceResult = _orderManagementService.UpdateCampaign(request);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        /// <summary>
        /// Deletes the campaign.
        /// </summary>
        /// <param name="campaignId">The campaign identifier.</param>
        /// <returns></returns>
        [HttpGet, Route("deleteCampaign")]
        public HttpResponseMessage DeleteCampaign([FromUri] int campaignId) {
            var serviceResult = _orderManagementService.DeleteCampaign(campaignId);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpGet, Route("getOrderDetailsByOrderId")]
        public HttpResponseMessage GetOrderDetailByOrderId([FromUri]int orderId) {
            var serviceResult = _orderManagementService.GetOrderDetailsByOrderId(orderId);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult);
        }

        [HttpPost, Route("acceptOffer")]
        public HttpResponseMessage AcceptOffer([FromBody]AcceptOfferRequestDto request) {
            ServiceResult serviceResult = new ServiceResult();
            switch (request.UserRoleType) {
                case UserRoleTypeEnum.Translator:
                case UserRoleTypeEnum.FreelanceTranslator:
                    serviceResult = _orderManagementService.AcceptOfferAsTranslator(request.BidderId, request.OrderDetailId,
                        request.Price);
                    break;
                case UserRoleTypeEnum.Editor:
                    serviceResult = _orderManagementService.AcceptOfferAsEditor(request.BidderId, request.OrderDetailId,
                        request.Price);
                    break;
                case UserRoleTypeEnum.ProofReader:
                    serviceResult = _orderManagementService.AcceptOfferAsProofReader(request.BidderId, request.OrderDetailId,
                        request.Price);
                    break;
                default:
                    serviceResult.ServiceResultType = ServiceResultType.Fail;
                    serviceResult.Exception = new Exception("Undefined user role");
                    break;
            }

            return serviceResult.ServiceResultType != ServiceResultType.Success ? Error(serviceResult) : OK(serviceResult);
        }
    }
}