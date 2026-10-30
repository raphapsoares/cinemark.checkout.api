# cinemark.checkout

API .NET 8 de checkout (Parte 2 do case): **reserva estoque** e **cobra num provedor de cartão simulado**, com
idempotência, retry com backoff, circuit breaker e compensação. A arquitetura completa (serviços separados, saga,
filas, outbox) é a resposta da Parte 1; aqui está só o recorte pedido.

## Como rodar

Requisito: .NET 8 SDK.

```
dotnet run --project src/api/cinemark.checkout.api   # abre o Swagger em http://localhost:5080/swagger
dotnet test cinemark.checkout.sln                    # os cenários como testes automatizados
```

Em Development (o perfil do `dotnet run` e do F5 no Visual Studio), **a cada start a API apaga e recria
`database/cinemark.db`** a partir de `database/schema.sql` e `database/seed.sql`. Toda execução começa com os
4 produtos e nenhum pedido, e as chaves `demo-1`, `demo-2`… podem ser reaproveitadas. Para manter os dados entre
execuções, mude `Database:ResetOnStartup` para `false` no `appsettings.Development.json`. Fora de Development, a API
nunca cria nem altera o esquema: o banco precisa existir, criado a partir de `database/schema.sql`.

| Endpoint | Para quê |
|---|---|
| `POST /checkout` | Compra. Header `Idempotency-Key` obrigatório. Corpo: `{ "customerId": "c-1", "items": [{ "productId": "ingresso-inteira", "quantity": 2 }] }` |
| `GET /orders/{id}` | Pedido com itens, reservas e pagamento |
| `GET /products/{id}` | Saldo e preço atual |
| `PUT /demo/products/{id}` | Só em Development: muda saldo e/ou preço (`{ "availableQuantity": 1, "unitPrice": 50.00 }`) para preparar um cenário |

O Swagger e o `PUT /demo` só existem em Development, que é o perfil do `dotnet run`. No Swagger, o `POST /checkout` já
vem com um corpo de exemplo, a chave `demo-1` e o `X-Simulate-Payment` como lista de opções. Troque a chave a cada
compra nova; repetir a mesma chave é justamente o cenário 5.

Produtos do seed: `ingresso-inteira` (100 un., R$ 40,00), `ingresso-meia` (100, R$ 20,00), `combo-pipoca` (50, R$ 35,00),
`poltrona-vip` (10, R$ 60,00).

## Cenários

Tudo é feito pelo **Swagger** (`/swagger`): o roteiro abaixo aparece no topo da página, e o `POST /checkout` e o
`PUT /demo/products` trazem um exemplo de corpo pronto por cenário, na lista **Examples**. Para forçar falhas, o provedor simulado lê o header **`X-Simulate-Payment`**: `decline`, `unavailable` ou `timeout`.
Sem o header, ele segue a configuração; em Development, sempre aprova.

| # | Cenário | Como disparar no Swagger | Resultado esperado | Onde ver |
|---|---|---|---|---|
| 1 | Compra feliz | `POST /checkout` com o exemplo pronto | 201 `CONFIRMED` | `GET /orders/{id}`: reserva `CONFIRMED`, pagamento `APPROVED`. `GET /products/{id}`: saldo menor |
| 2 | Estoque insuficiente | `POST /checkout` com `quantity: 999` | 422 `CANCELLED`, `STOCK_INSUFFICIENT` | `GET /orders/{id}`: `payment: null`, sem reservas |
| 3 | Cartão recusado | `POST /checkout` com `X-Simulate-Payment` = `decline` | 422 `CANCELLED`, `PAYMENT_DECLINED` | `GET /orders/{id}`: pagamento `DECLINED`, reserva `RELEASED`. Saldo inalterado |
| 4 | Concorrência no último item | No terminal: `.\scripts\cenario-04-concorrencia.ps1`. O Swagger manda uma requisição por vez | Uma 201 e outra 422 `STOCK_INSUFFICIENT` | Saída do script, ou `GET /products/poltrona-vip`: saldo 0 |
| 5 | Requisição duplicada | `POST /checkout` duas vezes com a mesma `Idempotency-Key` | 201, depois 200 com o mesmo `orderId` | `GET /products/{id}`: o saldo caiu uma vez só |
| 7 | Provedor lento / fora do ar | `POST /checkout` com `X-Simulate-Payment` = `timeout`, depois outra compra sem o header | 503 `PAYMENT_UNAVAILABLE` após ~9 s; a compra seguinte recebe 503 imediato | Console da API: `OnTimeout`, `OnRetry`, `OnCircuitOpened`. `GET /orders/{id}`: pagamento `FAILED` com `attempts: 4` (não `DECLINED`) |
| 10 | Preço muda depois da compra | `POST /checkout`, depois `PUT /demo/products/ingresso-inteira` com `{ "unitPrice": 50.00 }`, depois nova compra | Pedido antigo mantém R$ 80,00; compra nova sai a R$ 100,00 | `GET /orders/{id}`: `unitPrice` e `totalAmount` gravados |

