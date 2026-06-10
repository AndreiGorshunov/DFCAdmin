using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddRazorPages();

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.CommandTimeout(120)));

builder.Services.AddScoped<RegistrantQueryService>();
builder.Services.AddScoped<TshirtReportService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<AdminWriteService>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
