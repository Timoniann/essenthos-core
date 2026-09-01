using System.Collections.ObjectModel;
using System.Reflection;
using System.Text;

namespace Essenthos.Core.Csv;

public class CsvParser
{
    private readonly Options _options;

    public CsvParser(Options options)
    {
        _options = options;
    }

    public IList<T> ParseFromFile<T>(string path)
    {
        return Parse<T>(new StreamReader(path));
    }

    public IList<T> Parse<T>(string content)
    {
        return Parse<T>(new StreamReader(new MemoryStream(Encoding.UTF8.GetBytes(content))));
    }

    public IList<T> Parse<T>(StreamReader reader)
    {
        var separator = _options.Separator;
        var hasHeader = _options.HasHeader;
        List<T> result = [];
        var headers = hasHeader ? ParseHeaders(reader, separator) : null;
        var props = GetProps<T>(headers);
        DoParse(reader, separator, props, result);
        return result;
    }

    private void DoParse<T>(StreamReader reader, char separator, IProperty[] props, List<T> result)
    {
        List<string> values = [];
        List<char> chars = [];
        while (!reader.EndOfStream)
        {
            var c = reader.Read();
            if (c == separator)
            {
                values.Add(new string(chars.ToArray()));
                chars.Clear();
                continue;
            }

            if (c == '\n')
            {
                if (values.Count <= 0)
                {
                    continue;
                }

                values.Add(new string(chars.ToArray()));
                chars.Clear();
                var obj = Activator.CreateInstance<T>();
                var count = Math.Min(values.Count, props.Length);
                for (var i = 0; i < count; i++)
                {
                    props[i].SetValue(obj!, values[i]);
                }

                result.Add(obj);
                values.Clear();
                continue;
            }

            if (c == '\r' && reader.Peek() == '\n')
            {
                continue;
            }

            if (c == '"' && chars.Count == 0)
            {
                while (!reader.EndOfStream)
                {
                    c = reader.Read();
                    if (c != '"')
                    {
                        chars.Add((char)c);
                        continue;
                    }

                    var next = reader.Peek();
                    if (next == '"')
                    {
                        reader.Read();
                        chars.Add('"');
                        continue;
                    }

                    break;
                }

                continue;
            }

            chars.Add((char)c);
        }

        if (values.Count <= 0)
        {
            return;
        }

        {
            values.Add(new string(chars.ToArray()));
            var obj = Activator.CreateInstance<T>();
            var count = Math.Min(values.Count, props.Length);
            for (var i = 0; i < count; i++)
            {
                props[i].SetValue(obj!, values[i]);
            }

            result.Add(obj);
        }
    }

    private IProperty[] GetProps<T>(List<string>? headers)
    {
        var type = typeof(T);
        var props = type.GetProperties();
        if (headers == null)
        {
            return props.Select(IProperty.Create).ToArray();
        }

        var count = headers.Count;
        var result = new IProperty[count];
        for (var i = 0; i < count; i++)
        {
            var header = headers[i];
            foreach (var prop in props)
            {
                if (prop.Name == header || prop.Name.Equals(header, StringComparison.OrdinalIgnoreCase))
                {
                    result[i] = IProperty.Create(prop);
                    goto Next;
                }
            }

            result[i] = new NullProperty();
            Next: ;
        }

        return result;
    }

    private List<string> ParseHeaders(StreamReader reader, char separator)
    {
        List<string> headers = [];
        List<char> chars = [];
        while (!reader.EndOfStream)
        {
            var c = reader.Read();
            if (c == separator)
            {
                headers.Add(new string(chars.ToArray()));
                chars.Clear();
            }
            else if (c == '\n')
            {
                headers.Add(new string(chars.ToArray()));
                chars.Clear();
                break;
            }
            else if (c != '\r')
            {
                chars.Add((char)c);
            }
        }

        return headers;
    }

