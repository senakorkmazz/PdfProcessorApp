using PdfProcessorApp.Services;
using Microsoft.Extensions.FileProviders;
using System.IO;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

builder.Services.AddSingleton<ProcessingStatusService>();
builder.Services.AddSingleton<KafkaProducerService>();
builder.Services.AddHostedService<KafkaConsumerService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles(); 

app.UseStaticFiles(new StaticFileOptions
{
    FileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(
        Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "output")),
    RequestPath = "/output",
    OnPrepareResponse = ctx =>
    {
        var path = ctx.File.PhysicalPath;
        var ext = Path.GetExtension(path).ToLowerInvariant();
        
        if (ext == ".txt")
            ctx.Context.Response.ContentType = "text/plain; charset=utf-8";
        else if (ext == ".docx")
            ctx.Context.Response.ContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
        
        ctx.Context.Response.Headers.Append("Cache-Control", "no-cache, no-store");
        ctx.Context.Response.Headers.Append("Pragma", "no-cache");
        ctx.Context.Response.Headers.Append("Expires", "-1");
    }
});

app.UseRouting();

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Pdf}/{action=Index}/{id?}");

app.MapRazorPages();

app.Run();
