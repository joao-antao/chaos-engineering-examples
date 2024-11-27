using Microsoft.Net.Http.Headers;
using Polly;
using Polly.Simmy;
using System.Net.Mime;
using System.Net;
using Pandemonium;
using Polly.Retry;
using Polly.CircuitBreaker;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddHttpClient("fake-http-client", (sp, httpClient) =>
{
    httpClient.BaseAddress = new Uri("https://jsonplaceholder.typicode.com/");
    httpClient.DefaultRequestHeaders.Add(HeaderNames.Accept, MediaTypeNames.Application.Json);
    httpClient.DefaultRequestHeaders.Add(HeaderNames.AcceptEncoding, "deflate, gzip");
}).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
{
    AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
}).AddResilienceHandler("pandemonium-pipeline", resiliencePipelineBuilder =>
 {
     resiliencePipelineBuilder
         .AddConcurrencyLimiter(10, 100)
         .AddRetry(new RetryStrategyOptions<HttpResponseMessage> { })
         .AddCircuitBreaker(new CircuitBreakerStrategyOptions<HttpResponseMessage> { })
         .AddTimeout(TimeSpan.FromSeconds(5));

     // Inject chaos into 50% of invocations
     const double injectionRate = 0.5;

     resiliencePipelineBuilder
         .AddChaosLatency(injectionRate, TimeSpan.FromSeconds(3))
         .AddChaosFault(injectionRate, () => new InvalidOperationException("Chaos fault!"))
         .AddChaosOutcome(injectionRate, () => new HttpResponseMessage(HttpStatusCode.InternalServerError));
 });

var app = builder.Build();
app.UseHttpsRedirection();
app.MapGet("pandemonium", async (IHttpClientFactory factory, CancellationToken cancellationToken) =>
{
    var httpClient = factory.CreateClient("fake-http-client");
    var response = await httpClient.GetAsync("https://jsonplaceholder.typicode.com/posts/1", cancellationToken);
    response.EnsureSuccessStatusCode();
    return await response.Content.ReadFromJsonAsync<Post>();
});

app.Run();