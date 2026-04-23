using Microsoft.EntityFrameworkCore;
using Multiplay.Server.Data;

namespace Multiplay.Server.Tests.Helpers;

public static class DbHelper
{
    /// <summary>Creates a fresh in-memory database for each test.</summary>
    public static AppDbContext CreateDb() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
}
