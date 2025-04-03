using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Efferent.HL7.V2;
using Newtonsoft.Json;
using HL7ParserAPI.Data;
using HL7ParserAPI.Models;
using HL7ParserAPI.Services;
using Microsoft.AspNetCore.SignalR.Protocol;

namespace HL7ParserAPI.Controllers
{
    [Route("api/HL7Controller")]
    [ApiController]
    public class HL7Controller : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly RabbitMQService _rabbitMQService;

        public HL7Controller(ApplicationDbContext context, RabbitMQService rabbitMQService)
        {
            _context = context;
            _rabbitMQService = rabbitMQService;
        }

        [HttpPost("parse")]
        [Consumes("text/plain")] // Accepts text format
        public async Task<IActionResult> ParseHL7()
        {
            using var reader = new StreamReader(Request.Body);
            string hl7Message = await reader.ReadToEndAsync();

            hl7Message = hl7Message.Replace("\n", "\r").Trim();  // Convert \n to \r for consistency
            hl7Message = string.Join("\r", hl7Message.Split('\r').Where(line => !string.IsNullOrWhiteSpace(line)));  // Remove empty lines

            Console.WriteLine($"HL7 Message Received: {hl7Message}");
            Console.WriteLine($"Content-Type Received: {Request.ContentType}");

            if (string.IsNullOrEmpty(hl7Message))
            {
                return BadRequest(new { Error = "HL7 message cannot be empty." });
            }

            try
            {
                var message = new Message(hl7Message); // Using Efferent.HL7.V2.Message
                bool parseResult = message.ParseMessage();

                Console.WriteLine($"Parsing Result: {parseResult}");

                if (!parseResult)
                {
                    Console.WriteLine("Parsing failed! Inspecting message segments...");
                    foreach (var segment in hl7Message.Split('\r'))
                    {
                        Console.WriteLine($"Segment: {segment}");
                    }
                    return BadRequest(new { Error = "Failed to parse HL7 message. Check format." });
                }

                //Extracting values
                var messageType = message.Segments("MSH").FirstOrDefault()?.Fields(9)?.Value;
                var patientId = message.Segments("PID").FirstOrDefault()?.Fields(2)?.Value;


                // var patientIdField = message.Segments("PID").FirstOrDefault()?.Fields(2)?.Value;
                //var patientId = patientIdField?.Split('^').FirstOrDefault();

                // Extract Patient ID using Regex
                //var patientIdField = message.Segments("PID").FirstOrDefault()?.Fields(2)?.Value;
                //var patientId = Regex.Match(patientIdField ?? "", @"^[^\^]+").Value;

                // Handle possible null values
                if (messageType == null || patientId == null)
                {
                    return BadRequest(new { Error = "Required fields are missing in the HL7 message." });
                }

                string parsedJson = ConvertHL7ToJson(hl7Message);

                // Store in database
                var hl7Entity = new HL7Record
                {
                    MessageType = messageType,
                    PatientID = patientId,
                    ParsedJson = parsedJson
                };

                _context.HL7Records.Add(hl7Entity);
                await _context.SaveChangesAsync();

                // Publish to RabbitMQ
                _rabbitMQService.PublishMessage(hl7Entity.Id, parsedJson);

                return Ok(new { Id = hl7Entity.Id, ParsedJson = parsedJson });
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception:" + ex.Message);
                return StatusCode(500, new { Error = ex.Message });
            }
        }

        public static string ConvertHL7ToJson(string hl7Message)
        {
            var result = new Dictionary<string, object>();

            // Split the HL7 message into segments
            string[] segments = hl7Message.Split(new[] { '\r' }, StringSplitOptions.RemoveEmptyEntries);

            // Iterate over each segment
            foreach (var segment in segments)
            {
                // Split the segment by the '|' delimiter
                string[] fields = segment.Split('|');

                // Check the segment type (first field) and parse accordingly
                string segmentType = fields[0];

                if (segmentType == "MSH")
                {
                    result["MSH"] = ParseMSH(fields);
                }
                else if (segmentType == "EVN")
                {
                    result["EVN"] = ParseEVN(fields);
                }
                else if (segmentType == "PID")
                {
                    result["PID"] = ParsePID(fields);
                }
                else if (segmentType == "PV1")
                {
                    result["PV1"] = ParsePV1(fields);
                }
            }

            // Return the JSON string of the parsed result
            return JsonConvert.SerializeObject(result, Formatting.Indented);
        }

        private static Dictionary<string, object> ParseMSH(string[] fields)
        {
            return new Dictionary<string, object>
            {
                { "field_separator", fields.ElementAtOrDefault(0) ?? "N/A" },
                { "encoding_characters", fields.ElementAtOrDefault(1) ?? "N/A" },
                { "sending_application", fields.ElementAtOrDefault(2) ?? "N/A" },
                { "sending_facility", fields.ElementAtOrDefault(3) ?? "N/A" },
                { "receiving_application", fields.ElementAtOrDefault(4) ?? "N/A" },
                { "receiving_facility", fields.ElementAtOrDefault(5) ?? "N/A" },
                { "date_time_of_message", fields.ElementAtOrDefault(6) ?? "N/A" },
                { "security", fields.ElementAtOrDefault(7) ?? "N/A" },
                { "message_type", fields.ElementAtOrDefault(8) ?? "N/A" },
                { "message_control_id", fields.ElementAtOrDefault(9) ?? "N/A" },
                { "processing_id", fields.ElementAtOrDefault(10) ?? "N/A" },
                { "version_id", fields.ElementAtOrDefault(11) ?? "N/A" }
            };
        }

