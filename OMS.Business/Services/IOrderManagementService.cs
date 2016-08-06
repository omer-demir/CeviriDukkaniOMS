using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.Domain.Dto.Sale;
using Tangent.CeviriDukkani.Domain.Dto.Translation;
using Tangent.CeviriDukkani.Domain.Entities.Sale;

namespace OMS.Business.Services {
    public interface IOrderManagementService {
        ServiceResult<OrderDto> CreateOrder(CreateTranslationOrderRequestDto orderRequest);
        ServiceResult<List<OrderDetailDto>> CreateOrderDetails(List<TranslationOperationDto> translationOperations, int orderId);
        ServiceResult<List<OrderDto>> GetOrders();
        ServiceResult<OrderDto> GetOrderById(int orderId);
        ServiceResult<OrderDto> UpdateOrder(OrderDto order);
        ServiceResult DeactivateOrder(int orderId);
        ServiceResult<List<OrderDto>> GetOrdersByQuery(Expression<Func<Order, bool>> expression);
        ServiceResult<List<OrderDto>> GetResponsePendingOrders();
        ServiceResult AcceptOfferAsTranslator(int translatorId, int translationOperationId, decimal price);
        ServiceResult AcceptOfferAsEditor(int editorId, int translationOperationId, decimal price);
        ServiceResult AcceptOfferAsProofReader(int proofReaderId, int translationOperationId, decimal price);
        ServiceResult UpdateOrderStatus(int translationOperationId, int orderStatusId);
    }
}