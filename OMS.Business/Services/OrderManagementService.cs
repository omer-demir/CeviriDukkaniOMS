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

        public ServiceResult<OrderDto> CreateOrder(CreateTranslationOrderRequestDto orderRequest) {
            var serviceResult = new ServiceResult<OrderDto>();
            try {
                _logger.Info($"New order creation request obtained {DateTime.Now.ToString("d")}");

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

                var campaingDiscountAmount = GettingCampaignItemFromCode(orderRequest);
                var orderPrice = CalculateOrderPrice(orderRequest, orderRequest.TerminologyId, campaingDiscountAmount);
                newOrder.CalculatedPrice = orderPrice;
                newOrder.VatPrice = orderPrice * VatAmount;

                _logger.Info("Order creating...");
                _model.Orders.Add(newOrder);

                var saveResult = _model.SaveChanges() > 0;
                if (!saveResult) {
                    serviceResult.Message = "Unable to save order";
                    throw new BusinessException(ExceptionCodes.UnableToSave);
                }


                UpdateCampaignItemAsUsed(orderRequest.CampaignItemCode);

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
                serviceResult.Data = _mapper.GetMapDto<OrderDto, Order>(newOrder);
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
                var order = _model.Orders.FirstOrDefault(a => a.Id == orderId);
                if (order == null) {
                    serviceResult.Message = $"There is no related order with id {orderId}";
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                //TODO for test purpose
                order.OrderDetails = translationOperations.Select(a => new OrderDetail {
                    AveragePrice = 1,
                    TranslationOperationId = a.Id,
                    OfferedPrice = 1,
                    CreatedBy = 1,
                    OrderId = orderId
                }).ToList();

                _model.Entry(order).State = EntityState.Modified;
                var updateResult = _model.SaveChanges() > 0;
                if (!updateResult) {
                    throw new BusinessException(ExceptionCodes.UnableToUpdate);
                }

                var relatedTranslatorsResult = _userServiceClient.GetTranslatorsAccordingToOrderTranslationQuality(orderId);
                var relatedTranslators = relatedTranslatorsResult.Data;


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

        public ServiceResult<List<OrderDto>> GetOrders() {
            var serviceResult = new ServiceResult<List<OrderDto>>();
            try {
                var orderList = _model.Orders
                    .Include(a => a.OrderDetails)
                    .Include(a => a.SourceLanguage)
                    .Include(a => a.Customer)
                    .Include(a => a.TranslationQuality)
                    .Include(a => a.Terminology)
                    .Include(a => a.OrderStatus).ToList();
                serviceResult.Data = orderList.Select(a => _mapper.GetMapDto<OrderDto, Order>(a)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<OrderDto> GetOrderById(int orderId) {
            var serviceResult = new ServiceResult<OrderDto>();
            try {
                var order = _model.Orders
                    .Include(a => a.OrderDetails.Select(b=>b.TranslationOperation.Editor))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.Translator))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.ProofReader))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.TranslationOperationStatus))
                    .Include(a => a.OrderDetails.Select(b => b.TranslationOperation.TranslationProgressStatus))
                    .Include(a => a.OrderStatus)
                    .Include(a => a.Customer.Company)
                    .Include(a => a.Customer.MembershipType)
                    .Include(a => a.CompanyTerminology)
                    .Include(a => a.SourceLanguage)
                    .Include(a => a.TargetLanguages.Select(b=>b.Language))
                    .Include(a => a.Terminology)
                    .Include(a => a.TranslationQuality)
                    .Include(a => a.CampaignItem)
                    .FirstOrDefault(a => a.Id == orderId);
                if (order == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }
                serviceResult.Data = _mapper.GetMapDto<OrderDto, Order>(order);
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<OrderDto> UpdateOrder(OrderDto orderDto) {
            var serviceResult = new ServiceResult<OrderDto>();
            try {
                var order = _model.Orders.Include(a => a.OrderDetails).Include(a => a.OrderStatus).FirstOrDefault(a => a.Id == orderDto.Id);
                if (order == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }
                serviceResult.Data = _mapper.GetMapDto<OrderDto, Order>(order);
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
                var order = _model.Orders.Include(a => a.OrderDetails).Include(a => a.OrderStatus).FirstOrDefault(a => a.Id == orderId);
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

        public ServiceResult<List<OrderDto>> GetOrdersByQuery(Expression<Func<Order, bool>> expression) {
            var serviceResult = new ServiceResult<List<OrderDto>>();
            try {
                var orders = _model.Orders.Where(expression).ToList();
                serviceResult.Data = orders.Select(a => _mapper.GetMapDto<OrderDto, Order>(a)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult<List<OrderDto>> GetResponsePendingOrders() {
            var serviceResult = new ServiceResult<List<OrderDto>>();
            try {
                var orderDetails =
                    _model.OrderDetails.Include(a => a.Order)
                        .Include(a => a.TranslationOperation)
                        .Where(
                            a =>
                                a.TranslationOperation.TranslationProgressStatusId == (int)TranslationProgressStatusEnum.Open);

                serviceResult.Data = orderDetails.Select(a => _mapper.GetMapDto<OrderDto, Order>(a.Order)).ToList();
                serviceResult.ServiceResultType = ServiceResultType.Success;
            } catch (Exception exc) {
                _logger.Error($"Error occured in {MethodBase.GetCurrentMethod()} with message {exc.Message}");
                serviceResult.Exception = exc;
                serviceResult.ServiceResultType = ServiceResultType.Fail;
            }

            return serviceResult;
        }

        public ServiceResult AcceptOfferAsTranslator(int translatorId, int translationOperationId, decimal price) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail =
                    _model.OrderDetails.Include(a => a.Order)
                        .Include(a => a.TranslationOperation)
                        .FirstOrDefault(a => a.TranslationOperationId == translationOperationId);

                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var translationOperation = orderDetail.TranslationOperation;
                var order = orderDetail.Order;

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

        public ServiceResult AcceptOfferAsEditor(int editorId, int translationOperationId, decimal price) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail =
                    _model.OrderDetails.Include(a => a.Order)
                        .Include(a => a.TranslationOperation)
                        .FirstOrDefault(a => a.TranslationOperationId == translationOperationId);

                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var translationOperation = orderDetail.TranslationOperation;
                var order = orderDetail.Order;

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

        public ServiceResult AcceptOfferAsProofReader(int proofReaderId, int translationOperationId, decimal price) {
            var serviceResult = new ServiceResult();
            try {
                var orderDetail =
                    _model.OrderDetails.Include(a => a.Order)
                        .Include(a => a.TranslationOperation)
                        .FirstOrDefault(a => a.TranslationOperationId == translationOperationId);

                if (orderDetail == null) {
                    throw new BusinessException(ExceptionCodes.NoRelatedData);
                }

                var translationOperation = orderDetail.TranslationOperation;
                var order = orderDetail.Order;

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

                var order = _model.Orders.FirstOrDefault(a => a.Id == orderDetail.OrderId);
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

                serviceResult.Data = campaigns.Select(a=>_mapper.GetMapDto<CampaignItemDto,CampaignItem>(a)).ToList();
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
                var campaign = _model.CampaignItems.FirstOrDefault(a=>a.Id==campaingItemId);
                if (campaign==null) {
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

                _model.Entry(campaign).State=EntityState.Modified;
                var result = _model.SaveChanges() >0;
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

        #endregion

        private decimal CalculateOrderPrice(CreateTranslationOrderRequestDto orderRequest, int terminologyId, decimal? campaingDiscountAmount = null) {
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

            var terminologyRate = _model.TerminologyPriceRate.FirstOrDefault(a => a.TerminologyId == terminologyId);
            if (terminologyRate == null) {
                throw new Exception($"There is no defined terminology price rate for {terminologyId}");
            }


            var calculatedPrice = orderPrice * orderRequest.CharCountWithSpaces;
            calculatedPrice += calculatedPrice * terminologyRate.Rate;

            if (campaingDiscountAmount.HasValue) {
                calculatedPrice = calculatedPrice - calculatedPrice * campaingDiscountAmount.Value;
            }

            return calculatedPrice;

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
    }
}