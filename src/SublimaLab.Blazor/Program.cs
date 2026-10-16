using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using SublimaLab.Blazor.Services;

// Root components (the router and head outlet) are placed by the server-rendered page.
var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Same-origin API: the browser attaches the HttpOnly auth cookie automatically.
builder.Services.AddScoped(_ => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddStorefront();

await builder.Build().RunAsync();
