using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Efferent.HL7.V2;
using Newtonsoft.Json;
using HL7ParserAPI.Data;
using HL7ParserAPI.Models;
using HL7ParserAPI.Services;
using Microsoft.Extensions.Logging;

namespace HL7ParserAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HL7Controller : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly RabbitMQService _rabbitMQService;
        private readonly ILogger<HL7Controller> _logger;

        public HL7Controller(ApplicationDbContext context, RabbitMQService rabbitMQService, ILogger<HL7Controller> logger)
        {
            _context = context;
            _rabbitMQService = rabbitMQService;
            _logger = logger;
        }

        [HttpPost("parse")]
        [Consumes("text/plain")]
        public async Task<IActionResult> ParseHL7()
        {
            try
            {
                using var reader = new StreamReader(Request.Body);
                string hl7Message = await reader.ReadToEndAsync();

                hl7Message = hl7Message.Replace("\n", "\r").Trim();
                hl7Message = string.Join("\r", hl7Message.Split('\r').Where(line => !string.IsNullOrWhiteSpace(line)));

                _logger.LogInformation("Received HL7 message: {HL7Message}", hl7Message);

                if (string.IsNullOrEmpty(hl7Message))
                {
                    _logger.LogWarning("Empty HL7 message received.");
                    return BadRequest(new { Error = "HL7 message cannot be empty." });
                }

                var message = new Message(hl7Message);
                bool parseResult = message.ParseMessage();

                if (!parseResult)
                {
                    _logger.LogWarning("Failed to parse HL7 message.");
                    return BadRequest(new { Error = "Failed to parse HL7 message. Check format." });
                }

                var messageType = message.Segments("MSH").FirstOrDefault()?.Fields(9)?.Value;
                var patientId = message.Segments("PID").FirstOrDefault()?.Fields(2)?.Value;

                if (string.IsNullOrEmpty(messageType) || string.IsNullOrEmpty(patientId))
                {
                    _logger.LogWarning("Required fields are missing in the HL7 message.");
                    return BadRequest(new { Error = "Required fields are missing in the HL7 message." });
                }

                string parsedJson = ConvertHL7ToJson(hl7Message);

                var hl7Entity = new HL7Record
                {
                    MessageType = messageType,
                    PatientID = patientId,
                    ParsedJson = parsedJson
                };

                _context.HL7Records.Add(hl7Entity);
                await _context.SaveChangesAsync();

                _rabbitMQService.PublishMessage(hl7Entity.Id, parsedJson);

                _logger.LogInformation("HL7 message processed and stored with ID: {RecordId}", hl7Entity.Id);

                return Ok(new { Id = hl7Entity.Id, ParsedJson = parsedJson });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "An error occurred while processing the HL7 message.");
                return StatusCode(500, new { Error = "An internal server error occurred." });
            }
        }

        private static string ConvertHL7ToJson(string hl7Message)
        {
            // Implement your HL7 to JSON conversion logic here
            return JsonConvert.SerializeObject(new { HL7 = hl7Message });
        }
    }
}
