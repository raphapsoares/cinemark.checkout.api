namespace cinemark.checkout.api.swagger;

/// <summary>Roteiro da demonstração exibido no topo do Swagger (Markdown).</summary>
public static class ScenarioGuide
{
    public const string Description =
        """
        **Como usar:** abra o endpoint → **Try it out** → escolha o exemplo em **Examples** → **Execute**.
        Use uma `Idempotency-Key` nova por compra (`demo-1`, `demo-2`…). Para conferir, copie o `orderId` da resposta
        no **GET /orders/{id}**. Reiniciar a API recria o banco e fecha o disjuntor.

        **Cenários**

        1. **Compra feliz:** POST /checkout, exemplo *Cenário 1*, chave `demo-1`.
           Esperado: **201 CONFIRMED**. No GET /orders, reserva CONFIRMED e pagamento APPROVED; GET /products/ingresso-inteira: saldo 98.
        2. **Requisição duplicada (cenário 5):** clique em Execute de novo **com a mesma chave** `demo-1`.
           Esperado: **200** com o mesmo `orderId`; o saldo continua 98.
        3. **Estoque insuficiente (cenário 2):** exemplo *Cenário 2*, chave `demo-2`.
           Esperado: **422 STOCK_INSUFFICIENT**; no GET /orders, `payment: null` (nenhuma cobrança).
        4. **Cartão recusado (cenário 3):** exemplo *Cenário 3*, chave `demo-3`, **X-Simulate-Payment = decline**.
           Esperado: **422 PAYMENT_DECLINED**; pagamento DECLINED, reserva RELEASED; GET /products/combo-pipoca: saldo 50 (compensação).
           Depois, volte o X-Simulate-Payment para vazio.
        5. **Preço muda depois da compra (cenário 10):** compre com `demo-4` (exemplo *Cenário 1*: 80.00) → PUT /demo/products/ingresso-inteira,
           exemplo *Cenário 10* → GET /orders do pedido antigo: continua **80.00** → nova compra com `demo-5`: **100.00**.
        6. **Concorrência no último item (cenário 4):** o Swagger não dispara em paralelo. No terminal:
           `.\scripts\cenario-04-concorrencia.ps1`. Esperado: uma **201** e outra **422 STOCK_INSUFFICIENT**; saldo 0.
        7. **Provedor lento e disjuntor (cenário 7), sempre por último:** espere **30 s sem comprar**
           (as compras aprovadas recentes entram na conta do disjuntor).
           Exemplo *Cenário 7*, chave `demo-6`, **X-Simulate-Payment = timeout**: leva ~9 s → **503 PAYMENT_UNAVAILABLE**,
           pagamento **FAILED com attempts 4** (indisponível, não recusado). No console da API: OnRetry e OnCircuitOpened.
           Logo depois, compre com `demo-7` **sem** o header: **503 imediato**, porque o disjuntor abriu e o provedor nem é chamado.
           Após 30 s, volta a aprovar.

        Fora do código (Parte 1, filas e outbox): cenários 6 (mensagem duplicada), 8 (DLQ) e 9 (queda do publicador).
        """;
}
