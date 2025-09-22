using RetireWiseWebApp.Services;
using System.Runtime.InteropServices.Marshalling;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorPages();

// Configure request limits
builder.Services.Configure<IISServerOptions>(options =>
{
    options.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
});

// Register the PersistentAgentService
builder.Services.AddSingleton<PersistentAgentService>();
builder.Services.AddHttpContextAccessor();


// Add session with optimized settings
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    options.Cookie.SameSite = SameSiteMode.Strict;
});

// Add HTTP client with timeout
builder.Services.AddHttpClient();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var persistentAgentService = scope.ServiceProvider.GetRequiredService<PersistentAgentService>();
    _ = persistentAgentService.PreWarmConnectionAsync();
}
// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error");
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthorization();

// Add session middleware before MapRazorPages
app.UseSession();
app.MapRazorPages();

app.Run();
