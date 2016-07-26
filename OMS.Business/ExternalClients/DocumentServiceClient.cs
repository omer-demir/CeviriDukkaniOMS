using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Document;

namespace OMS.Business.ExternalClients {
    public class DocumentServiceClient:IDocumentServiceClient {
        private readonly string _documentServiceEndpoint;
        private HttpClient _httpClient;

        public DocumentServiceClient() {
            _documentServiceEndpoint = ConfigurationManager.AppSettings["DocumentServiceEndpoint"];
            _httpClient = new HttpClient {
                BaseAddress = new Uri(_documentServiceEndpoint)
            };
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        public ServiceResult CreateDocument(TranslationDocumentDto documentDto) {
            var response = _httpClient.PostAsJsonAsync("api/documentapi/createTranslationDocument", documentDto).Result;
            return response.Content.ReadAsAsync<ServiceResult>().Result;

        }
    }
}