using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using OMS.Business.ExternalClients;
using Tangent.CeviriDukkani.Data.Model;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Document;
using Tangent.CeviriDukkani.Domain.Dto.Enums;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.Domain.Dto.Sale;
using Tangent.CeviriDukkani.Domain.Dto.Translation;
using Tangent.CeviriDukkani.Domain.Entities.Sale;
using Tangent.CeviriDukkani.Domain.Exceptions;
using Tangent.CeviriDukkani.Domain.Exceptions.ExceptionCodes;
using Tangent.CeviriDukkani.Domain.Mappers;
using Tangent.CeviriDukkani.Event.DocumentEvents;
using Tangent.CeviriDukkani.Messaging;
using Tangent.CeviriDukkani.Messaging.Producer;

namespace OMS.Business.Service {
    public class OrderManagementService : IOrderManagementService {
        internal const decimal VatAmount = 0.18M;
        private readonly CeviriDukkaniModel _model;
        private readonly CustomMapperConfiguration _mapper;
        private readonly IDispatchCommits _dispatcher;
        private readonly IDocumentServiceClient _documentServiceClient;


        public OrderManagementService(CeviriDukkaniModel model, CustomMapperConfiguration mapper, IDispatchCommits dispatcher, IDocumentServiceClient documentServiceClient) {
            _model = model;
            _mapper = mapper;
            _dispatcher = dispatcher;
            _documentServiceClient = documentServiceClient;
        }

        #region Implementation of IOrderService

        public ServiceResult CreateOrder(CreateTranslationOrderRequestDto orderRequest) {
            var serviceResult = new ServiceResult(ServiceResultType.NotKnown);
            try {

                var newOrder = new Order {
                    SourceLanguageId = orderRequest.SourceLanguageId,
                    TerminologyId = orderRequest.TerminologyId,
                    CompanyTerminologyId = orderRequest.CompanyTerminologyId,
                    CompanyDocumentTemplateId = orderRequest.CompanyDocumentTemplateId,
                    CustomerId = orderRequest.CustomerId,
                    OrderStatusId = (int)OrderStatusEnum.Created,
                    TranslationQualityId = orderRequest.TranslationQualityId
                };

                var translationDocument = CreateTranslationDocumentForOrder(orderRequest);
                newOrder.TranslationDocumentId = translationDocument.Id;

                var orderPrice = CalculatOrderPrice(orderRequest);
                newOrder.CalculatedPrice = orderPrice;
                newOrder.VatPrice = orderPrice * VatAmount;

                _model.Orders.Add(newOrder);

                var saveResult = _model.SaveChanges() > 0;
                if (!saveResult) {
                    serviceResult.Message = "Unable to save order";
                    throw new BusinessException(ExceptionCodes.UnableToSave);
                }

                var createDocumentPartEvent = new CreateDocumentPartEvent {
                    TranslationDocumentId = newOrder.TranslationDocumentId,
                    TranslationQualityId = newOrder.TranslationQualityId
                };

                _dispatcher.Dispatch(new List<EventMessage> {
                    createDocumentPartEvent.ToEventMessage()
                });

                serviceResult.ServiceResultType = ServiceResultType.Success;
                serviceResult.Data = _mapper.GetMapDto<OrderDto, Order>(newOrder);
            } catch (Exception exc) {
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult CreateOrderDetails(List<TranslationOperationDto> translationOperations, int orderId) {
            var serviceResult = new ServiceResult(ServiceResultType.NotKnown);
            try {
                var order = _model.Orders.FirstOrDefault(a => a.Id == orderId);
                if (order == null) {
                    serviceResult.Message = $"There is no related order with id {orderId}";
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                order.OrderDetails = translationOperations.Select(a => new OrderDetail {
                    AveragePrice = 1,
                    TranslationOperationId = a.Id,
                    OfferedPrice = 1
                }).ToList();

                _model.Entry(order).State = EntityState.Modified;
                var updateResult = _model.SaveChanges() > 0;
                if (!updateResult) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = order.OrderDetails.Select(a => _mapper.GetMapDto<OrderDetailDto, OrderDetail>(a));
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        #endregion

        private decimal CalculatOrderPrice(CreateTranslationOrderRequestDto orderRequest) {
            var translationUnitPriceList =
                _model.PriceLists.Where(
                    a =>
                        a.SourceLanguageId == orderRequest.SourceLanguageId &&
                        orderRequest.TargetLanguagesIds.Any(b => b == a.TargetLanguageId));

            var orderPrice = 0.0M;

            if (orderRequest.CharCountWithSpaces < 100) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_0_100).Sum();
            } else if (orderRequest.CharCountWithSpaces < 150) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_100_150).Sum();
            } else if (orderRequest.CharCountWithSpaces < 200) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_150_200).Sum();
            } else if (orderRequest.CharCountWithSpaces < 500) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_200_500).Sum();
            } else {
                orderPrice = translationUnitPriceList.Select(a => a.Char_500_More).Sum();
            }

            return orderPrice * orderRequest.CharCountWithSpaces;

        }

        private TranslationDocumentDto CreateTranslationDocumentForOrder(CreateTranslationOrderRequestDto orderRequest) {
            var newTranslationDocument = new TranslationDocumentDto {
                CharCount = orderRequest.CharCount,
                CharCountWithSpaces = orderRequest.CharCountWithSpaces,
                PageCount = orderRequest.PageCount,
                Path = orderRequest.TranslationDocumentPath
            };

            var translationDocumentSaveResult = _documentServiceClient.CreateDocument(newTranslationDocument);
            if (translationDocumentSaveResult.ServiceResultType != ServiceResultType.Success) {
                throw new BusinessException(ExceptionCodes.UnableToInsert);
            }

            var translationDocument = translationDocumentSaveResult.Data as TranslationDocumentDto;
            return translationDocument;
        }
    }
}