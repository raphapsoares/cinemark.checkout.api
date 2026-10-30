namespace cinemark.checkout.shared.models;

/// <summary>Corpo das respostas de erro da API: { "error": "..." }.</summary>
public sealed record ErrorResponse(string Error);
