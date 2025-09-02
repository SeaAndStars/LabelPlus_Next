using LabelPlus_Next.ApiServer.Entities;
using Microsoft.EntityFrameworkCore;

namespace LabelPlus_Next.ApiServer.Data;

public sealed class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<FileEntry> Files => Set<FileEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<User>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Username).IsUnique();
        });
        modelBuilder.Entity<FileEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.HasIndex(x => x.Path).IsUnique();
            b.HasIndex(x => x.ParentPath);
        });
    }
}

