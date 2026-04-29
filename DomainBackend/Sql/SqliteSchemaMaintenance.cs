using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;

namespace Domain.Backend.Sql;

public static class SqliteSchemaMaintenance
{
    private static readonly IReadOnlyDictionary<string, string> ResultColumns = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["raw_whois"] = "TEXT NULL",
        ["expiration_date"] = "INTEGER NULL",
        ["proxy_id"] = "TEXT NULL",
        ["proxy_elapsed_ms"] = "INTEGER NULL",
        ["proxy_worker_id"] = "INTEGER NULL",
        ["dispatch_info"] = "TEXT NULL",
        ["registration_price_snapshot_json"] = "TEXT NULL",
        ["registration_price_snapshot_at"] = "INTEGER NULL"
    };

    public static async Task EnsureAsync(DomainBackendDbContext db, CancellationToken cancellationToken = default)
    {
        var existingColumns = await db.Database.SqlQueryRaw<string>(
                "SELECT name AS Value FROM pragma_table_info('domain_search_results')")
            .ToListAsync(cancellationToken);
        var existing = existingColumns.ToHashSet(StringComparer.OrdinalIgnoreCase);

        foreach (var (column, definition) in ResultColumns)
        {
            if (existing.Contains(column))
            {
                continue;
            }

            var sql = "ALTER TABLE domain_search_results ADD COLUMN " + column + " " + definition;
            await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
        }
    }
}
