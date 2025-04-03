namespace HL7ParserAPI.Models
{
    public class HL7Record
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string MessageType { get; set; }
        public string PatientID { get; set; }
        public string ParsedJson { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
