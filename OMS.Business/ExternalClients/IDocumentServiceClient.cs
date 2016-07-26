using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Document;

namespace OMS.Business.ExternalClients {
    public interface IDocumentServiceClient {
        ServiceResult CreateDocument(TranslationDocumentDto documentDto);
    }
}