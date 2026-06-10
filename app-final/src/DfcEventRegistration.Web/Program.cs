using DfcEventRegistration.Web.Data;
using DfcEventRegistration.Web.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var razorPages = builder.Services.AddRazorPages();

// В Development включаем Razor Runtime Compilation: правки .cshtml применяются
// без пересборки/перезапуска (refresh в браузере достаточно). В проде НЕ включаем —
// там вьюхи компилируются в сборку (быстрее старт, нет лишней зависимости и file watcher'а).
if (builder.Environment.IsDevelopment())
{
    razorPages.AddRazorRuntimeCompilation();
}

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("Default"),
        sql => sql.CommandTimeout(120)));

builder.Services.AddScoped<RegistrantQueryService>();
builder.Services.AddScoped<ChildQueryService>();
builder.Services.AddScoped<TshirtReportService>();
builder.Services.AddScoped<ExcelExportService>();
builder.Services.AddScoped<StreamingExportService>();
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
