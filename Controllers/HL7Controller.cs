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
    public class HL7Controller : ControllerBase // Inheriting from ControllerBase to create a RESTful API controller.
    {
        private readonly ApplicationDbContext _context; // Database context for interacting with the database.
        private readonly RabbitMQService _rabbitMQService; // Service for publishing messages to RabbitMQ.
        private readonly ILogger<HL7Controller> _logger; // Logger for logging information, warnings, and errors.

        // Constructor to initialize dependencies via dependency injection.
        public HL7Controller(ApplicationDbContext context, RabbitMQService rabbitMQService, ILogger<HL7Controller> logger)
        {
            _context = context; // Assigning the database context.
            _rabbitMQService = rabbitMQService; // Assigning the RabbitMQ service.
            _logger = logger; // Assigning the logger.
        }

        [HttpPost("parse")] // Defining an HTTP POST endpoint with the route "parse".
        [Consumes("text/plain")] // Specifying that the endpoint consumes plain text.
        public async Task<IActionResult> ParseHL7() // Asynchronous method to parse HL7 messages.
        {
            try
            {
                using var reader = new StreamReader(Request.Body); // Reading the request body as a stream.
                string hl7Message = await reader.ReadToEndAsync(); // Reading the entire request body as a string.

                hl7Message = hl7Message.Replace("\n", "\r").Trim(); // Replacing line breaks and trimming whitespace.
                hl7Message = string.Join("\r", hl7Message.Split('\r').Where(line => !string.IsNullOrWhiteSpace(line))); // Removing empty lines.

                _logger.LogInformation("Received HL7 message: {HL7Message}", hl7Message); // Logging the received HL7 message.

                if (string.IsNullOrEmpty(hl7Message)) // Checking if the HL7 message is empty.
                {
                    _logger.LogWarning("Empty HL7 message received."); // Logging a warning for an empty message.
                    return BadRequest(new { Error = "HL7 message cannot be empty." }); // Returning a 400 Bad Request response.
                }

                var message = new Message(hl7Message); // Creating a new HL7 message object.
                bool parseResult = message.ParseMessage(); // Parsing the HL7 message.

                if (!parseResult) // Checking if the parsing failed.
                {
                    _logger.LogWarning("Failed to parse HL7 message."); // Logging a warning for parsing failure.
                    return BadRequest(new { Error = "Failed to parse HL7 message. Check format." }); // Returning a 400 Bad Request response.
                }

                var messageType = message.Segments("MSH").FirstOrDefault()?.Fields(9)?.Value; // Extracting the message type from the MSH segment.
                var patientId = message.Segments("PID").FirstOrDefault()?.Fields(2)?.Value; // Extracting the patient ID from the PID segment.

                if (string.IsNullOrEmpty(messageType) || string.IsNullOrEmpty(patientId)) // Checking if required fields are missing.
                {
                    _logger.LogWarning("Required fields are missing in the HL7 message."); // Logging a warning for missing fields.
                    return BadRequest(new { Error = "Required fields are missing in the HL7 message." }); // Returning a 400 Bad Request response.
                }

                string parsedJson = ConvertHL7ToJson(hl7Message); // Converting the HL7 message to JSON format.

                var hl7Entity = new HL7Record // Creating a new HL7Record entity.
                {
                    MessageType = messageType, // Setting the message type.
                    PatientID = patientId, // Setting the patient ID.
                    ParsedJson = parsedJson // Setting the parsed JSON.
                };

                _context.HL7Records.Add(hl7Entity); // Adding the HL7Record entity to the database context.
                await _context.SaveChangesAsync(); // Saving changes to the database.

                _rabbitMQService.PublishMessage(hl7Entity.Id, parsedJson); // Publishing the parsed message to RabbitMQ.

                _logger.LogInformation("HL7 message processed and stored with ID: {RecordId}", hl7Entity.Id); // Logging the successful processing of the message.

                return Ok(new { Id = hl7Entity.Id, ParsedJson = parsedJson }); // Returning a 200 OK response with the record ID and parsed JSON.
            }
            catch (Exception ex) // Catching any exceptions that occur.
            {
                _logger.LogError(ex, "An error occurred while processing the HL7 message."); // Logging the error.
                return StatusCode(500, new { Error = "An internal server error occurred." }); // Returning a 500 Internal Server Error response.
            }
        }

        private static string ConvertHL7ToJson(string hl7Message) // Static method to convert HL7 message to JSON.
        {
            // Implement your HL7 to JSON conversion logic here.
            return JsonConvert.SerializeObject(new { HL7 = hl7Message }); // Serializing the HL7 message to JSON.
        }
    }
}
