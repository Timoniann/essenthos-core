using System.Collections.Concurrent;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace Essenthos.Core.Loading.Marks;

/// <summary>
/// A digest of the code a load step can run inside this project's own assemblies: the instructions of
/// every method reachable from the step, with every name, string and constant they use spelled out
/// rather than as the metadata numbers a build hands out, and the bytes of every embedded file whose
/// name they spell. A change to the step, to a loader it calls, to a reader or a helper those call, or to
/// a record file compiled in, moves the digest of exactly the steps that can reach it, so the step runs
/// again without anybody remembering to say so.
///
/// <para>
/// Reachable means: called, created, its delegate taken, named as a type (a service asked for, a
/// generic argument, a <c>typeof</c>), its static fields read (their initialiser), a virtual or interface
/// method's overrides in these assemblies, and the delegates the step's own closure holds (a text step's
/// reader). Of a type used whole every member counts; of the compiler's closures only the lambdas
/// actually taken, so one step's lambda does not make every step depend on every other. What is still
/// outside — reflection, a file read by a name computed at run time — is what a step's version is for.
/// </para>
///
/// <para>
/// The database model is the exception to "used whole": building it names every mapped type, and a
/// mapped type's constructor names the types of its collections, without running their code. So a mapped
/// type, and the lambdas that configure it, count only for a step whose own code uses that type; what
/// the model says about the tables is in the migrations, which move the marks of the steps reading the
/// tables they touch (<see cref="MigrationTables"/>).
/// </para>
/// </summary>
internal sealed partial class CodeDigest
{
    private static readonly OpCode[] OneByte = new OpCode[256];
    private static readonly OpCode[] TwoByte = new OpCode[256];

    private readonly IReadOnlySet<Assembly> _ours;
    private readonly IReadOnlyDictionary<Type, List<Type>> _derived;
    private readonly IReadOnlyDictionary<string, Assembly> _resources;
    private readonly ConcurrentDictionary<MethodBase, Body> _bodies = new();
    private readonly ConcurrentDictionary<string, string> _resourceHashes = new(StringComparer.Ordinal);

    static CodeDigest()
    {
        foreach (var field in typeof(OpCodes).GetFields(BindingFlags.Public | BindingFlags.Static))
        {
            if (field.GetValue(null) is OpCode code)
            {
                var value = (ushort)code.Value;
                if (value < 0x100)
                {
                    OneByte[value] = code;
                }
                else if ((value & 0xFF00) == 0xFE00)
                {
                    TwoByte[value & 0xFF] = code;
                }
            }
        }
    }

    /// <param name="ours">The assemblies whose code counts; every other assembly is taken as it is named.</param>
    public CodeDigest(IEnumerable<Assembly> ours)
    {
        _ours = ours.ToHashSet();
        var derived = new Dictionary<Type, List<Type>>();
        foreach (var type in _ours.SelectMany(Loadable))
        {
            foreach (var above in Bases(type).Concat(type.GetInterfaces()))
            {
                var key = above.IsGenericType ? above.GetGenericTypeDefinition() : above;
                if (_ours.Contains(key.Assembly))
                {
                    (derived.TryGetValue(key, out var list) ? list : derived[key] = []).Add(type);
                }
            }
        }

        _derived = derived;
        _resources = _ours
            .SelectMany(assembly => assembly.GetManifestResourceNames().Select(name => (name, assembly)))
            .ToDictionary(pair => pair.name, pair => pair.assembly, StringComparer.Ordinal);

        var entities = new HashSet<Type>();
        var builders = new HashSet<MethodBase>();
        const BindingFlags declared = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                      | BindingFlags.Public | BindingFlags.NonPublic;
        foreach (var type in _ours.SelectMany(Loadable))
        {
            if (Bases(type).Any(IsDbContext))
            {
                foreach (var property in type.GetProperties(declared))
                {
                    if (property.PropertyType is { IsGenericType: true } set
                        && set.GetGenericTypeDefinition().FullName == "Microsoft.EntityFrameworkCore.DbSet`1"
                        && _ours.Contains(set.GetGenericArguments()[0].Assembly))
                    {
                        entities.Add(set.GetGenericArguments()[0]);
                    }
                }

                builders.UnionWith(type.GetMethods(declared).Where(method => method.Name == "OnModelCreating"));
            }

            foreach (var method in type.GetMethods(declared))
            {
                entities.UnionWith(Configured(method));
            }
        }

        _entities = entities;
        _modelBuilders = builders;
    }

