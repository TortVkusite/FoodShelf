using FoodShelf;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Diagnostics;

var builder = WebApplication.CreateBuilder(args);
var demo = builder.Configuration.GetValue<bool>("Demo");
builder.Services.AddDbContext<ShelfDb>(o =>
{
    if (demo) o.UseSqlite(builder.Configuration.GetConnectionString("Demo") ?? "Data Source=foodshelf-demo.db");
    else o.UseNpgsql(builder.Configuration.GetConnectionString("Default") ?? "Host=localhost;Port=5434;Database=foodshelf;Username=foodshelf;Password=foodshelf_local");
});
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();
builder.Services.AddProblemDetails();
var app = builder.Build();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
    if (error is DbUpdateException) { context.Response.StatusCode = 409; await context.Response.WriteAsJsonAsync(new { error = "Изменение конфликтует с другими данными. Обновите страницу и повторите." }); }
    else { context.Response.StatusCode = 500; await context.Response.WriteAsJsonAsync(new { error = "Не удалось выполнить запрос. Повторите попытку." }); }
}));
app.UseSwagger(); app.UseSwaggerUI();
app.UseDefaultFiles(); app.UseStaticFiles();
DateOnly Today() => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeBySystemTimeZoneId(DateTime.UtcNow, "Europe/Moscow"));

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ShelfDb>();
    await db.Database.EnsureCreatedAsync();
    await OrderModule.InitializeAsync(db, demo);
    if (!demo) await db.Database.ExecuteSqlRawAsync("""
        CREATE OR REPLACE PROCEDURE get_expiring_products(IN p_today date, IN p_days integer, INOUT p_result refcursor)
        LANGUAGE plpgsql AS $$ BEGIN
          IF p_days < 0 OR p_days > 365 THEN RAISE EXCEPTION 'days must be between 0 and 365'; END IF;
          OPEN p_result FOR SELECT * FROM "Products"
            WHERE "ExpirationDate" >= p_today AND "ExpirationDate" <= p_today + p_days ORDER BY "ExpirationDate", "Id";
        END; $$;
        """);
    if (demo && !await db.Categories.AnyAsync() && !await db.StorageLocations.AnyAsync())
    {
        var cats = new[] { new Category { Name = "Молочные продукты" }, new Category { Name = "Овощи и фрукты" }, new Category { Name = "Бакалея" } };
        var locs = new[] { new StorageLocation { Name = "Холодильник" }, new StorageLocation { Name = "Кухонный шкаф" }, new StorageLocation { Name = "Морозильник" } };
        db.AddRange(cats); db.AddRange(locs); await db.SaveChangesAsync();
        var today = Today();
        db.Products.AddRange(
            new Product { Name = "Молоко", Quantity = 1, Unit = "л", ExpirationDate = today.AddDays(2), Category = cats[0], StorageLocation = locs[0] },
            new Product { Name = "Йогурт", Quantity = 2, Unit = "шт", ExpirationDate = today.AddDays(-1), Category = cats[0], StorageLocation = locs[0] },
            new Product { Name = "Яблоки", Quantity = 1.5m, Unit = "кг", ExpirationDate = today.AddDays(7), Category = cats[1], StorageLocation = locs[0] },
            new Product { Name = "Рис", Quantity = 900, Unit = "г", ExpirationDate = today.AddDays(180), Category = cats[2], StorageLocation = locs[1] });
        await db.SaveChangesAsync();
    }
}

