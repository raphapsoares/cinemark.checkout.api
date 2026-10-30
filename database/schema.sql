-- Esquema do banco do checkout (SQLite). Aplicado pelos testes (TestDatabase) e, só em Development, pela própria
-- API a cada start (Database:ResetOnStartup). Fora disso, a aplicação não cria nem migra esquema.

CREATE TABLE products (
    id TEXT PRIMARY KEY,
    name TEXT NOT NULL,
    available_quantity INTEGER NOT NULL CHECK (available_quantity >= 0),
    unit_price TEXT NOT NULL               -- preço atual de catálogo, ex.: '25.00'; o pedido grava o seu em order_items
);

CREATE TABLE orders (
    id TEXT PRIMARY KEY,
    idempotency_key TEXT NOT NULL UNIQUE,  -- garante que a mesma requisição não cria dois pedidos
    customer_id TEXT NOT NULL,
    status INTEGER NOT NULL CHECK (status IN (1, 2, 3)), -- 1 PENDING, 2 CONFIRMED, 3 CANCELLED
    total_amount TEXT NOT NULL,            -- ex.: '50.00'
    failure_reason TEXT NULL,              -- STOCK_INSUFFICIENT, PAYMENT_DECLINED ou PAYMENT_UNAVAILABLE
    created_at TEXT NOT NULL,
    updated_at TEXT NOT NULL
);

CREATE TABLE order_items (
    id TEXT PRIMARY KEY,
    order_id TEXT NOT NULL REFERENCES orders (id),
    product_id TEXT NOT NULL,
    quantity INTEGER NOT NULL,
    unit_price TEXT NOT NULL               -- ex.: '25.00'
);

CREATE TABLE reservations (
    id TEXT PRIMARY KEY,
    order_id TEXT NOT NULL REFERENCES orders (id),
    product_id TEXT NOT NULL REFERENCES products (id),
    quantity INTEGER NOT NULL,
    status INTEGER NOT NULL CHECK (status IN (1, 2, 3)), -- 1 RESERVED, 2 CONFIRMED, 3 RELEASED
    expires_at TEXT NOT NULL,
    UNIQUE (order_id, product_id)          -- uma reserva por produto e pedido: reservar de novo não duplica
);

CREATE TABLE payments (
    id TEXT PRIMARY KEY,
    order_id TEXT NOT NULL UNIQUE,
    idempotency_key TEXT NOT NULL UNIQUE,  -- enviada ao provedor: um retry nunca cobra duas vezes
    amount TEXT NOT NULL,                  -- ex.: '50.00'
    status INTEGER NOT NULL CHECK (status IN (1, 2, 3)), -- 1 APPROVED, 2 DECLINED, 3 FAILED
    provider_ref TEXT NULL,
    attempts INTEGER NOT NULL,             -- tentativas HTTP, retries incluídos
    updated_at TEXT NOT NULL
);
