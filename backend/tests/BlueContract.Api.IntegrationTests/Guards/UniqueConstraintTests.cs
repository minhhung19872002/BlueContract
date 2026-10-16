using BlueContract.Api.IntegrationTests.Infrastructure;
using BlueContract.Application.Common;
using BlueContract.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Shouldly;

namespace BlueContract.Api.IntegrationTests.Guards;

/// <summary>AUDIT-1 §10.6: every unique index of the database tells the losing request, in Vietnamese, which rule it broke.</summary>
[Collection(ApiCollection.Name)]
public class UniqueConstraintTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_unique_index_has_a_business_message()
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var names = await db.Database.SqlQuery<string>($"""
            SELECT indexname AS "Value" FROM pg_indexes
            WHERE schemaname = 'public' AND indexdef LIKE 'CREATE UNIQUE%' AND indexname NOT LIKE 'pk%'
            """).ToListAsync();
        names.ShouldNotBeEmpty();
        var unmapped = names.Where(n => !UniqueConstraints.Names.Contains(n)).ToList();
        unmapped.ShouldBeEmpty("unique indexes without a business message: " + string.Join(", ", unmapped));
        UniqueConstraints.Names.Select(UniqueConstraints.CodeFor).ShouldAllBe(code => ErrorMessages.IsKnown(code));
    }
}
