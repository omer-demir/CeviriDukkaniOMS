using System.Collections.Generic;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.System;

namespace OMS.Business.ExternalClients {
    public interface IUserServiceClient {
        ServiceResult<List<UserDto>> GetTranslatorsAccordingToOrderTranslationQuality(int orderId);
        ServiceResult<List<UserDto>> GetEditorsAccordingToOrderTranslationQuality(int orderId);
        ServiceResult<List<UserDto>> GetProofReadersAccordingToOrderTranslationQuality(int orderId);
    }
}