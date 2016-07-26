using System;
using System.Collections.Generic;
using System.Web.Http;
using Tangent.CeviriDukkani.Event.DocumentEvents;
using Tangent.CeviriDukkani.Messaging;
using Tangent.CeviriDukkani.Messaging.Producer;

namespace OMS.Api.Controllers {
    [RoutePrefix("api/commonapi")]
    public class HomeApiController : ApiController {
        private readonly IDispatchCommits _dispatchCommits;

        public HomeApiController(IDispatchCommits dispatchCommits) {
            _dispatchCommits = dispatchCommits;
        }

        [HttpGet, Route("produceEvent")]
        public string ProduceEvent() {
            var orderCreatedEvent = new CreateDocumentPartEvent {
                Id = Guid.NewGuid(),
                OrderId = 1,
                TranslationDocumentId = 1,
                TranslationQualityId = 1
            };

            _dispatchCommits.Dispatch(new List<EventMessage> {
                orderCreatedEvent.ToEventMessage()
            });
            return "OK";
        }
    }
}