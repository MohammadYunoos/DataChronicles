using DataChronicles.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace DataChronicles.Api.Services;

public class DataChroniclesDbContext : DbContext
{
    public DataChroniclesDbContext(DbContextOptions<DataChroniclesDbContext> options) : base(options) { }

    public DbSet<OutputTicket> Tickets => Set<OutputTicket>();
}
