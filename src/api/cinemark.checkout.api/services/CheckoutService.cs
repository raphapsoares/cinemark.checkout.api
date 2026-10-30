using cinemark.checkout.api.interfaces;
using cinemark.checkout.api.models;
using cinemark.checkout.shared.enums;
using cinemark.checkout.shared.exceptions;
using cinemark.checkout.shared.interfaces;
using Microsoft.Data.Sqlite;

namespace cinemark.checkout.api.services;

public sealed class CheckoutService : ICheckoutService
{
    public static readonly TimeSpan ReservationTtl = TimeSpan.FromMinutes(15);

    private readonly ITransactionRunner _transactions;
    private readonly IOrderRepository _orders;
    private readonly IStockRepository _stock;
    private readonly IPaymentRepository _payments;
    private readonly IPaymentProvider _paymentProvider;
    private readonly TimeProvider _timeProvider;

    public CheckoutService(
        ITransactionRunner transactions,
        IOrderRepository orders,
        IStockRepository stock,
        IPaymentRepository payments,
        IPaymentProvider paymentProvider,
        TimeProvider timeProvider)
    {
        _transactions = transactions;
        _orders = orders;
        _stock = stock;
        _payments = payments;
        _paymentProvider = paymentProvider;
        _timeProvider = timeProvider;
    }

    public async Task<CheckoutResult> CheckoutAsync(string idempotencyKey, CheckoutRequest request, CancellationToken cancellationToken)
    {
        // Etapa 1: numa transação, cria o pedido PENDING e reserva o estoque.
        // Mesma chave de idempotência: devolve o pedido já existente, sem reservar de novo.
        var (order, early) = await _transactions.RunAsync(
            (connection, transaction, ct) => CreateOrderAndReserveStockAsync(connection, transaction, idempotencyKey, request, ct),
            cancellationToken);
        if (early is not null)
        {
            return early;
        }

        // Etapa 2: cobra no provedor, fora da transação (chamada externa lenta e instável, com retry e circuit breaker).
        var payment = await ChargeAsync(order, cancellationToken);

        // Etapa 3: numa transação, grava o pagamento e confirma o pedido ou compensa (devolve o estoque e cancela).
        // Sem cancelamento: uma vez cobrado, o resultado precisa ser gravado.
        return await _transactions.RunAsync(
            (connection, transaction, ct) => CompleteAsync(connection, transaction, order, payment, ct),
            CancellationToken.None);
    }

    /// <summary>Early vem null quando o estoque foi reservado e o checkout segue para o pagamento.</summary>
    private async Task<(OrderRecord Order, CheckoutResult? Early)> CreateOrderAndReserveStockAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string idempotencyKey,
        CheckoutRequest request,
        CancellationToken cancellationToken)
    {
        // O preço vem do catálogo, nunca do cliente, e fica gravado no item: mudar o preço depois não altera o pedido.
        var prices = await _stock.GetUnitPricesAsync(
            connection, transaction, request.Items.Select(item => item.ProductId).ToList(), cancellationToken);
        var items = new List<OrderItemRecord>(request.Items.Count);
        foreach (var item in request.Items)
        {
            if (!prices.TryGetValue(item.ProductId, out var unitPrice))
            {
                throw new UnknownProductException(item.ProductId);
            }

            items.Add(new OrderItemRecord(item.ProductId, item.Quantity, unitPrice));
        }

        var now = _timeProvider.GetUtcNow();
        var order = new OrderRecord(
            Guid.NewGuid(),
            idempotencyKey,
            request.CustomerId,
            OrderStatus.Pending,
            items.Sum(item => item.Quantity * item.UnitPrice),
            FailureReason: null,
            now,
            now);

        var (existing, created) = await _orders.TryCreatePendingAsync(connection, transaction, order, items, cancellationToken);
        if (!created)
        {
            return (existing, new CheckoutResult(existing.Id, existing.Status, existing.FailureReason, existing.TotalAmount, IsReplay: true));
        }

        var expiresAt = now.Add(ReservationTtl);
        if (await _stock.TryReserveAsync(connection, transaction, order.Id, request.Items, expiresAt, cancellationToken))
        {
            return (order, null);
        }

        // Falta estoque: o pedido já nasce cancelado e o pagamento nem é chamado.
        await _orders.TryTransitionAsync(
            connection, transaction, order.Id, OrderStatus.Pending, OrderStatus.Cancelled, FailureReasons.StockInsufficient, cancellationToken);
        return (order, new CheckoutResult(order.Id, OrderStatus.Cancelled, FailureReasons.StockInsufficient, order.TotalAmount, IsReplay: false));
    }

    /// <summary>
    /// Chama o provedor com uma chave de idempotência derivada do pedido, para um retry nunca cobrar duas vezes.
    /// Retries esgotados ou circuito aberto viram pagamento FAILED (indisponível).
    /// </summary>
    private async Task<PaymentRecord> ChargeAsync(OrderRecord order, CancellationToken cancellationToken)
    {
        var idempotencyKey = $"charge:{order.Id}";
        PaymentStatus status;
        string? providerReference = null;
        int attempts;
        try
        {
            var charge = await _paymentProvider.ChargeAsync(order.Id, order.TotalAmount, idempotencyKey, cancellationToken);
            status = charge.Outcome == ChargeOutcome.Approved ? PaymentStatus.Approved : PaymentStatus.Declined;
            providerReference = charge.ProviderReference;
            attempts = charge.Attempts;
        }
        catch (PaymentUnavailableException ex)
        {
            status = PaymentStatus.Failed;
            attempts = ex.Attempts;
        }

        return new PaymentRecord(
            Guid.NewGuid(), order.Id, idempotencyKey, order.TotalAmount, status, providerReference, attempts, _timeProvider.GetUtcNow());
    }

    private async Task<CheckoutResult> CompleteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        OrderRecord order,
        PaymentRecord payment,
        CancellationToken cancellationToken)
    {
        await _payments.SaveAsync(connection, transaction, payment, cancellationToken);

        if (payment.Status == PaymentStatus.Approved)
        {
            await _stock.ConfirmAsync(connection, transaction, order.Id, cancellationToken);
            await _orders.TryTransitionAsync(
                connection, transaction, order.Id, OrderStatus.Pending, OrderStatus.Confirmed, null, cancellationToken);
            return new CheckoutResult(order.Id, OrderStatus.Confirmed, null, order.TotalAmount, IsReplay: false);
        }

        // Compensação: pagamento recusado ou indisponível devolve o estoque reservado.
        var failureReason = payment.Status == PaymentStatus.Declined ? FailureReasons.PaymentDeclined : FailureReasons.PaymentUnavailable;
        await _stock.ReleaseAsync(connection, transaction, order.Id, cancellationToken);
        await _orders.TryTransitionAsync(
            connection, transaction, order.Id, OrderStatus.Pending, OrderStatus.Cancelled, failureReason, cancellationToken);
        return new CheckoutResult(order.Id, OrderStatus.Cancelled, failureReason, order.TotalAmount, IsReplay: false);
    }
}
