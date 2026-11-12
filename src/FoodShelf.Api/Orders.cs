using System.Net.Mail;
using Microsoft.EntityFrameworkCore;

namespace FoodShelf;

public class Customer
{
    public int Id { get; set; }
    public string FirstName { get; set; } = "";
    public string LastName { get; set; } = "";
    public DateOnly BirthDate { get; set; }
    public string Email { get; set; } = "";
    public string Address { get; set; } = "";
    public string SearchKey { get; set; } = "";
}
public class CustomerOrder
{
    public int Id { get; set; }
    public decimal Amount { get; set; }
    public DateTimeOffset OrderedAt { get; set; }
    public string Status { get; set; } = "Pending";
    public int CustomerId { get; set; }
    public Customer Customer { get; set; } = null!;
}
public record CustomerInput(string FirstName, string LastName, DateOnly BirthDate, string Email, string Address);
public record OrderInput(decimal Amount, DateTimeOffset OrderedAt, string Status, int CustomerId);
public record OrderView(int Id, decimal Amount, DateTimeOffset OrderedAt, string Status, int CustomerId, string FirstName, string LastName, string Email, string Address);
public class BirthdayReport { public int CustomerId { get; set; } public string FirstName { get; set; } = ""; public string LastName { get; set; } = ""; public decimal Total { get; set; } }
public class HourlyReport { public int Hour { get; set; } public decimal Average { get; set; } public int Count { get; set; } }
public class InventoryReport { public int CategoryId { get; set; } public string Name { get; set; } = ""; public int ProductCount { get; set; } }

