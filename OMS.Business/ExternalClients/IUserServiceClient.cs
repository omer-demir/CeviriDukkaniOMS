using Tangent.CeviriDukkani.Domain.Common;

namespace OMS.Business.ExternalClients {
    public interface IUserServiceClient {
        ServiceResult GetTranslatorsAccordingToOrderTranslationQuality(int orderId);
    }
}