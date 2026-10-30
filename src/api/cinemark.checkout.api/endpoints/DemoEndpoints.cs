using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.interfaces;
using cinemark.checkout.shared.models;

namespace cinemark.checkout.api.endpoints;

/// <summary>
/// Endpoints só de demonstração, mapeados apenas em Development: preparam o catálogo para os cenários
/// (ex.: deixar 1 unidade para o cenário 4, mudar o preço para o cenário 10).
/// </summary>
public static class DemoEndpoints
{
    public static IEndpointRouteBuilder MapDemoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapPut("/demo/products/{id}", UpdateProductAsync)
            .WithTags("Demonstração")
            .WithSummary("Muda saldo e/ou preço de um produto (só em Development)")
            .Produces<ProductRecord>()
            .Produces<ErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces(StatusCodes.Status404NotFound);
        return app;
    }

    private static async Task<IResult> UpdateProductAsync(
        string id,
        UpdateProductRequest body,
        ITransactionRunner transactions,
        IStockRepository stock,
        ICheckoutQueries queries,
        CancellationToken cancellationToken)
    {
        if (body.AvailableQuantity is < 0)
        {
            return Results.BadRequest(new ErrorResponse("availableQuantity must be zero or greater."));
        }

        // Dinheiro tem no máximo 2 casas: 25.5 e 25.50 valem, 25.505 não.
        if (body.UnitPrice is { } price && (price <= 0 || decimal.Round(price, 2) != price))
        {
            return Results.BadRequest(new ErrorResponse("unitPrice must be greater than zero with at most 2 decimal places."));
        }

        var found = await transactions.RunAsync(
            (connection, transaction, ct) =>
                stock.UpdateProductAsync(connection, transaction, id, body.AvailableQuantity, body.UnitPrice, ct),
            cancellationToken);

        return found ? Results.Ok(await queries.GetProductAsync(id, cancellationToken)) : Results.NotFound();
    }
}