    /// <summary>The types the database model maps to tables.</summary>
    private readonly IReadOnlySet<Type> _entities;

    /// <summary>The methods that build the database model (a context's <c>OnModelCreating</c>).</summary>
    private readonly IReadOnlySet<MethodBase> _modelBuilders;

    /// <summary>
    /// The types of this project a method configures, when it takes one of the model's builders for them
    /// (<c>EntityTypeBuilder&lt;Word&gt;</c>): the lambdas the model is built with.
    /// </summary>
    private IReadOnlyList<Type> Configured(MethodBase method)
    {
        ParameterInfo[] parameters;
        try
        {
            parameters = method.GetParameters();
        }
        catch (Exception exception) when (exception is FileNotFoundException or TypeLoadException)
        {
            return [];
        }

        return
        [
            .. parameters
                .Select(parameter => parameter.ParameterType)
                .Where(type => type.IsGenericType && type.Namespace == "Microsoft.EntityFrameworkCore.Metadata.Builders")
                .SelectMany(type => type.GetGenericArguments())
                .Where(type => _ours.Contains(type.Assembly)),
        ];
    }

    /// <summary>
    /// Whether a method builds the model: a context's <c>OnModelCreating</c>, a helper it hands the
    /// model builder to, or a lambda configuring a mapped type.
    /// </summary>
    private bool BuildsTheModel(MethodBase method) =>
        _modelBuilders.Contains(method)
        || method.GetParameters().Any(parameter => parameter.ParameterType.FullName == "Microsoft.EntityFrameworkCore.ModelBuilder")
        || Configured(method).Count > 0;

    private static bool IsDbContext(Type type) => type.FullName == "Microsoft.EntityFrameworkCore.DbContext";