        private static Dictionary<string, object> ParseEVN(string[] fields)
        {
            return new Dictionary<string, object>
            {
                { "event_type_code", fields.ElementAtOrDefault(1) ?? "N/A" },
                { "recorded_date_time", fields.ElementAtOrDefault(2) ?? "N/A" },
                { "date_time_planned_event", fields.ElementAtOrDefault(3) ?? "N/A" },
                { "event_reason_code", fields.ElementAtOrDefault(4) ?? "N/A" }
            };
        }

        private static Dictionary<string, object> ParsePID(string[] fields)
        {
            return new Dictionary<string, object>
            {
                { "patient_id", fields.ElementAtOrDefault(1) ?? "N/A" },
                { "patient_identifier_list", new List<Dictionary<string, string>> {
                    new Dictionary<string, string> {
                        { "id_number", fields.ElementAtOrDefault(2) ?? "N/A" },
                        { "identifier_type_code", fields.ElementAtOrDefault(3) ?? "N/A" }
                    }
                }},
                { "patient_name", new Dictionary<string, string> {
                    { "last_name", fields.ElementAtOrDefault(4)?.Split('^').ElementAtOrDefault(0) ?? "N/A" },
                    { "first_name", fields.ElementAtOrDefault(4)?.Split('^').ElementAtOrDefault(1) ?? "N/A" },
                    { "middle_name", fields.ElementAtOrDefault(4)?.Split('^').ElementAtOrDefault(2) ?? "N/A" },
                    { "suffix", fields.ElementAtOrDefault(4)?.Split('^').ElementAtOrDefault(3) ?? "N/A" }
                }},
                { "dob", fields.ElementAtOrDefault(6) ?? "N/A" },
                { "sex", fields.ElementAtOrDefault(7) ?? "N/A" },
                { "address", new List<Dictionary<string, string>> {
                    new Dictionary<string, string> {
                        { "street", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(0) ?? "N/A" },
                        { "city", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(1) ?? "N/A" },
                        { "state", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(2) ?? "N/A" },
                        { "zip", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(3) ?? "N/A" },
                        { "country", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(4) ?? "N/A" }
                    }
                }},
                { "phone_number_home", fields.ElementAtOrDefault(9) ?? "N/A" },
                { "phone_number_business", fields.ElementAtOrDefault(10) ?? "N/A" },
                { "social_security_number", fields.ElementAtOrDefault(11) ?? "N/A" },
                { "ethnic_group", fields.ElementAtOrDefault(12) ?? "N/A" },
                { "religion", fields.ElementAtOrDefault(13) ?? "N/A" }
            };
        }

        private static Dictionary<string, object> ParsePV1(string[] fields)
        {
            return new Dictionary<string, object>
            {
                { "set_id", fields.ElementAtOrDefault(1) ?? "N/A" },
                { "patient_class", fields.ElementAtOrDefault(2) ?? "N/A" },
                { "assigned_patient_location", new Dictionary<string, string> {
                    { "point_of_care", fields.ElementAtOrDefault(3)?.Split('^').ElementAtOrDefault(0) ?? "N/A" },
                    { "room", fields.ElementAtOrDefault(3)?.Split('^').ElementAtOrDefault(1) ?? "N/A" },
                    { "bed", fields.ElementAtOrDefault(3)?.Split('^').ElementAtOrDefault(2) ?? "N/A" }
                }},
                { "admission_type", fields.ElementAtOrDefault(4) ?? "N/A" },
                { "admitting_doctor", new Dictionary<string, string> {
                    { "id_number", fields.ElementAtOrDefault(5) ?? "N/A" },
                    { "last_name", fields.ElementAtOrDefault(6)?.Split('^').ElementAtOrDefault(0) ?? "N/A" },
                    { "first_name", fields.ElementAtOrDefault(6)?.Split('^').ElementAtOrDefault(1) ?? "N/A" },
                    { "prefix", fields.ElementAtOrDefault(6)?.Split('^').ElementAtOrDefault(2) ?? "N/A" }
                }},
                { "attending_doctor", new Dictionary<string, string> {
                    { "id_number", fields.ElementAtOrDefault(7) ?? "N/A" },
                    { "last_name", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(0) ?? "N/A" },
                    { "first_name", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(1) ?? "N/A" },
                    { "prefix", fields.ElementAtOrDefault(8)?.Split('^').ElementAtOrDefault(2) ?? "N/A" }
                }},
                { "hospital_service", fields.ElementAtOrDefault(9) ?? "N/A" },
                { "patient_type", fields.ElementAtOrDefault(10) ?? "N/A" },
                { "visit_number", fields.ElementAtOrDefault(11) ?? "N/A" },
                { "referral_source_code", fields.ElementAtOrDefault(12) ?? "N/A" }
            };
        }
    }
}