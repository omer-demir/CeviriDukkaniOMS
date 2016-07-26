using System.Collections.Generic;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.Domain.Dto.Translation;

namespace OMS.Business.Service {
    public interface IOrderManagementService {
        ServiceResult CreateOrder(CreateTranslationOrderRequestDto orderRequest);
        ServiceResult CreateOrderDetails(List<TranslationOperationDto> translationOperations, int orderId);
    }
}