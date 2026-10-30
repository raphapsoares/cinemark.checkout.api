using System.Text.Json;
using System.Text.Json.Serialization;
using cinemark.checkout.api;
using cinemark.checkout.api.endpoints;
using cinemark.checkout.api.swagger;
using cinemark.checkout.shared.data;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCheckout(builder.Configuration, builder.Environment.ContentRootPath);

// Enums saem como texto no JSON (CONFIRMED, RESERVED, DECLINED); no banco continuam gravados como inteiros.
var enumAsText = new JsonStringEnumConverter(JsonNamingPolicy.SnakeCaseUpper);
builder.Services.ConfigureHttpJsonOptions(options => options.SerializerOptions.Converters.Add(enumAsText));

// O Swagger lê as opções JSON do MVC: o mesmo conversor faz a documentação mostrar os enums como texto.
builder.Services.Configure<Microsoft.AspNetCore.Mvc.JsonOptions>(options => options.JsonSerializerOptions.Converters.Add(enumAsText));

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    // O roteiro dos cenários aparece no topo da página do Swagger.
    options.SwaggerDoc("v1", new OpenApiInfo { Title = "cinemark.checkout", Version = "v1", Description = ScenarioGuide.Description });
    options.OperationFilter<ScenarioExamplesFilter>();
});

var app = builder.Build();

app.MapCheckoutEndpoints();

// Swagger, banco recriado a cada start e endpoints de demonstração: só em Development (o perfil do dotnet run).
if (app.Environment.IsDevelopment())
{
    var database = app.Services.GetRequiredService<IOptions<DatabaseOptions>>().Value;
    if (database.ResetOnStartup)
    {
        SqliteDatabaseReset.Recreate(database.DatabasePath);
        app.Logger.LogInformation("Banco recriado a partir de schema.sql e seed.sql: {DatabasePath}", database.DatabasePath);
    }

    app.UseSwagger();
    app.UseSwaggerUI();
    app.MapDemoEndpoints();
}

app.Run();

// Exposto para os testes de integração (WebApplicationFactory<Program>).
public partial class Program
{
}
