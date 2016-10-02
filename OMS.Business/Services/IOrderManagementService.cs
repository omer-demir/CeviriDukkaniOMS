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
        ServiceResult<TranslatingOrderDto> CreateOrder(CreateTranslationOrderRequestDto orderRequest);
        ServiceResult<List<OrderDetailDto>> CreateOrderDetails(List<TranslationOperationDto> translationOperations, int orderId);
        ServiceResult<List<TranslatingOrderDto>> GetOrders();
        ServiceResult<TranslatingOrderDto> GetOrderById(int orderId);
        ServiceResult<TranslatingOrderDto> UpdateOrder(TranslatingOrderDto order);
        ServiceResult DeactivateOrder(int orderId);
        ServiceResult<List<TranslatingOrderDto>> GetOrdersByQuery(Expression<Func<TranslatingOrder, bool>> expression);
        ServiceResult<List<TranslatingOrderDto>> GetResponsePendingOrders();
        ServiceResult AcceptOfferAsTranslator(int translatorId, int orderDetailId, decimal? price);
        ServiceResult AcceptOfferAsEditor(int editorId, int orderDetailId, decimal? price);
        ServiceResult AcceptOfferAsProofReader(int proofReaderId, int orderDetailId, decimal? price);
        ServiceResult UpdateOrderStatus(int translationOperationId, int orderStatusId);
        ServiceResult<List<CampaignItemDto>> GetCampaigns();
        ServiceResult<CampaignItemDto> GetCampaign(int campaingItemId);
        ServiceResult<CampaignItemDto> UpdateCampaign(CampaignItemDto campaignItem);
        ServiceResult DeleteCampaign(int campaingItemId);
        ServiceResult<List<OrderDetailDto>> GetOrderDetailsByOrderId(int orderId);

    }
}