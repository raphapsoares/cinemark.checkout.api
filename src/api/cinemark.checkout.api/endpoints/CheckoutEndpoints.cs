using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.api.validators;
using cinemark.checkout.shared.exceptions;
using cinemark.checkout.shared.models;
using Microsoft.AspNetCore.Mvc;

namespace cinemark.checkout.api.endpoints;

public static class CheckoutEndpoints
{
    public static IEndpointRouteBuilder MapCheckoutEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPost("/checkout", CheckoutAsync)
            .WithTags("Checkout")
            .WithSummary("Reserva o estoque e cobra no provedor de cartão")
            .WithDescription(
                "201 confirmado; 200 mesma chave, pedido já concluído; 409 mesma chave, ainda em andamento; " +
                "422 STOCK_INSUFFICIENT ou PAYMENT_DECLINED; 503 PAYMENT_UNAVAILABLE (retries esgotados ou disjuntor aberto); " +
                "400 entrada inválida.")
            .Produces<CheckoutResponse>(StatusCodes.Status201Created)
            .Produces<CheckoutResponse>(StatusCodes.Status200OK)
            .Produces<CheckoutResponse>(StatusCodes.Status409Conflict)
            .Produces<CheckoutResponse>(StatusCodes.Status422UnprocessableEntity)
            .Produces<CheckoutResponse>(StatusCodes.Status503ServiceUnavailable)
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest);

        // Leitura, para conferir o resultado de cada cenário: pedido com itens, reservas e pagamento; saldo e preço.
        app.MapGet("/orders/{id:guid}", GetOrderAsync)
            .WithTags("Consulta")
            .WithSummary("Pedido com itens, reservas e pagamento")
            .Produces<OrderDetails>()
            .Produces(StatusCodes.Status404NotFound);
        app.MapGet("/products/{id}", GetProductAsync)
            .WithTags("Consulta")
            .WithSummary("Saldo e preço atual de um produto (ex.: ingresso-inteira)")
            .Produces<ProductRecord>()
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<IResult> CheckoutAsync(
        [FromHeader(Name = CheckoutRequestValidator.IdempotencyKeyHeader)] string? idempotencyKey,
        [FromBody] CheckoutRequest? body,
        ICheckoutService checkoutService)
    {
        idempotencyKey = idempotencyKey?.Trim();
        if (!CheckoutRequestValidator.TryValidate(idempotencyKey, body, out var error))
        {
            return Results.BadRequest(new ErrorResponse(error));
        }

        try
        {
            // Sem cancelamento: se o cliente desconectar no meio, o checkout termina e grava o resultado.
            var result = await checkoutService.CheckoutAsync(idempotencyKey, body, CancellationToken.None);
            return CheckoutResponse.From(result);
        }
        catch (UnknownProductException ex)
        {
            return Results.BadRequest(new ErrorResponse(ex.Message));
        }
    }

    private static async Task<IResult> GetOrderAsync(Guid id, ICheckoutQueries queries, CancellationToken cancellationToken) =>
        await queries.GetOrderAsync(id, cancellationToken) is { } order ? Results.Ok(order) : Results.NotFound();

    private static async Task<IResult> GetProductAsync(string id, ICheckoutQueries queries, CancellationToken cancellationToken) =>
        await queries.GetProductAsync(id, cancellationToken) is { } product ? Results.Ok(product) : Results.NotFound();
}
