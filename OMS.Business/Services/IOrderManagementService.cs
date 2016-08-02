using System.Collections.Generic;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.Domain.Dto.Sale;
using Tangent.CeviriDukkani.Domain.Dto.Translation;

namespace OMS.Business.Services {
    public interface IOrderManagementService {
        ServiceResult<OrderDto> CreateOrder(CreateTranslationOrderRequestDto orderRequest);
        ServiceResult<List<OrderDetailDto>> CreateOrderDetails(List<TranslationOperationDto> translationOperations, int orderId);
        ServiceResult<List<OrderDto>> GetOrders();
        ServiceResult<OrderDto> GetOrderById(int orderId);
        ServiceResult<OrderDto> UpdateOrder(OrderDto order);
        ServiceResult DeactivateOrder(int orderId);
    }
}