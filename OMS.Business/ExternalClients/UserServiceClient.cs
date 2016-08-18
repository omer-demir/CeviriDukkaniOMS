using System;
using System.Collections.Generic;
using System.Configuration;
using System.Net.Http;
using System.Net.Http.Headers;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.System;

namespace OMS.Business.ExternalClients {
    public class UserServiceClient : IUserServiceClient {
        private readonly HttpClient _httpClient;

        public UserServiceClient() {
            var serviceEndpoint = ConfigurationManager.AppSettings["UserServiceEndpoint"];
            _httpClient = new HttpClient {
                BaseAddress = new Uri(serviceEndpoint)
            };
            _httpClient.DefaultRequestHeaders.Accept.Clear();
            _httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        }

        #region Implementation of IUserServiceClient

        public ServiceResult<List<UserDto>> GetTranslatorsAccordingToOrderTranslationQuality(int orderId) {
            var response = _httpClient.GetAsync($"api/userapi/getTranslatorsAccordingToOrderTranslationQuality?orderId={orderId}").Result;
            return response.Content.ReadAsAsync<ServiceResult<List<UserDto>>>().Result;
        }

        public ServiceResult<List<UserDto>> GetEditorsAccordingToOrderTranslationQuality(int orderId) {
            var response = _httpClient.GetAsync($"api/userapi/getEditorAccordingToOrderTranslationQuality?orderId={orderId}").Result;
            return response.Content.ReadAsAsync<ServiceResult<List<UserDto>>>().Result;
        }

        public ServiceResult<List<UserDto>> GetProofReadersAccordingToOrderTranslationQuality(int orderId) {
            var response = _httpClient.GetAsync($"api/userapi/getProofReadersAccordingToOrderTranslationQuality?orderId={orderId}").Result;
            return response.Content.ReadAsAsync<ServiceResult<List<UserDto>>>().Result;
        }

        #endregion
    }
}