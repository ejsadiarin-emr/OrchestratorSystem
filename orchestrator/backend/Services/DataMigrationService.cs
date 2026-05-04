using Microsoft.EntityFrameworkCore;

public class DataMigrationService : IDataMigrationService
{
    private readonly AppDbContext _dbContext;

    public DataMigrationService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task InitializeAsync()
    {
        await _dbContext.Database.EnsureCreatedAsync();
    }
}
