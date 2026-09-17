using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace Essenthos.Core.Bhsa;

public class StringEnum<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicConstructors |
                                DynamicallyAccessedMemberTypes.PublicConstructors)]
    T> : IParsable<T>
    where T : StringEnum<T>
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, T> InternalItems = new(10);
    private readonly string _value;

    protected StringEnum(string value, bool external = false)
    {
        _value = value;
        External = external;
        if (!external)
        {
            InternalItems.Add(value, (T)this);
        }
    }

    // ReSharper disable once ConvertToAutoProperty
    public string Value => _value;

    public bool External { get; }

    public override string ToString()
    {
        return _value;
    }

    public static implicit operator string(StringEnum<T> value)
    {
        return value._value;
    }

    // ReSharper disable once StaticMemberInGenericType
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        lock (Lock)
        {
            if (_initialized)
            {
                return;
            }

#pragma warning disable IL2059
            RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle);
#pragma warning restore IL2059

            _initialized = true;
        }
    }


    public static T Parse(string s, IFormatProvider? provider = null)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (!_initialized)
        {
            EnsureInitialized();
        }

        if (InternalItems.TryGetValue(s, out var item))
        {
            return item;
        }

        Console.WriteLine($"Creating new instance of {typeof(T).Name} with value '{s}'");
        return (T)Activator.CreateInstance(typeof(T),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance,
            null, [s, true], null, null)!;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
        [MaybeNullWhen(false)] out T result)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (!_initialized)
        {
            EnsureInitialized();
        }

        if (s is null)
        {
            result = null;
            return false;
        }

        if (InternalItems.TryGetValue(s, out var item))
        {
            result = item;
            return true;
        }

        try
        {
            Console.WriteLine($"Creating new instance of {typeof(T).Name} with value '{s}'");
            result = (T)Activator.CreateInstance(typeof(T),
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance,
                null, [s, true], null, null)!;
            return true;
        }
        catch (Exception)
        {
            result = null;
            return false;
        }
    }
}

public class StringEnums<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.NonPublicConstructors |
                                DynamicallyAccessedMemberTypes.PublicConstructors)]
    T> : IParsable<T>
    where T : StringEnums<T>
{
    // ReSharper disable once StaticMemberInGenericType
    private static readonly Lock Lock = new();
    private static readonly Dictionary<string, T> InternalItems = new(20);
    private readonly string[] _values;

    protected StringEnums(string[] values, bool external = false)
    {
        _values = values;
        External = external;
        if (!external)
        {
            foreach (var value in values)
            {
                if (InternalItems.ContainsKey(value))
                {
                    throw new ArgumentException($"Value '{value}' already exists in {typeof(T).Name}.");
                }

                InternalItems.Add(value, (T)this);
            }
        }
    }

    // ReSharper disable once ConvertToAutoPropertyWhenPossible
    public string[] Values => _values;

    /// <summary>
    /// The spelling the source writes today, which is the first one given. The rest are earlier
    /// releases' spellings of the same value, kept so an older export still parses — a tolerance
    /// that belongs to reading and must never reach anything that is written or compared.
    /// </summary>
    public string Value => _values[0];

    public bool External { get; }

    public override string ToString()
    {
        return Value;
    }

    // ReSharper disable once StaticMemberInGenericType
    private static bool _initialized;

    private static void EnsureInitialized()
    {
        lock (Lock)
        {
            if (_initialized)
            {
                return;
            }

#pragma warning disable IL2059
            RuntimeHelpers.RunClassConstructor(typeof(T).TypeHandle);
#pragma warning restore IL2059

            _initialized = true;
        }
    }

    public static T Parse(string s, IFormatProvider? provider = null)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (!_initialized)
        {
            EnsureInitialized();
        }

        if (InternalItems.TryGetValue(s, out var item))
        {
            return item;
        }

        Console.WriteLine($"Creating new instance of {typeof(T).Name} with value '{s}'");
        return (T)Activator.CreateInstance(typeof(T),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.CreateInstance,
            null, [s, true], null, null)!;
    }

    public static bool TryParse([NotNullWhen(true)] string? s, IFormatProvider? provider,
        [MaybeNullWhen(false)] out T result)
    {
        // ReSharper disable once InconsistentlySynchronizedField
        if (!_initialized)
        {
            EnsureInitialized();
        }

        if (s is null)
        {
            result = null;
            return false;
        }

        if (InternalItems.TryGetValue(s, out var item))
        {
            result = item;
            return true;
        }

        try
        {
            Console.WriteLine($"Creating new instance of {typeof(T).Name} with value '{s}'");
            result = (T)Activator.CreateInstance(typeof(T), s, true)!;
            return true;
        }
        catch (Exception)
        {
            result = null;
            return false;
        }
    }
}