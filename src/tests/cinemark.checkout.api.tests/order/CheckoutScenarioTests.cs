using System.Net.Http.Json;
using cinemark.checkout.api.requests;
using cinemark.checkout.api.tests.support;
using cinemark.checkout.shared.enums;

namespace cinemark.checkout.api.tests.order;

/// <summary>
/// Cenários da demonstração, numerados como no README. Cada teste sobe a API real sobre um banco novo.
/// </summary>
public class CheckoutScenarioTests
{
    private const string Product = "product-1";

    // Cenário 1
    [Fact]
    public async Task Dado_EstoqueSuficienteECartaoAprovado_Quando_Compra_Entao_ConfirmaPedidoEReservaEReduzSaldo()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10, unitPrice: 25.00m);

        var response = await host.PostCheckoutAsync("key-feliz", CheckoutTestHost.Body((Product, 2)));

        Assert.Equal(201, response.StatusCode);
        Assert.Equal("CONFIRMED", response.Status);
        Assert.Equal(50.00m, response.TotalAmount);
        Assert.Equal(8, host.Database.GetAvailableQuantity(Product));
        Assert.Equal(ReservationStatus.Confirmed, host.Database.GetReservationStatus(response.OrderId));
        Assert.Equal(PaymentStatus.Approved, host.Database.GetPaymentStatus(response.OrderId));

        var id = ("@id", (object?)response.OrderId.ToString());
        Assert.Equal("50.00", host.Database.Scalar<string>("SELECT total_amount FROM orders WHERE id = @id", id));
        Assert.Equal("50.00", host.Database.Scalar<string>("SELECT amount FROM payments WHERE order_id = @id", id));
    }

    // Cenário 2
    [Fact]
    public async Task Dado_EstoqueInsuficiente_Quando_Compra_Entao_CancelaPedidoSemCobrar()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 1);

        var response = await host.PostCheckoutAsync("key-sem-estoque", CheckoutTestHost.Body((Product, 2)));

        Assert.Equal(422, response.StatusCode);
        Assert.Equal("CANCELLED", response.Status);
        Assert.Equal("STOCK_INSUFFICIENT", response.FailureReason);
        Assert.Equal(1, host.Database.GetAvailableQuantity(Product));
        Assert.Equal(0, host.PaymentProvider.CallCount);
        Assert.Equal(0, host.Database.Count("payments"));
    }

    // Cenário 3
    [Fact]
    public async Task Dado_CartaoRecusado_Quando_Compra_Entao_CancelaPedidoEDevolveItensAoEstoque()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10);

        var response = await host.PostCheckoutAsync(
            "key-recusado", CheckoutTestHost.Body((Product, 2)), FakePaymentHandler.SimulateDecline);

        Assert.Equal(422, response.StatusCode);
        Assert.Equal("CANCELLED", response.Status);
        Assert.Equal("PAYMENT_DECLINED", response.FailureReason);
        Assert.Equal(PaymentStatus.Declined, host.Database.GetPaymentStatus(response.OrderId));
        Assert.Equal(ReservationStatus.Released, host.Database.GetReservationStatus(response.OrderId));
        Assert.Equal(10, host.Database.GetAvailableQuantity(Product));
        Assert.Equal(1, host.PaymentProvider.CallCount); // recusa é resposta final: sem retry
    }

    // Cenário 4
    [Fact]
    public async Task Dado_UltimaUnidade_Quando_DuasComprasSimultaneas_Entao_SoUmaReservaEAOutraFicaSemEstoque()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 1);

        var responses = await Task.WhenAll(
            Task.Run(() => host.PostCheckoutAsync("key-ultimo-a", CheckoutTestHost.Body((Product, 1)))),
            Task.Run(() => host.PostCheckoutAsync("key-ultimo-b", CheckoutTestHost.Body((Product, 1)))));

        Assert.Single(responses, response => response.StatusCode == 201);
        Assert.Single(responses, response => response is { StatusCode: 422, FailureReason: "STOCK_INSUFFICIENT" });
        Assert.Equal(0, host.Database.GetAvailableQuantity(Product));
        Assert.Equal(1, host.PaymentProvider.CallCount);
    }

    // Cenário 5
    [Fact]
    public async Task Dado_PedidoJaFeito_Quando_RepeteComMesmaChave_Entao_DevolveOPedidoExistenteSemCriarOutro()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10);
        var body = CheckoutTestHost.Body((Product, 2));

        var first = await host.PostCheckoutAsync("key-duplicada", body);
        var second = await host.PostCheckoutAsync("key-duplicada", body);

        Assert.Equal(201, first.StatusCode);
        Assert.Equal(200, second.StatusCode);
        Assert.Equal(first.Body, second.Body);
        Assert.Equal(8, host.Database.GetAvailableQuantity(Product));
        Assert.Equal(1, host.Database.Count("orders"));
        Assert.Equal(1, host.PaymentProvider.CallCount);
    }

    // Cenário 5, com as duas requisições chegando ao mesmo tempo.
    [Fact]
    public async Task Dado_MesmaChave_Quando_DuasRequisicoesSimultaneas_Entao_SoUmaReservaECobra()
    {
        using var host = CheckoutTestHost.Create(provider => (provider.MinDelayMs, provider.MaxDelayMs) = (100, 100));
        host.Database.InsertProduct(Product, 10);
        var body = CheckoutTestHost.Body((Product, 2));

        var responses = await Task.WhenAll(
            Task.Run(() => host.PostCheckoutAsync("key-concorrente", body)),
            Task.Run(() => host.PostCheckoutAsync("key-concorrente", body)));

        // Uma cria o pedido (201); a outra encontra o pedido em andamento (409) ou já concluído (200).
        Assert.Single(responses, response => response.StatusCode == 201);
        Assert.Single(responses, response => response.StatusCode is 409 or 200);
        Assert.Equal(8, host.Database.GetAvailableQuantity(Product));
        Assert.Equal(1, host.Database.Count("orders"));
        Assert.Equal(1, host.PaymentProvider.ProcessedChargeCount);
    }

    // Cenário 7: falha passageira, o retry resolve.
    [Fact]
    public async Task Dado_ProvedorFalhaDuasVezes_Quando_Compra_Entao_TentaDeNovoComEsperaEConfirma()
    {
        using var host = CheckoutTestHost.Create(provider => provider.FailFirstN = 2);
        host.Database.InsertProduct(Product, 10);

        var response = await host.PostCheckoutAsync("key-retry", CheckoutTestHost.Body((Product, 1)));

        Assert.Equal(201, response.StatusCode);
        Assert.Equal(3, host.PaymentProvider.CallCount);
        Assert.Equal(3, host.Database.Scalar<long>(
            "SELECT attempts FROM payments WHERE order_id = @id", ("@id", response.OrderId.ToString())));
    }

    // Cenário 7: provedor lento; indisponível não é recusa.
    [Fact]
    public async Task Dado_ProvedorLento_Quando_Compra_Entao_EsgotaAsTentativasEDevolve503IndisponivelNaoRecusado()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10);

        var response = await host.PostCheckoutAsync(
            "key-lento", CheckoutTestHost.Body((Product, 3)), FakePaymentHandler.SimulateTimeout);

        Assert.Equal(503, response.StatusCode);
        Assert.Equal("CANCELLED", response.Status);
        Assert.Equal("PAYMENT_UNAVAILABLE", response.FailureReason);
        Assert.Equal(PaymentStatus.Failed, host.Database.GetPaymentStatus(response.OrderId));
        Assert.Equal(4, host.PaymentProvider.CallCount); // 1 tentativa + 3 retries, cada uma cortada pelo timeout
        Assert.Equal(ReservationStatus.Released, host.Database.GetReservationStatus(response.OrderId));
        Assert.Equal(10, host.Database.GetAvailableQuantity(Product));
    }

    // Cenário 7: depois das falhas o disjuntor abre e a próxima compra nem chega ao provedor.
    [Fact]
    public async Task Dado_ProvedorForaDoAr_Quando_FalhasAtingemOMinimo_Entao_DisjuntorAbreEAProximaCompraNaoChamaOProvedor()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10);

        var first = await host.PostCheckoutAsync(
            "key-disjuntor-1", CheckoutTestHost.Body((Product, 1)), FakePaymentHandler.SimulateUnavailable);
        var callsAfterFirst = host.PaymentProvider.CallCount;
        var second = await host.PostCheckoutAsync("key-disjuntor-2", CheckoutTestHost.Body((Product, 1)));

        Assert.Equal(503, first.StatusCode);
        Assert.Equal(4, callsAfterFirst); // 4 falhas atingem o mínimo de chamadas e abrem o circuito
        Assert.Equal(503, second.StatusCode);
        Assert.Equal("PAYMENT_UNAVAILABLE", second.FailureReason);
        Assert.Equal(callsAfterFirst, host.PaymentProvider.CallCount);
        Assert.Equal(10, host.Database.GetAvailableQuantity(Product));
    }

    // Cenário 10
    [Fact]
    public async Task Dado_PedidoConfirmado_Quando_PrecoDoProdutoMuda_Entao_PedidoMantemOValorGravado()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10, unitPrice: 25.00m);
        var compra = await host.PostCheckoutAsync("key-preco-1", CheckoutTestHost.Body((Product, 2)));

        using var precoNovo = await host.Client.PutAsJsonAsync($"/demo/products/{Product}", new { unitPrice = 30.00m });
        var pedido = await host.GetOrderAsync(compra.OrderId);
        var novaCompra = await host.PostCheckoutAsync("key-preco-2", CheckoutTestHost.Body((Product, 2)));

        Assert.True(precoNovo.IsSuccessStatusCode);
        Assert.Equal(50.00m, pedido.GetProperty("order").GetProperty("totalAmount").GetDecimal());
        Assert.Equal(25.00m, pedido.GetProperty("items")[0].GetProperty("unitPrice").GetDecimal());
        Assert.Equal(50.00m, pedido.GetProperty("payment").GetProperty("amount").GetDecimal());
        Assert.Equal(60.00m, novaCompra.TotalAmount);
    }

    [Fact]
    public async Task Dado_RequisicaoInvalida_Quando_Compra_Entao_Devolve400ENaoCriaPedido()
    {
        using var host = CheckoutTestHost.Create();
        host.Database.InsertProduct(Product, 10);

        var missingKey = await host.PostCheckoutAsync(null, CheckoutTestHost.Body((Product, 1)));
        var unknownProduct = await host.PostCheckoutAsync("key-invalida", CheckoutTestHost.Body(("nao-existe", 1)));

        Assert.Equal(400, missingKey.StatusCode);
        Assert.Equal(400, unknownProduct.StatusCode);
        Assert.Equal(0, host.Database.Count("orders"));
    }
}
