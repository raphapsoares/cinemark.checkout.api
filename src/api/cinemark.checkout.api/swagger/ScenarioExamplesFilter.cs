using cinemark.checkout.api.requests;
using cinemark.checkout.api.validators;
using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace cinemark.checkout.api.swagger;

/// <summary>
/// Deixa o Swagger pronto para a demonstração: exemplos de corpo por cenário (lista "Examples"), chave de
/// idempotência preenchida e o cabeçalho X-Simulate-Payment como lista de opções do provedor simulado.
/// </summary>
public sealed class ScenarioExamplesFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        switch (context.ApiDescription.HttpMethod, context.ApiDescription.RelativePath)
        {
            case ("POST", "checkout"):
                DescribeCheckout(operation);
                break;
            case ("PUT", "demo/products/{id}"):
                SetIdExample(operation, "ingresso-inteira", "Cenário 10: ingresso-inteira. Cenário 4: poltrona-vip.");
                SetBodyExamples(
                    operation,
                    ("cenario10", "Cenário 10: preço muda para 50.00", Body(("unitPrice", new OpenApiDouble(50.00)))),
                    ("cenario4", "Cenário 4: deixa só 1 unidade (use id poltrona-vip)", Body(("availableQuantity", new OpenApiInteger(1)))));
                break;
            case ("GET", "products/{id}"):
                SetIdExample(operation, "ingresso-inteira", "Produtos do seed: ingresso-inteira, ingresso-meia, combo-pipoca, poltrona-vip.");
                break;
        }
    }

    private static void DescribeCheckout(OpenApiOperation operation)
    {
        if (operation.Parameters.FirstOrDefault(p => p.Name == CheckoutRequestValidator.IdempotencyKeyHeader) is { } key)
        {
            key.Required = true;
            key.Example = new OpenApiString("demo-1");
            key.Description = "Troque o valor a cada compra nova (demo-1, demo-2…). Repetir o mesmo valor devolve o pedido já criado (cenário 5).";
        }

        operation.Parameters.Add(new OpenApiParameter
        {
            Name = FakePaymentHandler.SimulateHeader,
            In = ParameterLocation.Header,
            Required = false,
            Description = "Força o resultado do provedor simulado. Vazio: aprova. decline: cartão recusado (cenário 3). " +
                          "timeout: provedor lento, ~9 s (cenário 7). unavailable: provedor fora do ar (cenário 7).",
            Schema = new OpenApiSchema
            {
                Type = "string",
                Enum =
                [
                    new OpenApiString(FakePaymentHandler.SimulateDecline),
                    new OpenApiString(FakePaymentHandler.SimulateTimeout),
                    new OpenApiString(FakePaymentHandler.SimulateUnavailable),
                ],
            },
        });

        SetBodyExamples(
            operation,
            ("cenario1", "Cenário 1 / 5 / 10: compra feliz (2 ingressos, 80.00)", Checkout("ingresso-inteira", 2)),
            ("cenario2", "Cenário 2: estoque insuficiente (999 poltronas)", Checkout("poltrona-vip", 999)),
            ("cenario3", "Cenário 3: cartão recusado (use X-Simulate-Payment = decline)", Checkout("combo-pipoca", 2)),
            ("cenario7", "Cenário 7: provedor lento (use X-Simulate-Payment = timeout)", Checkout("combo-pipoca", 1)));
    }

    private static OpenApiObject Checkout(string productId, int quantity) => new()
    {
        ["customerId"] = new OpenApiString("c-1"),
        ["items"] = new OpenApiArray { Body(("productId", new OpenApiString(productId)), ("quantity", new OpenApiInteger(quantity))) },
    };

    private static OpenApiObject Body(params (string Name, IOpenApiAny Value)[] fields)
    {
        var body = new OpenApiObject();
        foreach (var (name, value) in fields)
        {
            body[name] = value;
        }

        return body;
    }

    private static void SetBodyExamples(OpenApiOperation operation, params (string Key, string Summary, IOpenApiAny Value)[] examples)
    {
        if (operation.RequestBody?.Content.TryGetValue("application/json", out var json) != true)
        {
            return;
        }

        // Com vários exemplos, o Swagger mostra a lista "Examples"; o primeiro já vem selecionado.
        json!.Example = null;
        json.Examples = examples.ToDictionary(
            example => example.Key,
            example => new OpenApiExample { Summary = example.Summary, Value = example.Value });
    }

    private static void SetIdExample(OpenApiOperation operation, string id, string description)
    {
        if (operation.Parameters.FirstOrDefault(p => p.Name == "id") is { } parameter)
        {
            parameter.Example = new OpenApiString(id);
            parameter.Description = description;
        }
    }
}