app.MapGet("/api/info", () => new { name = "FoodShelf", demo, today = Today(), author = "Однобурцев Никата Максимович" });
app.MapGet("/api/health", async (ShelfDb db) => await db.Database.CanConnectAsync() ? Results.Ok(new { status = "ok" }) : Results.StatusCode(503));
var products = app.MapGroup("/api/products").WithTags("Продукты");
products.MapGet("/", async (ShelfDb db, string? search, string? status, int? categoryId, int? storageLocationId, string? sort, int? id, decimal? quantityMin, decimal? quantityMax, string? unit, DateOnly? expirationFrom, DateOnly? expirationTo, int page = 1, int pageSize = 10) =>
{
    if (page < 1 || page > 1000000 || pageSize < 1 || pageSize > 100) return Results.BadRequest(new { error = "Некорректные параметры страницы." });
    if (quantityMin > quantityMax || expirationFrom > expirationTo) return Results.BadRequest(new { error = "Начало диапазона больше конца." });
    if (status != null && !new[] { "expired", "soon", "fresh" }.Contains(status)) return Results.BadRequest(new { error = "Неизвестный статус." });
    if (sort != null && !new[] { "expiration", "expiration_desc", "name" }.Contains(sort)) return Results.BadRequest(new { error = "Неизвестный порядок сортировки." });
    var today = Today(); var soon = today.AddDays(3);
    var q = db.Products.AsNoTracking().Include(x => x.Category).Include(x => x.StorageLocation).AsQueryable();
    if (id.HasValue) q = q.Where(x => x.Id == id);
    if (quantityMin.HasValue) q = q.Where(x => x.Quantity >= quantityMin);
    if (quantityMax.HasValue) q = q.Where(x => x.Quantity <= quantityMax);
    if (unit != null) q = q.Where(x => x.Unit == unit);
    if (expirationFrom.HasValue) q = q.Where(x => x.ExpirationDate >= expirationFrom);
    if (expirationTo.HasValue) q = q.Where(x => x.ExpirationDate <= expirationTo);
    if (!string.IsNullOrWhiteSpace(search)) { var term = search.Trim().ToUpperInvariant(); q = q.Where(x => x.NameKey.Contains(term)); }
    if (categoryId.HasValue) q = q.Where(x => x.CategoryId == categoryId);
    if (storageLocationId.HasValue) q = q.Where(x => x.StorageLocationId == storageLocationId);
    q = status switch { "expired" => q.Where(x => x.ExpirationDate < today), "soon" => q.Where(x => x.ExpirationDate >= today && x.ExpirationDate <= soon), "fresh" => q.Where(x => x.ExpirationDate > soon), _ => q };
    var total = await q.CountAsync();
    q = sort switch { "name" => q.OrderBy(x => x.Name).ThenBy(x => x.Id), "expiration_desc" => q.OrderByDescending(x => x.ExpirationDate).ThenBy(x => x.Id), _ => q.OrderBy(x => x.ExpirationDate).ThenBy(x => x.Id) };
    var items = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();
    return Results.Ok(new { items = items.Select(x => ShelfRules.View(x, today)), total, page, pageSize });
});
products.MapGet("/{id:int}", async (int id, ShelfDb db) =>
{
    var p = await db.Products.Include(x => x.Category).Include(x => x.StorageLocation).SingleOrDefaultAsync(x => x.Id == id);
    return p is null ? Results.NotFound() : Results.Ok(ShelfRules.View(p, Today()));
});
async Task<IResult> SaveProduct(int? id, ProductInput input, ShelfDb db)
{
    var error = ShelfRules.Validate(input);
    if (error != null) return Results.BadRequest(new { error });
    if (!await db.Categories.AnyAsync(x => x.Id == input.CategoryId) || !await db.StorageLocations.AnyAsync(x => x.Id == input.StorageLocationId)) return Results.BadRequest(new { error = "Категория или место хранения не найдены." });
    var p = id.HasValue ? await db.Products.FindAsync(id.Value) : new Product();
    if (p == null) return Results.NotFound();
    p.Name = input.Name.Trim(); p.Quantity = input.Quantity; p.Unit = input.Unit; p.ExpirationDate = input.ExpirationDate; p.CategoryId = input.CategoryId; p.StorageLocationId = input.StorageLocationId;
    if (!id.HasValue) db.Products.Add(p);
    await db.SaveChangesAsync();
    return id.HasValue ? Results.Ok(new { p.Id }) : Results.Created($"/api/products/{p.Id}", new { p.Id });
}
products.MapPost("/", (ProductInput input, ShelfDb db) => SaveProduct(null, input, db));
products.MapPut("/{id:int}", (int id, ProductInput input, ShelfDb db) => SaveProduct(id, input, db));
products.MapDelete("/{id:int}", async (int id, ShelfDb db) =>
{
    var p = await db.Products.FindAsync(id); if (p == null) return Results.NotFound();
    db.Products.Remove(p); await db.SaveChangesAsync(); return Results.NoContent();
});
app.MapGet("/api/statistics", async (ShelfDb db) =>
{
    var today = Today(); var soon = today.AddDays(3);
    return Results.Ok(new
    {
        total = await db.Products.CountAsync(),
        expired = await db.Products.CountAsync(x => x.ExpirationDate < today),
        soon = await db.Products.CountAsync(x => x.ExpirationDate >= today && x.ExpirationDate <= soon),
        fresh = await db.Products.CountAsync(x => x.ExpirationDate > soon),
        categories = await db.Categories.Select(c => new { c.Id, c.Name, count = db.Products.Count(p => p.CategoryId == c.Id) }).ToListAsync()
    });
}).WithTags("Статистика");
app.MapGet("/api/products/expiring", async (ShelfDb db, int days = 3) =>
{
    if (days < 0 || days > 365) return Results.BadRequest(new { error = "Период должен быть от 0 до 365 дней." });
    var today = Today(); List<Product> items;
    if (demo) items = await db.Products.AsNoTracking().Where(x => x.ExpirationDate >= today && x.ExpirationDate <= today.AddDays(days)).OrderBy(x => x.ExpirationDate).ThenBy(x => x.Id).ToListAsync();
    else
    {
        // refcursor существует только внутри транзакции. Дата передаётся из приложения.
        await using var tx = await db.Database.BeginTransactionAsync();
        var cursor = "expiring_cursor";
        await db.Database.ExecuteSqlInterpolatedAsync($"CALL get_expiring_products({today}, {days}, {cursor}::refcursor)");
        items = await db.Products.FromSqlRaw("FETCH ALL FROM expiring_cursor").AsNoTracking().ToListAsync();
        await tx.CommitAsync();
    }
    var cats = await db.Categories.ToDictionaryAsync(x => x.Id); var locations = await db.StorageLocations.ToDictionaryAsync(x => x.Id);
    foreach (var p in items) { p.Category = cats[p.CategoryId]; p.StorageLocation = locations[p.StorageLocationId]; }
    return Results.Ok(items.Select(p => ShelfRules.View(p, today)));
}).WithTags("Продукты");

