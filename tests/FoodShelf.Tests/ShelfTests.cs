using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodShelf;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FoodShelf.Tests;

public class RulesTests
{
    private readonly DateOnly today = new(2026, 11, 12);
    [Theory]
    [InlineData(-1, "expired")]
    [InlineData(0, "soon")]
    [InlineData(3, "soon")]
    [InlineData(4, "fresh")]
    public void ClassifiesBoundaryDates(int days, string expected) => Assert.Equal(expected, ShelfRules.Status(today.AddDays(days), today));
    [Fact] public void HandlesMonthBoundary() => Assert.Equal(1, ShelfRules.DaysLeft(new(2027, 1, 1), new(2026, 12, 31)));
    [Fact] public void HandlesLeapDay() => Assert.Equal(2, ShelfRules.DaysLeft(new(2028, 3, 1), new(2028, 2, 28)));
    private ProductInput Valid => new("Молоко", 1, "л", today, 1, 1);
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1000001)]
    public void RejectsInvalidQuantity(int amount) => Assert.NotNull(ShelfRules.Validate(Valid with { Quantity = amount }));
    [Fact] public void RejectsEmptyName() => Assert.NotNull(ShelfRules.Validate(Valid with { Name = "  " }));
    [Fact] public void RejectsTooManyDecimalPlaces() => Assert.NotNull(ShelfRules.Validate(Valid with { Quantity = 0.0001m }));
    [Fact] public void AcceptsFractionalQuantity() => Assert.Null(ShelfRules.Validate(Valid with { Quantity = 1.125m }));
    [Fact] public void AcceptsExpiredDateForInventory() => Assert.Null(ShelfRules.Validate(Valid with { ExpirationDate = today.AddDays(-10) }));
    [Fact] public void RequiresDate() => Assert.NotNull(ShelfRules.Validate(Valid with { ExpirationDate = default }));
}

public class AppFactory : WebApplicationFactory<Program>
{
    readonly string path = Path.Combine(Path.GetTempPath(), $"foodshelf-tests-{Guid.NewGuid():N}.db");
    protected override void ConfigureWebHost(IWebHostBuilder builder) => builder.UseSetting("Demo", "true").UseSetting("ConnectionStrings:Demo", $"Data Source={path};Pooling=False");
    protected override void Dispose(bool disposing) { base.Dispose(disposing); if (disposing) foreach (var suffix in new[] { "", "-wal", "-shm" }) if (File.Exists(path + suffix)) File.Delete(path + suffix); }
}
public class ApiTests : IClassFixture<AppFactory>
{
    readonly HttpClient client;
    public ApiTests(AppFactory factory) => client = factory.CreateClient();
    [Fact]
    public async Task ProductLifecycleAndReferenceProtection()
    {
        var categoryResponse = await client.PostAsJsonAsync("/api/categories", new { Name = "Тестовая категория" });
        Assert.Equal(HttpStatusCode.Created, categoryResponse.StatusCode);
        int category = (await categoryResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var locationResponse = await client.PostAsJsonAsync("/api/locations", new { Name = "Тестовое место" });
        int location = (await locationResponse.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var input = new ProductInput("Проверочный продукт", 1.5m, "кг", new(2030, 1, 1), category, location);
        var response = await client.PostAsJsonAsync("/api/products", input);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        int id = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        var product = await client.GetFromJsonAsync<ProductView>($"/api/products/{id}");
        Assert.Equal(1.5m, product!.Quantity); Assert.Equal("Тестовая категория", product.Category);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/categories/{category}")).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/locations/{location}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/products/{id}", input with { Quantity = 2 })).StatusCode);
        Assert.Equal(2, (await client.GetFromJsonAsync<ProductView>($"/api/products/{id}"))!.Quantity);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/products/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/products/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/categories/{category}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/locations/{location}")).StatusCode);
    }
    [Fact]
    public async Task FiltersStatusAndPaginates()
    {
        var data = await client.GetFromJsonAsync<JsonElement>("/api/products?status=expired&pageSize=1");
        Assert.Single(data.GetProperty("items").EnumerateArray());
        Assert.Equal("expired", data.GetProperty("items")[0].GetProperty("status").GetString());
        var search = await client.GetFromJsonAsync<JsonElement>("/api/products?search=" + Uri.EscapeDataString("МОЛОКО"));
        Assert.Equal(1, search.GetProperty("total").GetInt32());
    }
    [Fact]
    public async Task RejectsMissingReferences()
    {
        var input = new ProductInput("Тест", 1, "шт", new(2030, 1, 1), 999999, 999999);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/products", input)).StatusCode);
    }
    [Fact]
    public async Task RejectsCaseInsensitiveRussianDuplicates()
    {
        var created = await client.PostAsJsonAsync("/api/categories", new { Name = "Дубликат" });
        int id = (await created.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32();
        try { Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/categories", new { Name = "ДУБЛИКАТ" })).StatusCode); }
        finally { await client.DeleteAsync($"/api/categories/{id}"); }
    }
    [Theory]
    [InlineData("/api/products?page=0")]
    [InlineData("/api/products?pageSize=101")]
    [InlineData("/api/products?status=oops")]
    [InlineData("/api/products/expiring?days=-1")]
    public async Task RejectsBadFilters(string url) => Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync(url)).StatusCode);
    [Fact]
    public async Task ExpiringEndpointAndStatsMatch()
    {
        var items = await client.GetFromJsonAsync<ProductView[]>("/api/products/expiring?days=3");
        var stats = await client.GetFromJsonAsync<JsonElement>("/api/statistics");
        Assert.All(items!, x => Assert.InRange(x.DaysLeft, 0, 3));
        Assert.Equal(stats.GetProperty("soon").GetInt32(), items!.Length);
    }
    [Fact]
    public async Task SwaggerDescribesProductCreation()
    {
        var spec = await client.GetFromJsonAsync<JsonElement>("/swagger/v1/swagger.json");
        Assert.Contains(spec.GetProperty("paths").EnumerateObject(), x => x.Name.StartsWith("/api/products") && x.Value.TryGetProperty("post", out _));
    }
}
