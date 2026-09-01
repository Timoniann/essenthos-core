namespace Essenthos.Core.Endpoints;

/// <summary>
/// Every paged endpoint in v1 takes <c>?skip=&amp;take=</c> and answers
/// <c>{ total, skip, take, items }</c>. The clamping lives here so the two numbers mean the same
/// thing on every route.
/// </summary>
internal static class Paging
{
    public const int DefaultPageSize = 50;

    public const int MaxPageSize = 500;

    public static (int Skip, int Take) Normalize(int? skip, int? take)
    {
        return (Math.Max(skip ?? 0, 0), Math.Clamp(take ?? DefaultPageSize, 1, MaxPageSize));
    }
}