    public static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) != null)
        {
            return true;
        }

        if (property.PropertyType.IsValueType)
        {
            return false;
        }

        var nullable = property.CustomAttributes.FirstOrDefault(x => x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableAttribute");

        if (nullable is { ConstructorArguments.Count: 1 })
        {
            var arg = nullable.ConstructorArguments[0];
            if (arg.ArgumentType != typeof(byte[]))
            {
                return (byte)arg.Value! == 2;
            }

            var flags = (ReadOnlyCollection<CustomAttributeTypedArgument>)arg.Value!;
            return (byte)flags[0].Value! == 2;
        }

        var context = property.DeclaringType?
            .CustomAttributes.FirstOrDefault(x =>
                x.AttributeType.FullName == "System.Runtime.CompilerServices.NullableContextAttribute");

        if (context is not { ConstructorArguments.Count: 1 } ||
            context.ConstructorArguments[0].ArgumentType != typeof(byte))
        {
            return false;
        }

        var flag = (byte)context.ConstructorArguments[0].Value!;
        return flag == 2;
    }

    public class Options
    {
        public bool HasHeader { get; init; } = true;

        public char Separator { get; init; } = ',';
    }


    private interface IProperty
    {
        void SetValue(object obj, string value);

        static IProperty Create(PropertyInfo info)
        {
            if (info.PropertyType == typeof(string))
            {
                return new StringProperty(info);
            }

            if (info.PropertyType == typeof(int) || info.PropertyType == typeof(int?))
            {
                return new IntegerProperty(info);
            }

            if (info.PropertyType == typeof(float) || info.PropertyType == typeof(float?))
            {
                return new FloatProperty(info);
            }

            if (info.PropertyType == typeof(double) || info.PropertyType == typeof(double?))
            {
                return new DoubleProperty(info);
            }

            if (info.PropertyType == typeof(bool) || info.PropertyType == typeof(bool?))
            {
                return new BoolProperty(info);
            }

            if (info.PropertyType == typeof(List<string>))
            {
                return new ListProperty(info);
            }

            if (info.PropertyType == typeof(string[]) || info.PropertyType == typeof(IList<string>))
            {
                return new ArrayProperty(info);
            }

            throw new Exception("Invalid property type: " + info.PropertyType.Name);
        }
    }

    private class NullProperty : IProperty
    {
        public void SetValue(object obj, string value)
        {
        }
    }

    private class StringProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            info.SetValue(obj, value);
        }
    }

    private class IntegerProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            if (!int.TryParse(value, out var intValue))
            {
                throw new FormatException("Invalid integer value.");
            }

            info.SetValue(obj, intValue);
        }
    }

    private class FloatProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            if (!float.TryParse(value, out var floatValue))
            {
                throw new FormatException("Invalid float value.");
            }

            info.SetValue(obj, floatValue);
        }
    }

    private class DoubleProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            if (!double.TryParse(value, out var doubleValue))
            {
                throw new FormatException("Invalid double value.");
            }

            info.SetValue(obj, doubleValue);
        }
    }

    private class BoolProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            info.SetValue(obj, value is "checked" or "true" or "True" or "yes" or "Yes" or "1");
        }
    }

    private class ListProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            info.SetValue(obj, SplitValues(value));
        }
    }

    private class ArrayProperty(PropertyInfo info) : IProperty
    {
        private readonly bool _isNullable = IsNullable(info);

        public void SetValue(object obj, string value)
        {
            if (_isNullable && string.IsNullOrEmpty(value))
            {
                info.SetValue(obj, null);
                return;
            }

            info.SetValue(obj, SplitValues(value).ToArray());
        }
    }

    private static List<string> SplitValues(string value)
    {
        var count = value.Length;
        if (count == 0)
        {
            return [];
        }

        List<string> result = [];
        var start = 0;
        for (var i = 0; i < count; i++)
        {
            var c = value[i];
            switch (c)
            {
                case ',':
                    result.Add(value.Substring(start, i - start));
                    start = i + 1;
                    continue;
                case ' ' when i == start:
                    start++;
                    continue;
            }
        }

        result.Add(value[start..]);

        return result;
    }
}