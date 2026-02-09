using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BistrosoftChallenge.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace BistrosoftChallenge.Infrastructure.Schema
{
    public class SchemaCompatibilityValidator
    {
        private readonly DbContext _dbContext;

        public SchemaCompatibilityValidator(DbContext dbContext)
        {
            _dbContext = dbContext;
        }

        public async Task Validate()
        {
            // Skip validation when using InMemory provider because it does not expose a relational DbConnection.
            var provider = _dbContext.Database.ProviderName;
            if (provider != null && provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            // If the database does not exist or cannot be connected to, try to create it.
            if (!_dbContext.Database.CanConnect())
            {
                _dbContext.Database.EnsureCreated();
                _dbContext.Add<Product>(new Product { Name = "Sample", Price = 0, StockQuantity = 10 });
                await _dbContext.SaveChangesAsync();
            }

            var conn = _dbContext.Database.GetDbConnection();
            conn.Open();
            try
            {
                var model = _dbContext.Model;
                var missing = new List<string>();
                foreach (var entityType in model.GetEntityTypes())
                {
                    var tableName = entityType.GetTableName();
                    if (string.IsNullOrEmpty(tableName))
                    {
                        missing.Add(entityType.Name);
                        continue;
                    }

                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = $"SELECT TOP 1 * FROM [{tableName}]";
                    try
                    {
                        using var reader = cmd.ExecuteReader();
                        // if succeed table exists
                    }
                    catch (Exception ex)
                    {
                        missing.Add(tableName + ": " + ex.Message);
                    }
                }

                if (missing.Count > 0)
                {
                    throw new InvalidOperationException("Schema compatibility validation failed: " + string.Join(",", missing));
                }
            }
            finally
            {
                conn.Close();
            }
        }
    }
}
