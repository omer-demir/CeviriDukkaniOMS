using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using log4net;
using LinqKit;
using Newtonsoft.Json;
using OMS.Business.ExternalClients;
using Tangent.CeviriDukkani.Data.Model;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Document;
using Tangent.CeviriDukkani.Domain.Dto.Enums;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.Domain.Dto.Sale;
using Tangent.CeviriDukkani.Domain.Dto.System;
using Tangent.CeviriDukkani.Domain.Dto.Translation;
using Tangent.CeviriDukkani.Domain.Entities.Sale;
using Tangent.CeviriDukkani.Domain.Exceptions;
using Tangent.CeviriDukkani.Domain.Exceptions.ExceptionCodes;
using Tangent.CeviriDukkani.Domain.Mappers;
using Tangent.CeviriDukkani.Event.DocumentEvents;
using Tangent.CeviriDukkani.Event.MailEvents;
using Tangent.CeviriDukkani.Logging;
using Tangent.CeviriDukkani.Messaging;
using Tangent.CeviriDukkani.Messaging.Producer;

namespace OMS.Business.Services {
    public class OrderManagementService : IOrderManagementService {
        internal const decimal VatAmount = 0.18M;
        private readonly CeviriDukkaniModel _model;
        private readonly CustomMapperConfiguration _mapper;
        private readonly IDispatchCommits _dispatcher;
        private readonly IDocumentServiceClient _documentServiceClient;
        private readonly ITranslationService _translationService;
        private readonly ILog _logger;
        private readonly IUserServiceClient _userServiceClient;


        public OrderManagementService(CeviriDukkaniModel model,
            CustomMapperConfiguration mapper,
            IDispatchCommits dispatcher,
            IDocumentServiceClient documentServiceClient,
            ITranslationService translationService,
            ILog logger,
            IUserServiceClient userServiceClient) {
            _model = model;
            _mapper = mapper;
            _dispatcher = dispatcher;
            _documentServiceClient = documentServiceClient;
            _translationService = translationService;
            _logger = logger;
            _userServiceClient = userServiceClient;
        }

        #region Implementation of IOrderService