public partial class ShelfDb
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CustomerOrder> Orders => Set<CustomerOrder>();
    static void ConfigureOrders(ModelBuilder b)
    {
        b.Entity<Customer>().Property(x => x.FirstName).HasMaxLength(60);
        b.Entity<Customer>().Property(x => x.LastName).HasMaxLength(60);
        b.Entity<Customer>().Property(x => x.Email).HasMaxLength(120);
        b.Entity<Customer>().Property(x => x.Address).HasMaxLength(250);
        b.Entity<CustomerOrder>().Property(x => x.Amount).HasPrecision(14, 2);
        b.Entity<CustomerOrder>().Property(x => x.Status).HasMaxLength(20);
        b.Entity<CustomerOrder>().HasIndex(x => x.OrderedAt);
        b.Entity<CustomerOrder>().HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
    }
}
public static class OrderRules
{
    public static readonly string[] Statuses = ["Pending", "Cancelled", "Completed", "Archive"];
    public static string? Validate(CustomerInput p, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(p.FirstName) || p.FirstName.Trim().Length > 60 || string.IsNullOrWhiteSpace(p.LastName) || p.LastName.Trim().Length > 60) return "Имя и фамилия обязательны и должны содержать до 60 символов.";
        if (p.BirthDate < new DateOnly(1900, 1, 1) || p.BirthDate > today) return "Укажите корректную дату рождения.";
        if (string.IsNullOrWhiteSpace(p.Email) || p.Email.Length > 120 || !MailAddress.TryCreate(p.Email, out var mail) || mail.Address != p.Email) return "Укажите корректный email.";
        if (string.IsNullOrWhiteSpace(p.Address) || p.Address.Length > 250) return "Адрес обязателен, до 250 символов.";
        return null;
    }
    public static string? Validate(OrderInput p) => p.Amount <= 0 || p.Amount > 1000000000 || decimal.Round(p.Amount, 2) != p.Amount ? "Сумма должна быть положительной, до 1 млрд и двух знаков после запятой." : !Statuses.Contains(p.Status) ? "Неизвестный статус." : p.OrderedAt == default ? "Укажите дату заказа." : p.CustomerId < 1 ? "Выберите клиента." : null;
    public static OrderView View(CustomerOrder p) => new(p.Id, p.Amount, p.OrderedAt, p.Status, p.CustomerId, p.Customer.FirstName, p.Customer.LastName, p.Customer.Email, p.Customer.Address);
    public static DateTimeOffset MoscowTime(DateTimeOffset value) => TimeZoneInfo.ConvertTimeBySystemTimeZoneId(value, "Europe/Moscow");
    public static List<BirthdayReport> Birthdays(IEnumerable<Customer> customers, IEnumerable<CustomerOrder> orders) => customers.OrderBy(c => c.Id).Select(c => new BirthdayReport { CustomerId = c.Id, FirstName = c.FirstName, LastName = c.LastName, Total = orders.Where(o => o.CustomerId == c.Id && o.Status == "Completed" && MoscowTime(o.OrderedAt).Month == c.BirthDate.Month && MoscowTime(o.OrderedAt).Day == c.BirthDate.Day).Sum(o => o.Amount) }).ToList();
    public static List<HourlyReport> Hourly(IEnumerable<CustomerOrder> source)
    {
        var orders = source.Where(o => o.Status == "Completed").ToList();
        return Enumerable.Range(0, 24).Select(hour => { var values = orders.Where(o => MoscowTime(o.OrderedAt).Hour == hour).Select(o => o.Amount).ToList(); return new HourlyReport { Hour = hour, Average = values.Count == 0 ? 0 : decimal.Round(values.Average(), 2, MidpointRounding.AwayFromZero), Count = values.Count }; }).OrderByDescending(x => x.Average).ThenBy(x => x.Hour).ToList();
    }
}
public static class OrderModule
{
    public static async Task InitializeAsync(ShelfDb db, bool demo)
    {
        // Совместимость с созданной ранее БД FoodShelf, без удаления продуктов.
        var id = demo ? "INTEGER PRIMARY KEY AUTOINCREMENT" : "integer GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY";
        var amount = demo ? "TEXT" : "numeric(14,2)"; var timestamp = demo ? "TEXT" : "timestamp with time zone"; var date = demo ? "TEXT" : "date";
        var auxiliarySchema = $"""
            CREATE TABLE IF NOT EXISTS "Customers" ("Id" {id}, "FirstName" text NOT NULL, "LastName" text NOT NULL, "BirthDate" {date} NOT NULL, "Email" text NOT NULL, "Address" text NOT NULL, "SearchKey" text NOT NULL);
            CREATE TABLE IF NOT EXISTS "Orders" ("Id" {id}, "Amount" {amount} NOT NULL, "OrderedAt" {timestamp} NOT NULL, "Status" text NOT NULL, "CustomerId" integer NOT NULL REFERENCES "Customers"("Id") ON DELETE RESTRICT);
            CREATE INDEX IF NOT EXISTS "IX_Orders_OrderedAt" ON "Orders"("OrderedAt");
            CREATE INDEX IF NOT EXISTS "IX_Orders_CustomerId" ON "Orders"("CustomerId");
            """;
        // Только фиксированные типы SQL из выбранного провайдера, без пользовательских значений.
        await db.Database.ExecuteSqlRawAsync(auxiliarySchema);
        if (!demo) await db.Database.ExecuteSqlRawAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Sql", "procedures.sql")));
    }
    public static void MapOrderModule(this WebApplication app, bool demo)
    {
        var customers = app.MapGroup("/api/customers").WithTags("Клиенты");
        customers.MapGet("/", async (ShelfDb db, int? id, string? firstName, string? lastName, DateOnly? birthFrom, DateOnly? birthTo, string? email, string? address, int page = 1, int pageSize = 10) =>
        {
            if (!PageValid(page, pageSize) || birthFrom > birthTo) return Results.BadRequest(new { error = "Некорректные фильтры или страница." });
            var q = db.Customers.AsNoTracking().AsQueryable();
            if (id.HasValue) q = q.Where(x => x.Id == id);
            // SearchKey хранится в верхнем регистре; отдельные поля фильтруются на стороне приложения для одинаковой Unicode-семантики провайдеров.
            var list = await q.OrderBy(x => x.Id).ToListAsync();
            bool Contains(string value, string? term) => string.IsNullOrWhiteSpace(term) || value.Contains(term.Trim(), StringComparison.OrdinalIgnoreCase);
            list = list.Where(x => Contains(x.FirstName, firstName) && Contains(x.LastName, lastName) && Contains(x.Email, email) && Contains(x.Address, address) && (!birthFrom.HasValue || x.BirthDate >= birthFrom) && (!birthTo.HasValue || x.BirthDate <= birthTo)).ToList();
            return Results.Ok(new { items = list.Skip((page - 1) * pageSize).Take(pageSize), total = list.Count, page, pageSize });
        });
        customers.MapGet("/{id:int}", async (int id, ShelfDb db) => { var c = await db.Customers.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); return c == null ? Results.NotFound() : Results.Ok(c); });
        async Task<IResult> SaveCustomer(int? id, CustomerInput input, ShelfDb db)
        {
            var error = OrderRules.Validate(input, DateOnly.FromDateTime(DateTime.UtcNow)); if (error != null) return Results.BadRequest(new { error });
            var c = id.HasValue ? await db.Customers.FindAsync(id.Value) : new Customer(); if (c == null) return Results.NotFound();
            c.FirstName = input.FirstName.Trim(); c.LastName = input.LastName.Trim(); c.Email = input.Email.Trim(); c.Address = input.Address.Trim(); c.BirthDate = input.BirthDate; c.SearchKey = (c.FirstName + " " + c.LastName).ToUpperInvariant();
            if (!id.HasValue) db.Customers.Add(c); await db.SaveChangesAsync(); return id.HasValue ? Results.Ok(new { c.Id }) : Results.Created($"/api/customers/{c.Id}", new { c.Id });
        }
        customers.MapPost("/", (CustomerInput input, ShelfDb db) => SaveCustomer(null, input, db));
        customers.MapPut("/{id:int}", (int id, CustomerInput input, ShelfDb db) => SaveCustomer(id, input, db));
        customers.MapDelete("/{id:int}", async (int id, ShelfDb db) => { if (await db.Orders.AnyAsync(x => x.CustomerId == id)) return Results.Conflict(new { error = "Клиент связан с заказами." }); var c = await db.Customers.FindAsync(id); if (c == null) return Results.NotFound(); db.Customers.Remove(c); await db.SaveChangesAsync(); return Results.NoContent(); });
        var orders = app.MapGroup("/api/orders").WithTags("Заказы");
        orders.MapGet("/", async (ShelfDb db, int? id, int? customerId, decimal? amountMin, decimal? amountMax, DateTimeOffset? dateFrom, DateTimeOffset? dateTo, string? status, int page = 1, int pageSize = 10) =>
        {
            if (!PageValid(page, pageSize) || amountMin > amountMax || dateFrom > dateTo || (status != null && !OrderRules.Statuses.Contains(status))) return Results.BadRequest(new { error = "Некорректные фильтры или страница." });
            var q = db.Orders.AsNoTracking().Include(x => x.Customer).AsQueryable();
            if (id.HasValue) q = q.Where(x => x.Id == id); if (customerId.HasValue) q = q.Where(x => x.CustomerId == customerId); if (status != null) q = q.Where(x => x.Status == status);
            var list = await q.OrderBy(x => x.Id).ToListAsync();
            list = list.Where(x => (!amountMin.HasValue || x.Amount >= amountMin) && (!amountMax.HasValue || x.Amount <= amountMax) && (!dateFrom.HasValue || x.OrderedAt >= dateFrom) && (!dateTo.HasValue || x.OrderedAt <= dateTo)).ToList();
            return Results.Ok(new { items = list.Skip((page - 1) * pageSize).Take(pageSize).Select(OrderRules.View), total = list.Count, page, pageSize });
        });
        orders.MapGet("/{id:int}", async (int id, ShelfDb db) => { var o = await db.Orders.AsNoTracking().Include(x => x.Customer).SingleOrDefaultAsync(x => x.Id == id); return o == null ? Results.NotFound() : Results.Ok(OrderRules.View(o)); });
        async Task<IResult> SaveOrder(int? id, OrderInput input, ShelfDb db)
        {
            var error = OrderRules.Validate(input); if (error != null) return Results.BadRequest(new { error }); if (!await db.Customers.AnyAsync(x => x.Id == input.CustomerId)) return Results.BadRequest(new { error = "Клиент не найден." });
            var o = id.HasValue ? await db.Orders.FindAsync(id.Value) : new CustomerOrder(); if (o == null) return Results.NotFound(); if (id.HasValue && o.Status == "Archive") return Results.Conflict(new { error = "Архивный заказ недоступен для редактирования." });
            o.Amount = input.Amount; o.OrderedAt = input.OrderedAt.ToUniversalTime(); o.Status = input.Status; o.CustomerId = input.CustomerId; if (!id.HasValue) db.Orders.Add(o); await db.SaveChangesAsync(); return id.HasValue ? Results.Ok(new { o.Id }) : Results.Created($"/api/orders/{o.Id}", new { o.Id });
        }
        orders.MapPost("/", (OrderInput input, ShelfDb db) => SaveOrder(null, input, db)); orders.MapPut("/{id:int}", (int id, OrderInput input, ShelfDb db) => SaveOrder(id, input, db));
        orders.MapDelete("/{id:int}", async (int id, ShelfDb db) => { var o = await db.Orders.FindAsync(id); if (o == null) return Results.NotFound(); db.Orders.Remove(o); await db.SaveChangesAsync(); return Results.NoContent(); });
        app.MapGet("/api/reports/birthday-orders", async (ShelfDb db) =>
        {
            if (demo) return Results.Ok(OrderRules.Birthdays(await db.Customers.AsNoTracking().ToListAsync(), await db.Orders.AsNoTracking().ToListAsync()));
            return Results.Ok(await Procedure<BirthdayReport>(db, "get_birthday_order_totals", "birthday_cursor"));
        }).WithTags("Отчёты");
        app.MapGet("/api/reports/hourly-orders", async (ShelfDb db) =>
        {
            if (demo) return Results.Ok(OrderRules.Hourly(await db.Orders.AsNoTracking().ToListAsync()));
            return Results.Ok(await Procedure<HourlyReport>(db, "get_hourly_order_averages", "hourly_cursor"));
        }).WithTags("Отчёты");
    }
    static bool PageValid(int page, int size) => page is >= 1 and <= 1000000 && size is >= 1 and <= 100;
    static async Task<List<T>> Procedure<T>(ShelfDb db, string procedure, string cursor)
    {
        await using var tx = await db.Database.BeginTransactionAsync();
        // Имена передаются только константами из двух маршрутов выше.
        var call = procedure switch { "get_birthday_order_totals" => "CALL get_birthday_order_totals('birthday_cursor'::refcursor)", "get_hourly_order_averages" => "CALL get_hourly_order_averages('hourly_cursor'::refcursor)", _ => throw new ArgumentException("Unknown procedure") };
        var fetch = cursor switch { "birthday_cursor" => "FETCH ALL FROM birthday_cursor", "hourly_cursor" => "FETCH ALL FROM hourly_cursor", _ => throw new ArgumentException("Unknown cursor") };
        await db.Database.ExecuteSqlRawAsync(call);
        var result = await db.Database.SqlQueryRaw<T>(fetch).ToListAsync(); await tx.CommitAsync(); return result;
    }
}
