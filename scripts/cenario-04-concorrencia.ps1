# Cenário 4: duas compras simultâneas da última unidade.
# Esperado: uma recebe 201 CONFIRMED; a outra 422 STOCK_INSUFFICIENT; o saldo termina em 0.
# Uso (com a API rodando): .\scripts\cenario-04-concorrencia.ps1
param(
    [string]$Api = "http://localhost:5080",
    [string]$Product = "poltrona-vip"
)

$ErrorActionPreference = 'Stop'

# Deixa só 1 unidade do produto (mesmo endpoint de demonstração do Swagger).
$null = Invoke-RestMethod -Method Put "$Api/demo/products/$Product" -ContentType 'application/json' -Body '{ "availableQuantity": 1 }'
Write-Host "$Product com 1 unidade. Disparando duas compras ao mesmo tempo..."

Add-Type -AssemblyName System.Net.Http
$client = New-Object System.Net.Http.HttpClient
$body = '{ "customerId": "c-1", "items": [{ "productId": "' + $Product + '", "quantity": 1 }] }'

# As duas requisições saem juntas, antes de esperar qualquer resposta.
$tasks = foreach ($name in 'A', 'B') {
    $request = New-Object System.Net.Http.HttpRequestMessage ([System.Net.Http.HttpMethod]::Post), "$Api/checkout"
    $request.Headers.Add('Idempotency-Key', "concorrencia-$name-$([guid]::NewGuid())")
    $request.Content = New-Object System.Net.Http.StringContent $body, ([Text.Encoding]::UTF8), 'application/json'
    $client.SendAsync($request)
}
[Threading.Tasks.Task]::WaitAll([Threading.Tasks.Task[]]$tasks)

$names = 'A', 'B'
for ($i = 0; $i -lt $tasks.Count; $i++) {
    $response = $tasks[$i].Result
    Write-Host "Compra $($names[$i]): HTTP $([int]$response.StatusCode) $($response.Content.ReadAsStringAsync().Result)"
}

Write-Host ""
Write-Host "Produto depois das duas compras:"
Invoke-RestMethod "$Api/products/$Product" | Format-List
