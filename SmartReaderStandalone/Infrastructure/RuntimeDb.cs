using Microsoft.EntityFrameworkCore;
using SmartReaderStandalone.Entities;

namespace SmartReader.Infrastructure.Database;

public class RuntimeDb : DbContext
{
    public RuntimeDb(DbContextOptions<RuntimeDb> options) : base(options)
    {
    }

    //public DbSet<Todo> Todos => Set<Todo>();

    public DbSet<ReaderCommands> ReaderCommands => Set<ReaderCommands>();

    public DbSet<ReaderStatus> ReaderStatus => Set<ReaderStatus>();

    public DbSet<ReaderConfigs> ReaderConfigs => Set<ReaderConfigs>();

    public DbSet<InventoryStatus> InventoryStatus => Set<InventoryStatus>();

    public DbSet<PostioningEpcs> PostioningEpcs => Set<PostioningEpcs>();

    public DbSet<ObjectEpcs> ObjectEpcs => Set<ObjectEpcs>();

    public DbSet<SmartReaderTagReadModel> SmartReaderTagReadModels => Set<SmartReaderTagReadModel>();

    public DbSet<SmartReaderSkuSummaryModel> SmartReaderSkuSummaryModels => Set<SmartReaderSkuSummaryModel>();
}