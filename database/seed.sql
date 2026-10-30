-- Produtos de exemplo para rodar o checkout localmente. Sem estoque, todo pedido é cancelado com STOCK_INSUFFICIENT.
INSERT INTO products (id, name, available_quantity, unit_price) VALUES
    ('ingresso-inteira', 'Ingresso inteira', 100, '40.00'),
    ('ingresso-meia', 'Meia-entrada', 100, '20.00'),
    ('combo-pipoca', 'Combo pipoca grande + refrigerante', 50, '35.00'),
    ('poltrona-vip', 'Poltrona VIP', 10, '60.00');
