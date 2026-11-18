using System.Collections.Concurrent;
using System.Text;

namespace Essenthos.Core.Build;

/// <summary>A log whose every message begins with the name of the producer that wrote it, so producers running at once read apart.</summary>
internal sealed class TaggedLogger(ILogger inner, string tag) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => inner.BeginScope(state);

    public bool IsEnabled(LogLevel logLevel) => inner.IsEnabled(logLevel);

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
        inner.Log(logLevel, eventId, state, exception, (s, e) => $"[{tag}] {formatter(s, e)}");
}

/// <summary>
/// The console, with every line a producer prints begun by its name: the producer is the one <see cref="Tag"/> names in the
/// flow that writes. A line written outside any producer (the console log's own thread) passes as it is.
/// </summary>
internal sealed class TaggedWriter(TextWriter inner) : TextWriter
{
    public static readonly AsyncLocal<string?> Tag = new();

    private readonly ConcurrentDictionary<string, bool> _midLine = new(StringComparer.Ordinal);

    public override Encoding Encoding => inner.Encoding;

    public override void Write(char value) => Write(value.ToString());

    public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

    public override void WriteLine(string? value) => Write(value + NewLine);

    public override void Write(string? value)
    {
        if (Tag.Value is not { } tag || string.IsNullOrEmpty(value))
        {
            inner.Write(value);
            return;
        }

        var tagged = new StringBuilder(value.Length + 32);
        var midLine = _midLine.GetValueOrDefault(tag);
        foreach (var letter in value)
        {
            if (!midLine && letter is not ('\r' or '\n'))
            {
                tagged.Append('[').Append(tag).Append("] ");
                midLine = true;
            }

            tagged.Append(letter);
            if (letter == '\n')
            {
                midLine = false;
            }
        }

        _midLine[tag] = midLine;
        inner.Write(tagged.ToString());
    }

    public override void Flush() => inner.Flush();
}