    /// <summary>The assemblies of this project the Forge is built from.</summary>
    public static CodeDigest ForTheForge() =>
        new(AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name?.StartsWith("Essenthos.", StringComparison.Ordinal) == true)
            .Append(typeof(CodeDigest).Assembly)
            .Distinct());

    /// <summary>
    /// The digest of everything <paramref name="step"/> can run, the embedded files it names, and every
    /// string its code spells (which is how a folder it reads without declaring it is found).
    /// </summary>
    public Reach Of(Delegate step)
    {
        var methods = new HashSet<MethodBase>();
        var types = new HashSet<Type>();
        var resources = new SortedSet<string>(StringComparer.Ordinal);
        var literals = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<MethodBase>();
        var mapped = new HashSet<Type>();
        var waiting = new Dictionary<Type, List<MethodBase>>();

        void Mapped(Type type)
        {
            if (_entities.Contains(type) && mapped.Add(type))
            {
                foreach (var held in waiting.GetValueOrDefault(type) ?? [])
                {
                    Method(held);
                }
            }
        }

        void Wait(MethodBase method, IEnumerable<Type> until)
        {
            foreach (var type in until)
            {
                if (mapped.Contains(type))
                {
                    Method(method);
                    return;
                }
            }

            foreach (var type in until)
            {
                (waiting.TryGetValue(type, out var list) ? list : waiting[type] = []).Add(method);
            }
        }

        void Method(MethodBase method)
        {
            method = Definition(method);
            if (_ours.Contains(method.Module.Assembly) && !IsSharedRegexState(method) && methods.Add(method))
            {
                queue.Enqueue(method);
                if (method.DeclaringType is { } declaring)
                {
                    Mapped(declaring);
                    foreach (var builder in _modelBuilders.Where(builder => builder.DeclaringType == declaring))
                    {
                        Method(builder);
                    }
                }

                foreach (var overriding in Overrides(method))
                {
                    Method(overriding);
                }
            }
        }

        void Whole(Type type)
        {
            while (type.HasElementType)
            {
                type = type.GetElementType()!;
            }

            if (type.IsGenericType && !type.IsGenericTypeDefinition)
            {
                foreach (var argument in type.GetGenericArguments())
                {
                    Whole(argument);
                }

                type = type.GetGenericTypeDefinition();
            }

            if (type.IsGenericParameter || !_ours.Contains(type.Assembly) || IsClosure(type) || !types.Add(type))
            {
                return;
            }

            Mapped(type);

            const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                     | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var member in type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all)))
            {
                Method(member);
            }

            if (type.TypeInitializer is { } initializer)
            {
                Method(initializer);
            }

            if (type.IsInterface || type.IsAbstract)
            {
                foreach (var implementation in _derived.GetValueOrDefault(type) ?? [])
                {
                    Whole(implementation);
                }
            }
        }

        Closure(step, Method, new HashSet<object>(ReferenceEqualityComparer.Instance));
        while (queue.Count > 0)
        {
            var method = queue.Dequeue();
            var body = Read(method);
            var model = BuildsTheModel(method);
            var mapping = model || (method is ConstructorInfo && method.DeclaringType is { } built && _entities.Contains(built));
            foreach (var called in body.Methods)
            {
                if (!mapping)
                {
                    Method(called);
                }
                else if (model && Configured(called) is { Count: > 0 } configured)
                {
                    Wait(called, configured);
                }
                else if (called.DeclaringType is { } owner && _entities.Contains(owner))
                {
                    Wait(called, [owner]);
                }
                else
                {
                    Method(called);
                }
            }

            foreach (var type in body.Types)
            {
                if (!mapping || !NamesOnlyMappedTypes(type))
                {
                    Whole(type);
                }
            }

            foreach (var resource in body.Resources)
            {
                resources.Add(resource);
            }

            literals.UnionWith(body.Literals);
        }

        var lines = methods
            .Select(method => Read(method).Line)
            .Concat(types.Select(type => "type " + Describe(type) + " " + Shape(type)))
            .Concat(resources.Select(resource => "resource " + resource + " " + ResourceHash(resource)))
            .Order(StringComparer.Ordinal)
            .ToList();
        var digest = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', lines))));
        return new Reach(digest, [.. resources], literals, lines);
    }

    /// <param name="Digest">The digest of the code and the embedded files.</param>
    /// <param name="Resources">The embedded files the code names.</param>
    /// <param name="Literals">Every string the code spells.</param>
    /// <param name="Lines">What the digest is taken over, a line per method, type and file: two of them side by side say what moved.</param>
    public sealed record Reach(string Digest, IReadOnlyList<string> Resources, IReadOnlySet<string> Literals, IReadOnlyList<string> Lines);

    /// <summary>The embedded files of these assemblies, by name.</summary>
    public IReadOnlyCollection<string> EmbeddedFiles => [.. _resources.Keys];

    /// <summary>
    /// The method a delegate runs, and the delegates its closure holds, followed into the closures they hold.
    /// </summary>
    private static void Closure(Delegate step, Action<MethodBase> method, HashSet<object> seen)
    {
        foreach (var part in step.GetInvocationList())
        {
            method(part.Method);
            if (part.Target is not { } target || !seen.Add(target) || !IsClosure(target.GetType()))
            {
                continue;
            }

            foreach (var field in target.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if (field.GetValue(target) is Delegate held)
                {
                    Closure(held, method, seen);
                }
                else if (field.GetValue(target) is { } inner && IsClosure(inner.GetType()) && seen.Add(inner))
                {
                    foreach (var innerField in inner.GetType().GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
                    {
                        if (innerField.GetValue(inner) is Delegate innerHeld)
                        {
                            Closure(innerHeld, method, seen);
                        }
                    }
                }
            }
        }
    }

    private Body Read(MethodBase method) => _bodies.GetOrAdd(method, Parse);

    private Body Parse(MethodBase method)
    {
        var text = new StringBuilder();
        var methods = new List<MethodBase>();
        var types = new List<Type>();
        var resources = new List<string>();
        var literals = new List<string>();
        text.Append(method.Attributes).Append(' ').Append(Describe(method)).Append('\n');
        foreach (var attribute in method.GetCustomAttributesData())
        {
            text.Append("attribute ").Append(Attribute(attribute)).Append('\n');
        }

        var body = method.GetMethodBody();
        if (body?.GetILAsByteArray() is not { } il)
        {
            return new Body(Describe(method) + " " + Hash(text), methods, types, resources, literals);
        }

        var module = method.Module;
        var typeArguments = method.DeclaringType is { IsGenericType: true } declaring ? declaring.GetGenericArguments() : null;
        var methodArguments = method is MethodInfo { IsGenericMethod: true } generic ? generic.GetGenericArguments() : null;

        foreach (var local in body.LocalVariables)
        {
            text.Append("local ").Append(Describe(local.LocalType)).Append('\n');
        }

        foreach (var clause in body.ExceptionHandlingClauses)
        {
            text.Append("clause ").Append(clause.Flags).Append(' ').Append(clause.TryOffset).Append(' ')
                .Append(clause.TryLength).Append(' ').Append(clause.HandlerOffset).Append(' ').Append(clause.HandlerLength);
            if (clause.Flags == ExceptionHandlingClauseOptions.Clause && clause.CatchType is { } caught)
            {
                text.Append(' ').Append(Describe(caught));
                types.Add(caught);
            }

            text.Append('\n');
        }

        var at = 0;
        while (at < il.Length)
        {
            var code = il[at] == 0xFE ? TwoByte[il[at + 1]] : OneByte[il[at]];
            at += il[at] == 0xFE ? 2 : 1;
            text.Append(code.Name);
            switch (code.OperandType)
            {
                case OperandType.InlineNone:
                    break;
                case OperandType.ShortInlineBrTarget:
                case OperandType.ShortInlineI:
                case OperandType.ShortInlineVar:
                    text.Append(' ').Append(il[at]);
                    at += 1;
                    break;
                case OperandType.InlineVar:
                    text.Append(' ').Append(BitConverter.ToUInt16(il, at));
                    at += 2;
                    break;
                case OperandType.InlineI:
                case OperandType.InlineBrTarget:
                case OperandType.ShortInlineR:
                    text.Append(' ').Append(BitConverter.ToInt32(il, at));
                    at += 4;
                    break;
                case OperandType.InlineI8:
                case OperandType.InlineR:
                    text.Append(' ').Append(BitConverter.ToInt64(il, at));
                    at += 8;
                    break;
                case OperandType.InlineSwitch:
                    var targets = BitConverter.ToInt32(il, at);
                    text.Append(' ').Append(Convert.ToHexString(il, at, 4 + targets * 4));
                    at += 4 + targets * 4;
                    break;
                case OperandType.InlineString:
                    var literal = module.ResolveString(BitConverter.ToInt32(il, at));
                    text.Append(" \"").Append(literal).Append('"');
                    literals.Add(literal);
                    if (_resources.ContainsKey(literal))
                    {
                        resources.Add(literal);
                    }

                    at += 4;
                    break;
                case OperandType.InlineSig:
                    text.Append(' ').Append(Convert.ToHexString(module.ResolveSignature(BitConverter.ToInt32(il, at))));
                    at += 4;
                    break;
                case OperandType.InlineMethod:
                case OperandType.InlineField:
                case OperandType.InlineType:
                case OperandType.InlineTok:
                    var token = BitConverter.ToInt32(il, at);
                    MemberInfo? member;
                    try
                    {
                        member = module.ResolveMember(token, typeArguments, methodArguments);
                    }
                    catch (ArgumentException)
                    {
                        member = null;
                    }

                    text.Append(' ').Append(member is null ? $"token {token:X8}" : Member(member, code, methods, types));
                    at += 4;
                    break;
                default:
                    throw new InvalidOperationException($"{code.Name} in {Describe(method)} has an operand this digest cannot read.");
            }

            text.Append('\n');
        }

        return new Body(Describe(method) + " " + Hash(text), methods, types, resources, literals);
    }

    private string Member(MemberInfo? member, OpCode code, List<MethodBase> methods, List<Type> types)
    {
        switch (member)
        {
            case MethodBase method:
                methods.Add(method);
                if (method.DeclaringType is { } declaring && (code == OpCodes.Newobj || declaring.IsGenericType))
                {
                    types.Add(declaring);
                }

                if (method is MethodInfo { IsGenericMethod: true } generic)
                {
                    types.AddRange(generic.GetGenericArguments());
                }

                if (method.IsStatic && method.DeclaringType?.TypeInitializer is { } initializer)
                {
                    methods.Add(initializer);
                }

                return Describe(method);
            case FieldInfo field:
                if (field.DeclaringType is { } owner)
                {
                    if (field.IsStatic && owner.TypeInitializer is { } staticInitializer)
                    {
                        methods.Add(staticInitializer);
                    }

                    if (owner.IsGenericType)
                    {
                        types.Add(owner);
                    }
                }

                return Describe(field);
            case Type type:
                types.Add(type);
                return Describe(type);
            default:
                return member?.ToString() ?? "?";
        }
    }

    /// <summary>The generic definition of a method on an instantiated type or with instantiated arguments.</summary>
    private static MethodBase Definition(MethodBase method)
    {
        if (method is MethodInfo { IsGenericMethod: true, IsGenericMethodDefinition: false } instantiated)
        {
            method = instantiated.GetGenericMethodDefinition();
        }

        if (method.DeclaringType is { IsGenericType: true, IsGenericTypeDefinition: false })
        {
            method = method.Module.ResolveMethod(method.MetadataToken)!;
        }

        return method;
    }

    /// <summary>The methods of these assemblies a call to a virtual or interface method can land in.</summary>
    private IEnumerable<MethodBase> Overrides(MethodBase method)
    {
        if (method.DeclaringType is not { } declaring || !(method.IsVirtual || method.IsAbstract) || method.IsFinal)
        {
            yield break;
        }

        var parameters = method.GetParameters().Length;
        foreach (var type in _derived.GetValueOrDefault(declaring) ?? [])
        {
            foreach (var candidate in type.GetMethods(BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
            {
                if ((candidate.Name == method.Name || candidate.Name.EndsWith("." + method.Name, StringComparison.Ordinal))
                    && candidate.GetParameters().Length == parameters)
                {
                    yield return candidate;
                }
            }
        }
    }

    /// <summary>
    /// Whether every type of this project in <paramref name="type"/> (itself, its element type, its
    /// generic arguments) is one the model maps. The model builder names every mapped type and a mapped
    /// type's constructor names the types of its collections; neither runs their code, so a step reaches
    /// a mapped type, and the lambda configuring it, only through code of its own that uses it.
    /// </summary>
    private bool NamesOnlyMappedTypes(Type type)
    {
        var any = false;
        var only = true;

        void Visit(Type part)
        {
            while (part.HasElementType)
            {
                part = part.GetElementType()!;
            }

            if (part.IsGenericType)
            {
                foreach (var argument in part.GetGenericArguments())
                {
                    Visit(argument);
                }

                part = part.GetGenericTypeDefinition();
            }

            if (part.IsGenericParameter || !_ours.Contains(part.Assembly))
            {
                return;
            }

            any = true;
            only &= _entities.Contains(part);
        }

        Visit(type);
        return any && only;
    }

    /// <summary>
    /// The static constructor of the regex source generator's shared helper class, which builds the
    /// character sets of every generated regex in the assembly. Each set is a field named after its own
    /// contents, so the code that uses one names what it holds; the constructor moves whenever any
    /// regex anywhere is added, which is not a change to the steps that do not use it.
    /// </summary>
    private static bool IsSharedRegexState(MethodBase method) =>
        method is ConstructorInfo { IsStatic: true, DeclaringType: { } type }
        && type.Namespace == "System.Text.RegularExpressions.Generated"
        && type.Name.EndsWith("__Utilities", StringComparison.Ordinal);

    private static IEnumerable<Type> Bases(Type type)
    {
        for (var at = type.BaseType; at is not null; at = at.BaseType)
        {
            yield return at;
        }
    }

    /// <summary>A type's fields, properties and attributes, which decide how it is read and written as data.</summary>
    private static string Shape(Type type)
    {
        const BindingFlags all = BindingFlags.DeclaredOnly | BindingFlags.Instance | BindingFlags.Static
                                 | BindingFlags.Public | BindingFlags.NonPublic;
        var text = new StringBuilder(Describe(type.BaseType ?? typeof(object)));
        foreach (var attribute in type.GetCustomAttributesData())
        {
            text.Append(" @").Append(Attribute(attribute));
        }

        foreach (var field in type.GetFields(all).OrderBy(field => field.MetadataToken))
        {
            text.Append(" f ").Append(Describe(field));
            if (field.IsLiteral)
            {
                text.Append('=').Append(field.GetRawConstantValue());
            }

            foreach (var attribute in field.GetCustomAttributesData())
            {
                text.Append(" @").Append(Attribute(attribute));
            }
        }

        foreach (var property in type.GetProperties(all).OrderBy(property => property.MetadataToken))
        {
            text.Append(" p ").Append(Normal(property.Name)).Append(':').Append(Describe(property.PropertyType));
            foreach (var attribute in property.GetCustomAttributesData())
            {
                text.Append(" @").Append(Attribute(attribute));
            }
        }

        return Hash(text);
    }

    private static string Attribute(CustomAttributeData attribute) =>
        Describe(attribute.AttributeType) + "(" +
        string.Join(",", attribute.ConstructorArguments.Select(argument => argument.ToString())
            .Concat(attribute.NamedArguments.Select(argument => argument.MemberName + "=" + argument.TypedValue))) + ")";

    private string ResourceHash(string name) =>
        _resourceHashes.GetOrAdd(name, _ =>
        {
            using var stream = _resources[name].GetManifestResourceStream(name)!;
            return Convert.ToHexString(SHA256.HashData(stream));
        });

    private static string Describe(MethodBase method) =>
        (method.DeclaringType is { } type ? Describe(type) : "") + "::" + Normal(method.Name)
        + (method is MethodInfo { IsGenericMethod: true } generic && !generic.IsGenericMethodDefinition
            ? "<" + string.Join(",", generic.GetGenericArguments().Select(Describe)) + ">"
            : "")
        + "(" + string.Join(",", method.GetParameters().Select(parameter => Describe(parameter.ParameterType))) + ")"
        + (method is MethodInfo info ? ":" + Describe(info.ReturnType) : "");

    private static string Describe(FieldInfo field) =>
        (field.DeclaringType is { } type ? Describe(type) : "") + "::" + Normal(field.Name) + ":" + Describe(field.FieldType);

    private static string Describe(Type type)
    {
        if (type.IsGenericParameter)
        {
            return (type.DeclaringMethod is null ? "!" : "!!") + type.GenericParameterPosition;
        }

        if (type.IsArray)
        {
            return Describe(type.GetElementType()!) + "[" + new string(',', type.GetArrayRank() - 1) + "]";
        }

        if (type.IsByRef)
        {
            return Describe(type.GetElementType()!) + "&";
        }

        if (type.IsPointer)
        {
            return Describe(type.GetElementType()!) + "*";
        }

        var name = type.IsNested && type.DeclaringType is { } outer
            ? Describe(outer.IsGenericType ? outer.GetGenericTypeDefinition() : outer) + "+" + Normal(type.Name)
            : (type.Namespace is { } space ? space + "." : "") + Normal(type.Name);
        return type.IsGenericType && !type.IsGenericTypeDefinition
            ? name + "<" + string.Join(",", type.GetGenericArguments().Select(Describe)) + ">"
            : name;
    }

    /// <summary>
    /// A name as the source wrote it: the numbers the compiler gives its lambdas, closures, state machines,
    /// local functions and anonymous types by their place in the file or the assembly are dropped, and so
    /// is the hash of the source file's path in the name of a file-local type (the regex generator's
    /// helpers), since one step added above another, or a build from another folder, must not move the
    /// other's digest; what those members do is in their bodies, which are read anyway.
    /// </summary>
    private static string Normal(string name) => name.Contains('<') ? Ordinal().Replace(name, "#") : name;

    [GeneratedRegex(@"(?<=__|_|\||DisplayClass|AnonymousType)\d+(_\d+)?|(?<=>F)[0-9A-F]{64}(?=__)")]
    private static partial Regex Ordinal();

    private static bool IsClosure(Type type) =>
        type.IsDefined(typeof(CompilerGeneratedAttribute), false)
        && (type.Name.Contains("DisplayClass", StringComparison.Ordinal) || type.Name == "<>c");

    private static IEnumerable<Type> Loadable(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static string Hash(StringBuilder text) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text.ToString())));

    /// <param name="Line">The method's name and the hash of its body, as the digest lists it.</param>
    private sealed record Body(
        string Line,
        IReadOnlyList<MethodBase> Methods,
        IReadOnlyList<Type> Types,
        IReadOnlyList<string> Resources,
        IReadOnlyList<string> Literals);
}
