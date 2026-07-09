using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using ThreePl.Core.Data;

namespace ThreePl.Tests;

/// <summary>
/// SQLite in-memory IDbContextFactory so the EF read/admin services run in
/// tests without a live DB. The connection is held open for the fixture's
/// lifetime (an in-memory SQLite database lives as long as its connection).
/// </summary>
public sealed class TestDbFactory : IDbContextFactory<OnboardingDbContext>, IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<OnboardingDbContext> _options;

    public TestDbFactory()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
        _options = new DbContextOptionsBuilder<OnboardingDbContext>()
            .UseSqlite(_connection)
            .Options;
        using var db = CreateDbContext();
        db.Database.EnsureCreated();
    }

    public OnboardingDbContext CreateDbContext() => new(_options);

    public void Dispose() => _connection.Dispose();
}
