using Microsoft.EntityFrameworkCore;
using PartsPortal.Api.Data;
using PartsPortal.Api.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<ClientVersionOptions>(
    builder.Configuration.GetSection(ClientVersionOptions.SectionName));

var connectionString = builder.Configuration.GetConnectionString("PartsPortalDb");
if (!string.IsNullOrWhiteSpace(connectionString))
{
    builder.Services.AddDbContext<PartsPortalDbContext>(options =>
        options.UseMySql(
            connectionString,
            ServerVersion.AutoDetect(connectionString)));
}

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.MapGet("/", () => Results.Ok(new
{
    application = "PartsPortal.Api",
    status = "running"
}));

app.MapControllers();

app.Run();
