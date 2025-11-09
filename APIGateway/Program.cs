var builder = WebApplication.CreateBuilder(args);

// Configure YARP Reverse Proxy
builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));
builder.WebHost.ConfigureKestrel(options =>
{
    options.AllowSynchronousIO = true;
});
var app = builder.Build();

app.MapGet("/", () => "✅ API Gateway is running");
app.UseHttpsRedirection();
// Enable reverse proxy routes
app.MapReverseProxy();

app.Run();
