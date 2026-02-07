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

        public void Validate()
        {
            // Compare EF model with database schema by checking that all tables and columns exist.
            // This is a minimal implementation: ensure that every entity's table exists and has at least one column.

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
