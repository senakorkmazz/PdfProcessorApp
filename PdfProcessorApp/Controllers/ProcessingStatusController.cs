using Microsoft.AspNetCore.Mvc;
using PdfProcessorApp.Models;
using PdfProcessorApp.Services;
using System.Collections.Generic;

namespace PdfProcessorApp.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ProcessingStatusController : ControllerBase
    {
        private readonly ProcessingStatusService _statusService;

        public ProcessingStatusController(ProcessingStatusService statusService)
        {
            _statusService = statusService;
        }

        [HttpGet("{requestId}")]
        public ActionResult<PdfProcessingMessage> GetStatus(string requestId)
        {
            var status = _statusService.GetStatus(requestId);
            if (status == null)
            {
                return NotFound();
            }

            return Ok(status);
        }

        [HttpGet]
        public ActionResult<IEnumerable<PdfProcessingMessage>> GetAllStatus()
        {
            var statuses = _statusService.GetAllProcessingMessages();
            return Ok(statuses);
        }
    }
}
