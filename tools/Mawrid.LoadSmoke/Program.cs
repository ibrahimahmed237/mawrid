using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Net.Http.Json;

// Usage: dotnet run --project tools/Mawrid.LoadSmoke -- [baseUrl] [users] [seconds] [p95LimitMs]
// Defaults: http://localhost:8080, 20 users, 30 seconds, p95 limit 500 ms. Uses the demo buyer.
// Exits with 1 when more than 1% of requests fail or p95 is over the limit, so CI can run it after a deploy.
var baseUrl = new Uri(args.ElementAtOrDefault(0) ?? "http://localhost:8080");
var users = int.Parse(args.ElementAtOrDefault(1) ?? "20", CultureInfo.InvariantCulture);
var duration = TimeSpan.FromSeconds(int.Parse(args.ElementAtOrDefault(2) ?? "30", CultureInfo.InvariantCulture));
var p95LimitMs = double.Parse(args.ElementAtOrDefault(3) ?? "500", CultureInfo.InvariantCulture);
var email = Environment.GetEnvironmentVariable("LOADSMOKE_EMAIL") ?? "buyer@acme.test";
var password = Environment.GetEnvironmentVariable("LOADSMOKE_PASSWORD") ?? "Mawrid-Demo-2026";

using var http = new HttpClient { BaseAddress = baseUrl, Timeout = TimeSpan.FromSeconds(30) };

// One login for all virtual users: login is rate limited on purpose.
var login = await http.PostAsJsonAsync("/api/v1/auth/login", new { email, password });
login.EnsureSuccessStatusCode();
var token = (await login.Content.ReadFromJsonAsync<TokenBody>())!.AccessToken;
http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

string[] paths =
[
    "/api/v1/purchase-orders?pageSize=20",
    "/api/v1/requisitions?pageSize=20",
    "/api/v1/rfqs?pageSize=20",
    "/api/v1/suppliers?pageSize=20",
    "/health/ready",
];

var results = new System.Collections.Concurrent.ConcurrentBag<(string Path, double Ms, bool Ok)>();
var stopAt = DateTime.UtcNow + duration;
Console.WriteLine($"Load smoke: {users} users for {duration.TotalSeconds:0} s against {baseUrl}");

await Task.WhenAll(Enumerable.Range(0, users).Select(async user =>
{
    var i = user;
    while (DateTime.UtcNow < stopAt)
    {
        var path = paths[i++ % paths.Length];
        var watch = Stopwatch.StartNew();
        bool ok;
        try
        {
            using var response = await http.GetAsync(path);
            ok = response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            ok = false;
        }

        results.Add((path, watch.Elapsed.TotalMilliseconds, ok));
    }
}));

var all = results.ToList();
double Percentile(IEnumerable<double> values, double p)
{
    var sorted = values.Order().ToList();
    return sorted.Count == 0 ? 0 : sorted[(int)Math.Min(sorted.Count - 1, Math.Ceiling(p * sorted.Count) - 1)];
}

Console.WriteLine($"{"Endpoint",-40} {"Requests",9} {"Errors",7} {"p50 ms",8} {"p95 ms",8} {"p99 ms",8}");
foreach (var group in all.GroupBy(r => r.Path).OrderBy(g => g.Key))
{
    var ms = group.Select(r => r.Ms).ToList();
    Console.WriteLine($"{group.Key,-40} {group.Count(),9} {group.Count(r => !r.Ok),7} {Percentile(ms, 0.50),8:0} {Percentile(ms, 0.95),8:0} {Percentile(ms, 0.99),8:0}");
}

var errorRate = all.Count == 0 ? 1 : all.Count(r => !r.Ok) / (double)all.Count;
var p95 = Percentile(all.Select(r => r.Ms), 0.95);
Console.WriteLine($"Total: {all.Count} requests, {all.Count / duration.TotalSeconds:0} per second, errors {errorRate:P2}, p95 {p95:0} ms (limit {p95LimitMs:0} ms)");

var passed = errorRate <= 0.01 && p95 <= p95LimitMs;
Console.WriteLine(passed ? "PASSED" : "FAILED");
return passed ? 0 : 1;

internal sealed record TokenBody(string AccessToken);
