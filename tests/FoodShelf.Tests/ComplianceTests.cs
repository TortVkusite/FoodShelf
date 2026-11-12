using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FoodShelf;
using Xunit;

namespace FoodShelf.Tests;

public class OrderRulesTests
{
    [Fact]
    public void BirthdayTotalExcludesCancelledAndOtherDates()
    {
        var c = new Customer { Id = 1, FirstName = "Анна", LastName = "Тест", BirthDate = new(2000, 11, 12) };
        var orders = new[] { new CustomerOrder { CustomerId = 1, Amount = 100, Status = "Completed", OrderedAt = new(2025, 11, 12, 12, 0, 0, TimeSpan.FromHours(3)) }, new CustomerOrder { CustomerId = 1, Amount = 200, Status = "Cancelled", OrderedAt = new(2026, 11, 12, 12, 0, 0, TimeSpan.FromHours(3)) }, new CustomerOrder { CustomerId = 1, Amount = 300, Status = "Completed", OrderedAt = new(2026, 11, 13, 12, 0, 0, TimeSpan.FromHours(3)) } };
        Assert.Equal(100, Assert.Single(OrderRules.Birthdays([c], orders)).Total);
    }
    [Fact]
    public void HourlyReportContains24HoursAndMoscowTimezone()
    {
        var result = OrderRules.Hourly([new CustomerOrder { Amount = 100, Status = "Completed", OrderedAt = new(2026, 11, 12, 21, 30, 0, TimeSpan.Zero) }, new CustomerOrder { Amount = 300, Status = "Completed", OrderedAt = new(2026, 11, 13, 0, 15, 0, TimeSpan.FromHours(3)) }]);
        Assert.Equal(24, result.Count); Assert.Equal(0, result[0].Hour); Assert.Equal(200, result[0].Average); Assert.Equal(2, result[0].Count);
    }
    [Fact] public void EmptyHourlyReportHasZeros() => Assert.All(OrderRules.Hourly([]), r => { Assert.Equal(0, r.Average); Assert.Equal(0, r.Count); });
    [Theory][InlineData(0)][InlineData(-1)] public void RejectsNonpositiveAmount(int amount) => Assert.NotNull(OrderRules.Validate(new OrderInput(amount, DateTimeOffset.UtcNow, "Pending", 1)));
    [Fact] public void RejectsUnknownStatus() => Assert.NotNull(OrderRules.Validate(new OrderInput(10, DateTimeOffset.UtcNow, "Unknown", 1)));
    [Fact] public void RejectsFutureBirthday() => Assert.NotNull(OrderRules.Validate(new CustomerInput("А", "Б", new(2030, 1, 1), "a@b.ru", "Брянск"), new(2026, 11, 12)));
}
public class ComplianceApiTests : IClassFixture<AppFactory>
{
    readonly HttpClient client; public ComplianceApiTests(AppFactory f) => client = f.CreateClient();
    static async Task<int> Id(HttpResponseMessage response) { Assert.Equal(HttpStatusCode.Created, response.StatusCode); return (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetInt32(); }
    [Fact]
    public async Task OrderAndCustomerCrudFilteringReportsAndArchive()
    {
        var customer = new CustomerInput("Регина", "Проверка", new(2000, 11, 12), "test@example.com", "Брянск");
        var cid = await Id(await client.PostAsJsonAsync("/api/customers", customer));
        var input = new OrderInput(123.45m, new(2026, 11, 12, 12, 0, 0, TimeSpan.FromHours(3)), "Completed", cid);
        var oid = await Id(await client.PostAsJsonAsync("/api/orders", input));
        try
        {
            var item = await client.GetFromJsonAsync<OrderView>($"/api/orders/{oid}"); Assert.Equal(123.45m, item!.Amount);
            Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/customers/{cid}")).StatusCode);
            var filtered = await client.GetFromJsonAsync<JsonElement>($"/api/orders?customerId={cid}&amountMin=120&amountMax=125&status=Completed&dateFrom=2026-11-12T00:00:00Z&dateTo=2026-11-13T00:00:00Z");
            Assert.Equal(1, filtered.GetProperty("total").GetInt32());
            var customers = await client.GetFromJsonAsync<JsonElement>("/api/customers?firstName=" + Uri.EscapeDataString("РЕГИНА")); Assert.Equal(1, customers.GetProperty("total").GetInt32());
            var report = await client.GetFromJsonAsync<List<BirthdayReport>>("/api/reports/birthday-orders"); Assert.Equal(123.45m, report!.Single(x => x.CustomerId == cid).Total);
            Assert.Equal(24, (await client.GetFromJsonAsync<List<HourlyReport>>("/api/reports/hourly-orders"))!.Count);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/customers/{cid}", customer with { Address = "Новый адрес" })).StatusCode);
            Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/orders/{oid}", input with { Status = "Archive" })).StatusCode);
            Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/orders/{oid}", input)).StatusCode);
        }
        finally { await client.DeleteAsync($"/api/orders/{oid}"); await client.DeleteAsync($"/api/customers/{cid}"); }
    }
    [Theory]
    [InlineData("categories")]
    [InlineData("locations")]
    public async Task ReferenceSupportsIdAndPage(string kind)
    {
        var first = (await client.GetFromJsonAsync<JsonElement>($"/api/{kind}?page=1&pageSize=1")).GetProperty("items")[0].GetProperty("id").GetInt32();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/{kind}/{first}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/{kind}/9999999")).StatusCode);
    }
    [Fact]
    public async Task ProductFiltersQuantityUnitAndDates()
    {
        var response = await client.GetAsync("/api/products?quantityMin=0.5&quantityMax=2&unit=" + Uri.EscapeDataString("л") + "&expirationFrom=2000-01-01&expirationTo=2099-01-01"); response.EnsureSuccessStatusCode();
        var data = await response.Content.ReadFromJsonAsync<JsonElement>(); Assert.Equal(1, data.GetProperty("total").GetInt32());
    }
}
