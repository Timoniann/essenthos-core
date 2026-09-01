using Essenthos.Core;
using Essenthos.Core.Configuration;
using Essenthos.Core.Database;
using Essenthos.Core.Endpoints;
using Essenthos.Core.Loading;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateSlimBuilder(args);

// The slim builder does not read user secrets, and the database password is deliberately not in
// appsettings.json, so development has nowhere else to find it.
if (builder.Environment.IsDevelopment())
{
    builder.Configuration.AddUserSecrets<Program>();
}

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(CorsOrigins.Read(builder.Configuration))
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, AppJsonSerializerContext.Default);
});

builder.Services.AddDbContext<AppDbContext>(optionsBuilder =>
{
    optionsBuilder.UseNpgsql(DatabaseConnection.Read(builder.Configuration));
});

builder.Services.AddScoped<CorpusLoader>();
builder.Services.AddScoped<CanonicalFrameLoader>();
builder.Services.AddSingleton<DatasetStatus>();
builder.Services.AddHostedService<DatasetLoader>();

var app = builder.Build();

app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var feature = context.Features.Get<IExceptionHandlerFeature>();
    app.Logger.LogError(feature?.Error, "Unhandled exception for {Path}", context.Request.Path);
    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
    context.Response.ContentType = "text/plain";
    await context.Response.WriteAsync(
        "The request could not be served. This is a fault in the API, not in the request; the cause is in " +
        "the API's own log.");
}));

var v1 = app.MapGroup("/v1");
v1.MapHealth();

app.UseCors();

app.Run();
