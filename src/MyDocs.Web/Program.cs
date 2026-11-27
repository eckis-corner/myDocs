using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using MyDocs.ApiClient;
using MyDocs.UI;
using MyDocs.UI.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

builder.Services.AddMyDocsUi();
builder.Services.AddMyDocsApi(new Uri(builder.HostEnvironment.BaseAddress));

var host = builder.Build();

await host.Services.GetRequiredService<CultureService>().ApplyStoredCultureAsync();

await host.RunAsync();