        public ServiceResult<TranslatingOrderDto> CreateOrder(CreateTranslationOrderRequestDto orderRequest) {
            var serviceResult = new ServiceResult<TranslatingOrderDto>();
            try {
                _logger.Info($"New order creation request obtained {DateTime.Now.ToString("d")}");

                var newOrder = new TranslatingOrder() {
                    SourceLanguageId = orderRequest.SourceLanguageId,
                    TerminologyId = orderRequest.TerminologyId,
                    CompanyTerminologyId = orderRequest.CompanyTerminologyId,
                    CompanyDocumentTemplateId = orderRequest.CompanyDocumentTemplateId,
                    CustomerId = orderRequest.CustomerId,
                    OrderStatusId = (int)OrderStatusEnum.Created,
                    TranslationQualityId = orderRequest.TranslationQualityId
                };

                CalculatePotentialDeliveryDate(newOrder, orderRequest);

                var translationDocument = CreateTranslationDocumentForOrder(orderRequest);
                newOrder.TranslationDocumentId = translationDocument.Id;

                SetOrderPrices(orderRequest, newOrder);

                _logger.Info("Order creating...");
                _model.TranslatingOrders.Add(newOrder);

                var saveResult = _model.SaveChanges() > 0;
                if (!saveResult) {
                    serviceResult.Message = "Unable to save order";
                    throw new BusinessException(ExceptionCodes.UnableToSave);
                }

                if (!string.IsNullOrEmpty(orderRequest.CampaignItemCode)) {
                    UpdateCampaignItemAsUsed(orderRequest.CampaignItemCode);
                }


                //translation service call edilecek
                var averageDocumentPartCount = _translationService.GetAverageDocumentPartCount(newOrder.Id);
                if (averageDocumentPartCount.ServiceResultType != ServiceResultType.Success) {
                    throw new Exception("Unable to retrieve related translators and average document part count");
                }


                var createDocumentPartEvent = new CreateDocumentPartEvent {
                    TranslationDocumentId = translationDocument.Id,
                    PartCount = Int32.Parse(averageDocumentPartCount.Data.ToString()),
                    CreatedBy = 0,
                    Id = Guid.NewGuid(),
                    OrderId = newOrder.Id
                };

                _logger.Info($"CreateDocumentPartEvent is firing with order Id {newOrder.Id}");

                _dispatcher.Dispatch(new List<EventMessage> {
                    createDocumentPartEvent.ToEventMessage()
                });

                serviceResult.ServiceResultType = ServiceResultType.Success;
                serviceResult.Data = _mapper.GetMapDto<TranslatingOrderDto, TranslatingOrder>(newOrder);
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");

                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<OrderDetailDto>> CreateOrderDetails(List<TranslationOperationDto> translationOperations, int orderId) {
            var serviceResult = new ServiceResult<List<OrderDetailDto>>();
            try {
                var order = _model.TranslatingOrders.FirstOrDefault(a => a.Id == orderId);
                if (order == null) {
                    serviceResult.Message = $"There is no related order with id {orderId}";
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var createdOrderDetails = translationOperations.Select(a => {
                    var calculatedPrice = CalculateDocumentPartPrice(new CreateTranslationOrderRequestDto {
                        CharCountWithSpaces = a.DocumentPart.CharCountWithSpaces,
                        CharCount = a.DocumentPart.CharCount,
                        SourceLanguageId = order.SourceLanguageId,
                        TargetLanguagesIds = order.TargetLanguages.Select(b => b.Id).ToList(),
                        TerminologyId = order.TerminologyId,
                    });

                    return new OrderDetail {
                        AveragePrice = calculatedPrice,
                        OfferedPrice = calculatedPrice,
                        EditorAveragePrice = calculatedPrice,
                        EditorOfferedPrice = calculatedPrice,
                        ProofReaderAveragePrice = calculatedPrice,
                        ProofReaderOfferedPrice = calculatedPrice,
                        TranslatingOrderId = orderId,
                        CreatedBy = 1,
                        TranslationOperationId = a.Id,
                        CreatedAt = DateTime.Now
                    };
                });

                order.OrderDetails = createdOrderDetails.ToList();

                _model.Entry(order).State = EntityState.Modified;
                var updateResult = _model.SaveChanges() > 0;
                if (!updateResult) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                var relatedTranslatorsResult = _userServiceClient.GetTranslatorsAccordingToOrderTranslationQuality(orderId);
                var relatedTranslators = relatedTranslatorsResult.Data;

                var relatedEditorsResult = _userServiceClient.GetEditorsAccordingToOrderTranslationQuality(orderId);
                var relatedEditors = relatedEditorsResult.Data;

                var relatedProofReadersResult = _userServiceClient.GetProofReadersAccordingToOrderTranslationQuality(orderId);
                var relatedProofReaders = relatedProofReadersResult.Data;

                //TODO change this
                //var sendMailEvent = new SendMailEvent {
                //    Id = Guid.NewGuid(),
                //    MailSender = MailSenderTypeEnum.System,
                //    CreatedBy = 1,
                //    Message = "Notify translators for new job",
                //    Subject = "New Document Ready to Work on!",
                //    To = relatedTranslators.Select(a => a.Email).ToList()
                //};

                //_dispatcher.Dispatch(new List<EventMessage> {
                //    sendMailEvent.ToEventMessage()
                //});

                serviceResult.Data = order.OrderDetails.Select(a => _mapper.GetMapDto<OrderDetailDto, OrderDetail>(a)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<TranslatingOrderDto>> GetOrders() {
            var serviceResult = new ServiceResult<List<TranslatingOrderDto>>();
            try {
                var orderList = _model.TranslatingOrders
                    .Include(a => a.OrderDetails)
                    .Include(a => a.SourceLanguage)
                    .Include(a => a.TranslationDocument)
                    .Include(a=>a.TargetLanguages.Select(b=>b.Language))
                    .Include(a => a.Customer.Company)
                    .Include(a => a.TranslationQuality)
                    .Include(a => a.Terminology)
                    .Include(a => a.OrderStatus).ToList();
                serviceResult.Data = orderList.Select(a => _mapper.GetMapDto<TranslatingOrderDto, TranslatingOrder>(a)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<TranslatingOrderDto> GetOrderById(int orderId) {
            var serviceResult = new ServiceResult<TranslatingOrderDto>();
            try {
                var order = _model.TranslatingOrders
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.Editor))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.Translator))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.ProofReader))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.TranslationOperationStatus))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.TranslationProgressStatus))
                    .Include(a => a.OrderStatus)
                    .Include(a => a.Customer.Company)
                    .Include(a => a.Customer.MembershipType)
                    .Include(a => a.CompanyTerminology)
                    .Include(a => a.SourceLanguage)
                    .Include(a => a.TargetLanguages.Select(b => b.Language))
                    .Include(a => a.Terminology)
                    .Include(a => a.TranslationQuality)
                    .Include(a => a.CampaignItem)
                    .FirstOrDefault(a => a.Id == orderId);
                if (order == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }
                serviceResult.Data = _mapper.GetMapDto<TranslatingOrderDto, TranslatingOrder>(order);
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<TranslatingOrderDto> UpdateOrder(TranslatingOrderDto orderDto) {
            var serviceResult = new ServiceResult<TranslatingOrderDto>();
            try {
                var order = _model.TranslatingOrders.Include(a => a.OrderDetails).Include(a => a.OrderStatus).FirstOrDefault(a => a.Id == orderDto.Id);
                if (order == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }
                serviceResult.Data = _mapper.GetMapDto<TranslatingOrderDto, TranslatingOrder>(order);
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult DeactivateOrder(int orderId) {
            var serviceResult = new ServiceResult(ServiceResultType.NotKnown);
            try {
                var order = _model.TranslatingOrders.Include(a => a.OrderDetails).Include(a => a.OrderStatus).FirstOrDefault(a => a.Id == orderId);
                if (order == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }
                order.Active = false;

                _model.Entry(order).State = EntityState.Modified;
                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = true;
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<TranslatingOrderDto>> GetOrdersByQuery(Expression<Func<TranslatingOrder, bool>> expression) {
            var serviceResult = new ServiceResult<List<TranslatingOrderDto>>();
            try {
                var orders = _model.TranslatingOrders.Where(expression).ToList();
                serviceResult.Data = orders.Select(a => _mapper.GetMapDto<TranslatingOrderDto, TranslatingOrder>(a)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<TranslatingOrderDto>> GetResponsePendingOrders() {
            var serviceResult = new ServiceResult<List<TranslatingOrderDto>>();
            try {
                var orderDetails =
                    _model.OrderDetails.Include(a => a.TranslatingOrder)
                        .Include(a => a.TranslationOperation)
                        .Where(
                            a =>
                                a.TranslationOperation.TranslationProgressStatusId == (int)TranslationProgressStatusEnum.Open);

                serviceResult.Data = orderDetails.Select(a => _mapper.GetMapDto<TranslatingOrderDto, TranslatingOrder>(a.TranslatingOrder)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult AcceptOfferAsTranslator(int translatorId, int orderDetailId, decimal? price) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail =
                    _model.OrderDetails.Include(a => a.TranslatingOrder)
                        .Include(a => a.TranslationOperation)
                        .FirstOrDefault(a => a.TranslationOperation.TranslatorId == translatorId && a.Id == orderDetailId);

                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var translationOperation = orderDetail.TranslationOperation;
                var order = orderDetail.TranslatingOrder;

                if (translationOperation.TranslationProgressStatusId != (int)TranslationProgressStatusEnum.Open) {
                    _logger.Error($"TranslationOperation {translationOperation.Id} is taken or not ready to be translated");
                    throw new Exception("Translation operation started by another user.");
                }

                if (translationOperation.TranslatorId != translatorId) {
                    _logger.Error($"TranslationOperation {translationOperation.Id} is taken by another user {translationOperation.TranslatorId} and your id {translatorId}");
                    throw new Exception("Translation operation started by another user.");
                }

                translationOperation.TranslatorId = translatorId;
                translationOperation.TranslationProgressStatusId = (int)TranslationProgressStatusEnum.TranslatorStarted;
                _model.Entry(translationOperation).State = EntityState.Modified;

                orderDetail.AcceptedPrice = price;
                _model.Entry(orderDetail).State = EntityState.Modified;

                order.OrderStatusId = (int)OrderStatusEnum.InProcess;
                _model.Entry(order).State = EntityState.Modified;

                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = true;
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult AcceptOfferAsEditor(int editorId, int orderDetailId, decimal? price) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail =
                    _model.OrderDetails.Include(a => a.TranslatingOrder)
                        .Include(a => a.TranslationOperation)
                        .FirstOrDefault(a => a.TranslationOperation.EditorId == editorId && a.Id == orderDetailId);

                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var translationOperation = orderDetail.TranslationOperation;
                var order = orderDetail.TranslatingOrder;

                translationOperation.EditorId = editorId;
                translationOperation.TranslationProgressStatusId = (int)TranslationProgressStatusEnum.EditorStarted;
                _model.Entry(translationOperation).State = EntityState.Modified;

                orderDetail.EditorAcceptedPrice = price;
                _model.Entry(orderDetail).State = EntityState.Modified;

                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = true;
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult AcceptOfferAsProofReader(int proofReaderId, int orderDetailId, decimal? price) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail =
                    _model.OrderDetails.Include(a => a.TranslatingOrder)
                        .Include(a => a.TranslationOperation)
                        .FirstOrDefault(a => a.TranslationOperation.ProofReaderId == proofReaderId && a.Id == orderDetailId);

                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var translationOperation = orderDetail.TranslationOperation;
                var order = orderDetail.TranslatingOrder;

                translationOperation.ProofReaderId = proofReaderId;
                translationOperation.TranslationProgressStatusId = (int)TranslationProgressStatusEnum.ProofReaderStarted;
                _model.Entry(translationOperation).State = EntityState.Modified;

                orderDetail.ProofReaderAcceptedPrice = price;
                _model.Entry(orderDetail).State = EntityState.Modified;

                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = true;
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult UpdateOrderStatus(int translationOperationId, int orderStatusId) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail = _model.OrderDetails.FirstOrDefault(a => a.TranslationOperationId == translationOperationId);
                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var order = _model.TranslatingOrders.FirstOrDefault(a => a.Id == orderDetail.TranslatingOrderId);
                if (order == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }
                order.OrderStatusId = orderStatusId;
                _model.Entry(order).State = EntityState.Modified;

                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = true;
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<CampaignItemDto>> GetCampaigns() {
            var serviceResult = new ServiceResult<List<CampaignItemDto>>();
            try {
                var campaigns = _model.CampaignItems.ToList();

                serviceResult.Data = campaigns.Select(a => _mapper.GetMapDto<CampaignItemDto, CampaignItem>(a)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<CampaignItemDto> GetCampaign(int campaingItemId) {
            var serviceResult = new ServiceResult<CampaignItemDto>();
            try {
                var campaign = _model.CampaignItems.FirstOrDefault(a => a.Id == campaingItemId);
                if (campaign == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                serviceResult.Data = _mapper.GetMapDto<CampaignItemDto, CampaignItem>(campaign);
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<CampaignItemDto> UpdateCampaign(CampaignItemDto campaignItem) {
            var serviceResult = new ServiceResult<CampaignItemDto>();
            try {
                var campaign = _model.CampaignItems.FirstOrDefault(a => a.Id == campaignItem.Id);
                if (campaign == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                campaign.Code = campaignItem.Code;
                campaign.DiscountRate = campaignItem.DiscountRate;
                campaign.EndTime = campaignItem.EndTime;
                campaign.StartTime = campaignItem.StartTime;
                campaign.Used = campaignItem.Used;
                campaign.Description = campaignItem.Description;

                _model.Entry(campaign).State = EntityState.Modified;
                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = _mapper.GetMapDto<CampaignItemDto, CampaignItem>(campaign);
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult DeleteCampaign(int campaingItemId) {
            var serviceResult = new ServiceResult();
            try {
                var campaign = _model.CampaignItems.FirstOrDefault(a => a.Id == campaingItemId);
                if (campaign == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                campaign.Active = false;

                _model.Entry(campaign).State = EntityState.Modified;
                var result = _model.SaveChanges() > 0;
                if (!result) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                serviceResult.Data = true;
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<OrderDetailDto>> GetOrderDetailsByOrderId(int orderId) {
            var result = new ServiceResult<List<OrderDetailDto>> { ServiceResultType = ServiceResultType.NotKnown };
            try {
                var orderDetails = _model.OrderDetails
                    .Include(a => a.TranslationOperation.TranslationDocumentPart)
                    .Include(a => a.TranslatingOrder.SourceLanguage)
                    .Include(a => a.TranslatingOrder.TargetLanguages)
                    .Include(a => a.TranslatingOrder.Terminology)
                    .Include(a => a.TranslatingOrder.TranslationQuality)
                    .Where(a => a.TranslatingOrderId == orderId)
                    .ToList();

                result.Data = orderDetails.Select(a => _mapper.GetMapDto<OrderDetailDto, OrderDetail>(a)).ToList();
            } catch (Exception exc) {
                result.Exception = exc;
                result.Message = exc.Message;
                result.ServiceResultType = ServiceResultType.Fail;
            }

            return result;
        }

        #endregion

        private decimal CalculateDocumentPartPrice(CreateTranslationOrderRequestDto orderRequest, decimal? campaingDiscountAmount = null) {
            var translationUnitPriceList =
                _model.PriceLists.Where(
                    a =>
                        a.SourceLanguageId == orderRequest.SourceLanguageId &&
                        orderRequest.TargetLanguagesIds.Any(b => b == a.TargetLanguageId));

            var orderPrice = 0.0M;

            if (orderRequest.CharCount < 100) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_0_100).Sum();
            } else if (orderRequest.CharCount < 150) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_100_150).Sum();
            } else if (orderRequest.CharCount < 200) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_150_200).Sum();
            } else if (orderRequest.CharCount < 500) {
                orderPrice = translationUnitPriceList.Select(a => a.Char_200_500).Sum();
            } else {
                orderPrice = translationUnitPriceList.Select(a => a.Char_500_More).Sum();
            }

            var terminologyRate = _model.TerminologyPriceRate.FirstOrDefault(a => a.TerminologyId == orderRequest.TerminologyId);
            if (terminologyRate == null) {
                throw new Exception($"There is no defined terminology price rate for {orderRequest.TerminologyId}");
            }


            var calculatedPrice = orderPrice * orderRequest.CharCount;
            calculatedPrice += calculatedPrice * terminologyRate.Rate;

            if (campaingDiscountAmount.HasValue) {
                calculatedPrice = calculatedPrice - calculatedPrice * campaingDiscountAmount.Value;
            }

            return calculatedPrice;

        }

        private decimal CalculateOrderPrice(CreateTranslationOrderRequestDto orderRequest, decimal? campaingDiscountAmount = null) {
            var orderPrice = 0.0M;

            decimal? companyFixedPrice = GetCompanyFixedPrice();
            orderPrice = companyFixedPrice ?? GetUnitPriceSum(orderRequest);

            var terminologyRate = _model.TerminologyPriceRate.FirstOrDefault(a => a.TerminologyId == orderRequest.TerminologyId);
            if (terminologyRate == null) {
                throw new Exception($"There is no defined terminology price rate for {orderRequest.TerminologyId}");
            }


            var calculatedPrice = orderPrice * orderRequest.CharCountWithSpaces;
            calculatedPrice += calculatedPrice * terminologyRate.Rate;

            if (campaingDiscountAmount.HasValue) {
                calculatedPrice = calculatedPrice - calculatedPrice * campaingDiscountAmount.Value;
            }

            return calculatedPrice;

        }

        private decimal GetUnitPriceSum(CreateTranslationOrderRequestDto orderRequest) {
            var unitPriceSum = 0.0M;
            var translationUnitPriceList =
                _model.PriceLists.Where(
                    a =>
                        a.SourceLanguageId == orderRequest.SourceLanguageId &&
                        orderRequest.TargetLanguagesIds.Any(b => b == a.TargetLanguageId));

            if (orderRequest.CharCountWithSpaces < 100) {
                unitPriceSum = translationUnitPriceList.Select(a => a.Char_0_100).Sum();
            } else if (orderRequest.CharCountWithSpaces < 150) {
                unitPriceSum = translationUnitPriceList.Select(a => a.Char_100_150).Sum();
            } else if (orderRequest.CharCountWithSpaces < 200) {
                unitPriceSum = translationUnitPriceList.Select(a => a.Char_150_200).Sum();
            } else if (orderRequest.CharCountWithSpaces < 500) {
                unitPriceSum = translationUnitPriceList.Select(a => a.Char_200_500).Sum();
            } else {
                unitPriceSum = translationUnitPriceList.Select(a => a.Char_500_More).Sum();
            }
            return unitPriceSum;
        }

        private decimal? GetCompanyFixedPrice() {
            decimal? price = null;
            var companyPriceOffer = (from companyPrice in _model.CompanyPriceOffers
                                     join company in _model.Companies on companyPrice.CompanyId equals company.Id
                                     join customer in _model.Customers on company.Id equals customer.CompanyId
                                     where companyPrice.Active && company.Active && customer.Active
                                     select companyPrice).FirstOrDefault();
            if (companyPriceOffer != null) {
                if (companyPriceOffer.IsApplicableForCalculation) {
                    //TODO be careful about calculation
                    price = companyPriceOffer.Price;
                }


            }
            return price;
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

            var translationDocument = translationDocumentSaveResult.Data;

            return translationDocument;
        }

        private decimal? GettingCampaignItemFromCode(CreateTranslationOrderRequestDto orderRequest) {
            decimal? campaingDiscountAmount = null;

            if (!string.IsNullOrEmpty(orderRequest.CampaignItemCode)) {
                var campaignDiscount =
                    _model.CampaignItems.FirstOrDefault(a => a.Code == orderRequest.CampaignItemCode && !a.Used && a.StartTime <= DateTime.Now && a.EndTime >= DateTime.Now);
                campaingDiscountAmount = campaignDiscount?.DiscountRate ?? null;
            }
            return campaingDiscountAmount;
        }

        private void UpdateCampaignItemAsUsed(string campaignItemCode) {
            var campaignItem = _model.CampaignItems.FirstOrDefault(a => a.Code == campaignItemCode);
            campaignItem.Used = true;

            _model.Entry(campaignItem).State = EntityState.Modified;
            _model.SaveChanges();
        }

        private void SetOrderPrices(CreateTranslationOrderRequestDto orderRequest, TranslatingOrder newOrder) {
            var campaingDiscountAmount = GettingCampaignItemFromCode(orderRequest);
            var orderPrice = CalculateOrderPrice(orderRequest, campaingDiscountAmount);
            newOrder.CalculatedPrice = orderPrice;
            newOrder.VatPrice = orderPrice * VatAmount;
        }

        private void CalculatePotentialDeliveryDate(TranslatingOrder newOrder, CreateTranslationOrderRequestDto orderRequest) {
            decimal perDay = 8000;
            var characterPerDay = _model.Configurations.FirstOrDefault(a => a.Key == "CharPerDay");
            if (characterPerDay != null) {
                perDay = int.Parse(characterPerDay.Value);
            }

            newOrder.OrderPotentialDeliveryDate = DateTime.Now.AddDays((double)Math.Ceiling(orderRequest.CharCountWithSpaces / perDay));
        }

    }
}