Para a reunião, deixe o cenário 7 por último, porque o disjuntor fica aberto por 30 s. Ele abre quando pelo menos 50%
das chamadas dos últimos 30 s falharam (mínimo de 4), e as compras aprovadas logo antes entram na conta. Por isso,
espere 30 s sem comprar antes de começá-lo. Para recomeçar a demonstração do zero, reinicie a API: isso recria o banco
e também fecha o disjuntor.

**Fora do código:** os cenários 6 (mesma mensagem entregue duas vezes), 8 (DLQ) e 9 (queda do publicador da outbox)
dependem de filas e outbox, que pertencem à Parte 1 e não a este recorte síncrono. O equivalente síncrono do 6 está
coberto: a mesma reserva não é gravada duas vezes (`UNIQUE(order_id, product_id)`) e a cobrança usa a chave
`charge:{orderId}`, para um retry nunca cobrar de novo.

## Testes

Testes de integração em [CheckoutScenarioTests.cs](src/tests/cinemark.checkout.api.tests/order/CheckoutScenarioTests.cs).
Cada teste sobe a API real em memória (`WebApplicationFactory`) sobre um banco SQLite temporário, com a mesma
configuração do `appsettings.json`, só com tempos mais curtos. Os nomes seguem o padrão Dado/Quando/Então e o comentário
de cada um indica o número do cenário.

```
dotnet test --filter "FullyQualifiedName~Disjuntor"     # um cenário por vez
```

## Decisões

- **Idempotência:** `orders.idempotency_key` é UNIQUE e as escritas são serializadas (`BEGIN IMMEDIATE`). Assim, a mesma requisição nunca reserva duas vezes, nem chegando ao mesmo tempo.
- **Sem venda além do estoque:** a reserva usa um UPDATE condicional (`... WHERE available_quantity >= @qty`). Se faltar um item, um savepoint desfaz os débitos parciais.
- **Preço:** vem do catálogo (`products.unit_price`), nunca do cliente, e fica gravado em `order_items`.
- **Resiliência:** handler padrão do `Microsoft.Extensions.Http.Resilience` (Polly v8), configurado na seção `PaymentResilience` do `appsettings.json`:
  - timeout de 2 s por tentativa e de 15 s no total;
  - 3 retries com backoff exponencial e jitter;
  - circuit breaker com 50% de falhas em pelo menos 4 chamadas, aberto por 30 s.
- **Recusa ≠ indisponível:** cartão recusado grava pagamento `DECLINED` e responde 422. Provedor indisponível grava `FAILED` e responde 503. Nos dois casos, o estoque volta.
- **Persistência:** SQLite com ADO.NET. O esquema (`database/schema.sql`) é criado por script; a aplicação só o aplica em Development, para recriar o banco local a cada start. Status são inteiros; dinheiro é decimal, gravado como texto exato (`'25.00'`).
- **Configuração:** `appsettings.json`, com variáveis de ambiente por cima (ex.: `Database__DatabasePath`, `PaymentResilience__Retry__MaxRetryAttempts`).

## Próximos passos

- Separar em serviços de pedido, estoque e pagamento (Parte 1): saga orquestrada por filas com DLQ e outbox em cada serviço (cenários 6, 8 e 9).
- A implementar: estorno, expiração automática da reserva de 15 minutos e tempo limite de pedido parado.
- Recusar chave de idempotência reutilizada com outro corpo.
- Banco de servidor (PostgreSQL/Aurora), observabilidade e teste de carga.
