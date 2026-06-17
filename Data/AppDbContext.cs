using EmekliRehberi.Models;
using Microsoft.EntityFrameworkCore;

namespace EmekliRehberi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
        {
        }

        public DbSet<AppUser> Users { get; set; }

        public DbSet<ServiceStatement> ServiceStatements { get; set; }

        public DbSet<PremiumDocument> PremiumDocuments { get; set; }

        public DbSet<PremiumRecord> PremiumRecords { get; set; }
        public DbSet<SocialSecurityRecordDocument> SocialSecurityRecordDocuments { get; set; }
    }
}