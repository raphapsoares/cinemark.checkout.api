namespace cinemark.checkout.shared.exceptions;

/// <summary>O pedido cita um produto que não existe no catálogo; nenhum pedido é criado.</summary>
public sealed class UnknownProductException : Exception
{
    public UnknownProductException(string productId)
        : base($"Product '{productId}' does not exist.")
    {
    }
}
