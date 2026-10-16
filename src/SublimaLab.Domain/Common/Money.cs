namespace SublimaLab.Domain.Common;

public static class Money
{
    /// <summary>Rounds a monetary amount to cents, midpoints away from zero.</summary>
    public static decimal Round(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero);
}
