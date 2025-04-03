using HL7ParserAPI.Models;
using Microsoft.EntityFrameworkCore;

namespace HL7ParserAPI.Data
{
    public class ApplicationDbContext: DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
        {
        }
        public DbSet<HL7Record> HL7Records { get; set; } // Represents HL7Records Table

    }
}

