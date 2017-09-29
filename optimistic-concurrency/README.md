# Optimistic Concurrency

We have gone through the concept of optimistic concurrency, and how it is used in environments with low contention for data and why it improves performance. Many libraries for accessing data sources include implementation of this technique. Let's see how it works against a PostgreSQL data source.

## Prerequisites

This lab has a dependency on the following technologies. These will need to be installed on a development machine to complete the lab.

*   [Docker CE](https://docs.docker.com/engine/installation/)
*   [.NET Core](https://www.microsoft.com/net/core)
*   [Visual Studio Code](https://code.visualstudio.com/)
*   [VS Code C# Extension](https://marketplace.visualstudio.com/items?itemName=ms-vscode.csharp)

## Setup the Environment

1.  Scaffold a new .NET Core console project with:

        dotnet new console --name db_writer

2.  Install the required nuget packages for connecting to PostgreSQL:

        dotnet add package Npgsql.EntityFrameworkCore.PostgreSQL && \
        dotnet add package Microsoft.EntityFrameworkCore.Design

3.  The EF Core .NET Command Line Tools are installed by manually editing the *.csproj file. Add Microsoft.EntityFrameworkCore.Tools.DotNet as a DotNetCliToolReference. See sample project below.

        <Project Sdk="Microsoft.NET.Sdk">
        <PropertyGroup>
         <OutputType>Exe</OutputType>
         <TargetFramework>netcoreapp1.1</TargetFramework>
        </PropertyGroup>
        <ItemGroup>
         <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="1.1.2" />
         <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="1.1.0" />
        </ItemGroup>
        <ItemGroup>
         <DotNetCliToolReference Include="Microsoft.EntityFrameworkCore.Tools.DotNet" Version="1.0.1" />
        </ItemGroup>
        </Project>

4.  And add the design package with:

        dotnet add package Microsoft.EntityFrameworkCore.Design

5.  Create a new file to contain the class representing the entity, `InventoryRecord`

        ```using System;namespace DbWriter{  public class InventoryRecord  {   public Guid ItemId { get; set; }   public string ItemName { get; set; }   public int AvailableItems { get; set; }  }}

    7\. Create a new file to contain the DBContext class. Entity Framework supports the concept of optimistic concurrency - a property on your entity is designated as a concurrency token, and EF detects concurrent modifications by checking whether that token has changed since the entity was read. You can read more about this in the EF docs. Although applications can update concurrency tokens themselves, we frequently rely on the database automatically updating a column on update - a "last modified" timestamp, an SQL Server rowversion, etc. Unfortunately PostgreSQL doesn't have such auto-updating columns - but there is one feature that can be used for concurrency token. All PostgreSQL have a set of implicit and hidden system columns, among which xmin holds the ID of the latest updating transaction. Since this value automatically gets updated every time the row is changed, it is ideal for use as a concurrency token.

        ```
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

    1.  We will instantiate a PostgreSQL Docker container. First pull the postgres image

            docker pull library/postgres

    2.  And instantiate the container and attach the stdout to see the logs on the database:

            docker run -it --name postgres -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgrespassword -e POSTGRES_DB=inventory -p 5432:5432  library/postgres

    3.  Add the EF migration to create the table and run the migration

            dotnet ef migrations add InitialCreate && \
            dotnet ef  database update

    4.  Connect to the container, and run psql utility to verify the table is created

            docker exec -it -e POSTGRES_USER=postgres -e POSTGRES_PASSWORD=postgddpassword -e POSTGRES_DB=inventory postgres /bin/bashInventoryRecord

    ## Handling Optimistic Concurrency

    1.  Update the program.cs with the following code:
    
    ```using System; using System.Collections.Generic; using System.Linq; using System.Threading; using System.Threading.Tasks; using Microsoft.EntityFrameworkCore;
  
        namespace DbWriter { class Program { static void Main(string[] args) {
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

        2\. Run the project and see the results:
        ```bash
        dotnet run
