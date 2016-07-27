using System;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using Tangent.CeviriDukkani.Domain.Common;

namespace OMS.Business.ExternalClients {
    public class TranslationService : ITranslationService {
        private readonly HttpClient _httpClient;

        public TranslationService() {
            var serviceEndpoint = ConfigurationManager.AppSettings["TranslationServiceEndpoint"];
            _httpClient = new HttpClient {
                BaseAddress = new Uri(serviceEndpoint)
            };
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        #region Implementation of ITranslationService

        public ServiceResult GetAverageDocumentPartCount(int orderId) {
            var response = _httpClient.GetAsync($"api/translationapi/getAverageDocumentPartCount?orderId={orderId}").Result;
            return response.Content.ReadAsAsync<ServiceResult>().Result;
        }

        #endregion
    }
}