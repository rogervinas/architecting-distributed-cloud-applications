using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace DbWriter { 
    class Program { static void Main(string[] args) {
        var itemIds = new List<Guid>();

        using (var dbContext = new InventoryDbContext())
        {
            for (var i = 0; i < 10; i++)
            {
                var id = Guid.NewGuid();
                var random = new Random();
                itemIds.Add(id);
                dbContext.Inventory.Add(new InventoryRecord
                {
                    ItemId = id,
                    ItemName = $"Item{(char)('A' + (char)i)}",
                    AvailableItems = 10
                });
            }

            var count = dbContext.SaveChanges();
            Console.WriteLine($"Initialized the database with {count} items. Their names are:");

            foreach (var item in dbContext.Inventory)
            {
                Console.WriteLine(item.ItemName);
            }
        }

        var updateOnTheSecond = new AutoResetEvent(false);

        var task1 = Task.Factory.StartNew(() =>
        {
            using (var dbContext = new InventoryDbContext())
            {
                var i = 0;
                foreach (var item in dbContext.Inventory)
                {
                    item.AvailableItems = 100;

                    if (i++ == 4)
                    {
                        updateOnTheSecond.Set();
                    }
                }
                SaveAndCatch(dbContext);
            }
        });

        var task2 = Task.Factory.StartNew(() =>
        {
            using (var dbContext = new InventoryDbContext())
            {
                foreach (var item in dbContext.Inventory)
                {
                    item.AvailableItems = 200;
                }
                updateOnTheSecond.WaitOne();

                SaveAndCatch(dbContext);
            }
        });

        Task.WaitAll(task1, task2);

        using (var dbContext = new InventoryDbContext())
        {
            Console.WriteLine($"The data in the table is now:");

            foreach (var item in dbContext.Inventory)
            {
                Console.WriteLine($"Item: {item.ItemName}, available: {item.AvailableItems}");
            }
        }
    }

    private static void SaveAndCatch(InventoryDbContext dbContext)
    {
        try
        {
            dbContext.SaveChanges();
        }
        catch (DbUpdateConcurrencyException exception)
        {
            foreach (var entry in exception.Entries)
            {
                if (entry.Entity is InventoryRecord)
                {
                    // Using a NoTracking query means we get the entity but it is not tracked by the context
                    // and will not be merged with existing entities in the context.
                    var databaseEntity = dbContext.Inventory.AsNoTracking()
                        .Single(p => p.ItemId == ((InventoryRecord)entry.Entity).ItemId);
                    var databaseEntry = dbContext.Entry(databaseEntity);

                    foreach (var property in entry.Metadata.GetProperties())
                    {
                        var proposedValue = entry.Property(property.Name).CurrentValue;
                        var originalValue = entry.Property(property.Name).OriginalValue;
                        var databaseValue = databaseEntry.Property(property.Name).CurrentValue;

                        // Decide how to handle the changes, currently just display for demo purposes.
                        Console.WriteLine($"Proposed: {proposedValue}, original: {originalValue}, database: {databaseValue}");

                        // Update original values to
                        // entry.Property(property.Name).OriginalValue = databaseEntry.Property(property.Name).CurrentValue;
                    }
                }
                else
                {
                    throw new NotSupportedException("Don't know how to handle concurrency conflicts for " + entry.Metadata.Name);
                }
            }

            // Retry the save operation as desired.
            // dbContext.SaveChanges();
        }
    }
}
}
