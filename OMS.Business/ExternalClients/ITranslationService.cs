using Tangent.CeviriDukkani.Domain.Common;

namespace OMS.Business.ExternalClients {
    public interface ITranslationService {
        ServiceResult GetAverageDocumentPartCount(int orderId);
    }
}