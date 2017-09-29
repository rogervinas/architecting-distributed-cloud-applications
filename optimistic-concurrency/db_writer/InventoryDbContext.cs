using System;
using Microsoft.EntityFrameworkCore;

namespace DbWriter
{
    public class InventoryDbContext: DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            optionsBuilder.UseNpgsql("Host=localhost;Database=inventory;Username=postgres;Password=postgresspasswrod");
        }

        public DbSet<InventoryRecord> Inventory { get; set; }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.Entity<InventoryRecord>()
                .ForNpgsqlUseXminAsConcurrencyToken()
                .HasKey(m => m.ItemId);

            base.OnModelCreating(builder);
        }
    }
}