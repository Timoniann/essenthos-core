namespace Essenthos.Core;

/// <summary>The names of the rate-limit policies an endpoint asks for; the API defines what each allows.</summary>
internal static class RateLimitPolicies
{
    /// <summary>The endpoint policy for the queries that cost seconds rather than milliseconds.</summary>
    public const string Expensive = "expensive";
}
