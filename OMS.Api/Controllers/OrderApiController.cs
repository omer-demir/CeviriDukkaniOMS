using System.Net.Http;
using System.Web.Http;
using OMS.Business.Services;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.WebCore.BaseControllers;

namespace OMS.Api.Controllers {
    [RoutePrefix("api/orderapi")]
    public class OrderApiController : BaseApiController {
        private readonly IOrderManagementService _orderManagementService;

        public OrderApiController(IOrderManagementService orderManagementService) {
            _orderManagementService = orderManagementService;
        }

        [HttpPost,Route("createOrderForTranslation")]
        public HttpResponseMessage CreateOrderForTranslation([FromBody] CreateTranslationOrderRequestDto requestDto) {
            var serviceResult = _orderManagementService.CreateOrder(requestDto);
            if (serviceResult.ServiceResultType != ServiceResultType.Success) {
                return Error(serviceResult);
            }

            return OK(serviceResult.Data);
        }
    }
}