foreach (var kind in new[] { "categories", "locations" })
{
    var isCategory = kind == "categories";
    var group = app.MapGroup($"/api/{kind}").WithTags(isCategory ? "Категории" : "Места хранения");
    group.MapGet("/", async (ShelfDb db, int? id, string? name, int? page, int pageSize = 10) =>
    {
        if (page is < 1 or > 1000000 || pageSize < 1 || pageSize > 100) return Results.BadRequest(new { error = "Некорректные параметры страницы." });
        var key = name?.Trim().ToUpperInvariant();
        if (isCategory)
        {
            var q = db.Categories.AsNoTracking().Where(x => (!id.HasValue || x.Id == id) && (key == null || x.NameKey.Contains(key))).OrderBy(x => x.Name).ThenBy(x => x.Id);
            if (!page.HasValue) return Results.Ok(await q.ToListAsync());
            return Results.Ok(new { items = await q.Skip((page.Value - 1) * pageSize).Take(pageSize).ToListAsync(), total = await q.CountAsync(), page, pageSize });
        }
        else
        {
            var q = db.StorageLocations.AsNoTracking().Where(x => (!id.HasValue || x.Id == id) && (key == null || x.NameKey.Contains(key))).OrderBy(x => x.Name).ThenBy(x => x.Id);
            if (!page.HasValue) return Results.Ok(await q.ToListAsync());
            return Results.Ok(new { items = await q.Skip((page.Value - 1) * pageSize).Take(pageSize).ToListAsync(), total = await q.CountAsync(), page, pageSize });
        }
    });
    group.MapGet("/{id:int}", async (int id, ShelfDb db) =>
    {
        if (isCategory) { var e = await db.Categories.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); return e == null ? Results.NotFound() : Results.Ok(e); }
        else { var e = await db.StorageLocations.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id); return e == null ? Results.NotFound() : Results.Ok(e); }
    });
    async Task<IResult> Save(int? id, NameInput input, ShelfDb db)
    {
        var name = input.Name?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 60) return Results.BadRequest(new { error = "Название должно содержать от 1 до 60 символов." });
        var key = name.ToUpperInvariant();
        var exists = isCategory ? await db.Categories.AnyAsync(x => x.NameKey == key && x.Id != id) : await db.StorageLocations.AnyAsync(x => x.NameKey == key && x.Id != id);
        if (exists) return Results.Conflict(new { error = "Такое название уже существует." });
        int resultId;
        if (isCategory) { var e = id.HasValue ? await db.Categories.FindAsync(id.Value) : new Category(); if (e == null) return Results.NotFound(); e.Name = name; if (!id.HasValue) db.Categories.Add(e); await db.SaveChangesAsync(); resultId = e.Id; }
        else { var e = id.HasValue ? await db.StorageLocations.FindAsync(id.Value) : new StorageLocation(); if (e == null) return Results.NotFound(); e.Name = name; if (!id.HasValue) db.StorageLocations.Add(e); await db.SaveChangesAsync(); resultId = e.Id; }
        return id.HasValue ? Results.Ok(new { id = resultId }) : Results.Created($"/api/{kind}/{resultId}", new { id = resultId });
    }
    group.MapPost("/", (NameInput input, ShelfDb db) => Save(null, input, db));
    group.MapPut("/{id:int}", (int id, NameInput input, ShelfDb db) => Save(id, input, db));
    group.MapDelete("/{id:int}", async (int id, ShelfDb db) =>
    {
        var used = isCategory ? await db.Products.AnyAsync(x => x.CategoryId == id) : await db.Products.AnyAsync(x => x.StorageLocationId == id);
        if (used) return Results.Conflict(new { error = "Сначала перенесите или удалите связанные продукты." });
        if (isCategory) { var e = await db.Categories.FindAsync(id); if (e == null) return Results.NotFound(); db.Categories.Remove(e); }
        else { var e = await db.StorageLocations.FindAsync(id); if (e == null) return Results.NotFound(); db.StorageLocations.Remove(e); }
        await db.SaveChangesAsync(); return Results.NoContent();
    });
}
app.MapOrderModule(demo);
app.MapGet("/api/reports/inventory", async (ShelfDb db) =>
{
    if (demo) return Results.Ok(await db.Categories.Select(c => new InventoryReport { CategoryId = c.Id, Name = c.Name, ProductCount = db.Products.Count(p => p.CategoryId == c.Id) }).ToListAsync());
    await using var tx = await db.Database.BeginTransactionAsync();
    await db.Database.ExecuteSqlRawAsync("CALL get_inventory_summary('inventory_cursor'::refcursor)");
    var result = await db.Database.SqlQueryRaw<InventoryReport>("FETCH ALL FROM inventory_cursor").ToListAsync();
    await tx.CommitAsync(); return Results.Ok(result);
}).WithTags("Отчёты");
app.MapFallbackToFile("index.html");
app.Run();
public partial class Program;
