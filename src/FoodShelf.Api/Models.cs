using Microsoft.EntityFrameworkCore;

namespace FoodShelf;

public class Category { public int Id { get; set; } public string Name { get; set; } = ""; [System.Text.Json.Serialization.JsonIgnore] public string NameKey { get; set; } = ""; }
public class StorageLocation { public int Id { get; set; } public string Name { get; set; } = ""; [System.Text.Json.Serialization.JsonIgnore] public string NameKey { get; set; } = ""; }
public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    [System.Text.Json.Serialization.JsonIgnore] public string NameKey { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "шт";
    public DateOnly ExpirationDate { get; set; }
    public int CategoryId { get; set; }
    public Category Category { get; set; } = null!;
    public int StorageLocationId { get; set; }
    public StorageLocation StorageLocation { get; set; } = null!;
}
public record ProductInput(string Name, decimal Quantity, string Unit, DateOnly ExpirationDate, int CategoryId, int StorageLocationId);
public record NameInput(string Name);
public record ProductView(int Id, string Name, decimal Quantity, string Unit, DateOnly ExpirationDate,
    int CategoryId, string Category, int StorageLocationId, string StorageLocation, int DaysLeft, string Status);

public static class ShelfRules
{
    // Продукт годен до конца указанной даты включительно.
    public static int DaysLeft(DateOnly expiration, DateOnly today) => expiration.DayNumber - today.DayNumber;
    public static string Status(DateOnly expiration, DateOnly today) => DaysLeft(expiration, today) switch
    { < 0 => "expired", <= 3 => "soon", _ => "fresh" };
    public static string? Validate(ProductInput p)
    {
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Trim().Length > 120) return "Название должно содержать от 1 до 120 символов.";
        if (p.Quantity <= 0 || p.Quantity > 1000000 || decimal.Round(p.Quantity, 3) != p.Quantity) return "Количество должно быть больше 0, не больше 1 000 000 и иметь до трёх знаков после запятой.";
        if (!new[] { "шт", "кг", "г", "л", "мл" }.Contains(p.Unit)) return "Выберите единицу измерения из списка.";
        if (p.ExpirationDate == default) return "Укажите срок годности.";
        if (p.CategoryId <= 0 || p.StorageLocationId <= 0) return "Выберите категорию и место хранения.";
        return null;
    }
    public static ProductView View(Product p, DateOnly today) => new(p.Id, p.Name, p.Quantity, p.Unit, p.ExpirationDate,
        p.CategoryId, p.Category.Name, p.StorageLocationId, p.StorageLocation.Name, DaysLeft(p.ExpirationDate, today), Status(p.ExpirationDate, today));
}

public partial class ShelfDb(DbContextOptions<ShelfDb> options) : DbContext(options)
{
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        foreach (var entry in ChangeTracker.Entries().Where(x => x.State is EntityState.Added or EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case Product p: p.NameKey = p.Name.ToUpperInvariant(); break;
                case Category c: c.NameKey = c.Name.ToUpperInvariant(); break;
                case StorageLocation l: l.NameKey = l.Name.ToUpperInvariant(); break;
            }
        }
        return base.SaveChangesAsync(cancellationToken);
    }
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<StorageLocation> StorageLocations => Set<StorageLocation>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        ConfigureOrders(b);
        b.Entity<Category>().Property(x => x.Name).HasMaxLength(60);
        b.Entity<Category>().HasIndex(x => x.NameKey).IsUnique();
        b.Entity<StorageLocation>().Property(x => x.Name).HasMaxLength(60);
        b.Entity<StorageLocation>().HasIndex(x => x.NameKey).IsUnique();
        b.Entity<Product>().Property(x => x.Name).HasMaxLength(120);
        b.Entity<Product>().Property(x => x.Unit).HasMaxLength(5);
        b.Entity<Product>().Property(x => x.Quantity).HasPrecision(12, 3);
        b.Entity<Product>().HasIndex(x => x.ExpirationDate);
        b.Entity<Product>().HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Product>().HasOne(x => x.StorageLocation).WithMany().HasForeignKey(x => x.StorageLocationId).OnDelete(DeleteBehavior.Restrict);
    }
}
