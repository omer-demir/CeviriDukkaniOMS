using System;
using System.Collections.Generic;
using System.Data.Entity;
using System.Linq;
using System.Linq.Expressions;
using FluentAssertions;
using log4net;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Internal;
using OMS.Business.ExternalClients;
using OMS.Business.Services;
using Tangent.CeviriDukkani.Data.Model;
using Tangent.CeviriDukkani.Domain.Common;
using Tangent.CeviriDukkani.Domain.Dto.Document;
using Tangent.CeviriDukkani.Domain.Dto.Request;
using Tangent.CeviriDukkani.Domain.Entities.Sale;
using Tangent.CeviriDukkani.Domain.Mappers;
using Tangent.CeviriDukkani.Messaging;
using Tangent.CeviriDukkani.Messaging.Producer;

namespace OMS.Tests.Service {
    [TestFixture]
    public class when_creating_order {
        private TranslationDocumentDto document = new TranslationDocumentDto {
            Id = 1,
            CharCount = 22,
            CharCountWithSpaces = 22,
            Active = true,
            PageCount = 2,
            Name = "Dummy",
            Path = "Path-To-Read"
        };
        private List<PriceList> priceList=new List<PriceList> {
            new PriceList {Char_0_100 = 10,Char_100_150 = 12,Char_150_200 = 14,Char_200_500 = 16,Char_500_More = 20}
        };

        private OrderManagementService orderManagementService;


        [SetUp]
        public void SetupTest() {
            var documentClientService = new Mock<IDocumentServiceClient>();
            documentClientService
                .Setup(a => a.CreateDocument(It.IsAny<TranslationDocumentDto>()))
                .Returns(new ServiceResult {
                    ServiceResultType = ServiceResultType.Success,
                    Data = document
                });

            var mockModel = new Mock<CeviriDukkaniModel>();
            mockModel
                .Setup(a => a.PriceLists.Where(It.IsAny<Expression<Func<PriceList, bool>>>()))
                .Returns(priceList.AsQueryable());
            mockModel.Setup(a => a.Orders.Add(It.IsAny<Order>())).Returns(new Order {
                
            });

            var translationServiceMock=new Mock<ITranslationService>();
            translationServiceMock.Setup(a => a.GetAverageDocumentPartCount(It.IsAny<int>()))
                .Returns(new ServiceResult {
                    ServiceResultType = ServiceResultType.Success,
                    Data = 3
                });

            var dispatcherMock=new Mock<IDispatchCommits>();
            dispatcherMock.Setup(a => a.Dispatch(It.IsAny<List<EventMessage>>()));

            var loggerMock=new Mock<ILog>();
            loggerMock.Setup(a => a.Error(It.IsAny<object>()));
            loggerMock.Setup(a => a.Info(It.IsAny<object>()));

            orderManagementService=new OrderManagementService(mockModel.Object,new CustomMapperConfiguration(), dispatcherMock.Object,documentClientService.Object,translationServiceMock.Object, loggerMock.Object,null);
        }

        [Test]
        public void should_response_be_ok() {
            var result = orderManagementService.CreateOrder(new CreateTranslationOrderRequestDto());
            result.ServiceResultType.Should().Be(ServiceResultType.Success);
        }

    }
}