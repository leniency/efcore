// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Microsoft.EntityFrameworkCore.TestUtilities;

public class TestSqlLoggerFactory : ListLoggerFactory
{
    private readonly bool _proceduralQueryGeneration = false;

    private const string FileNewLine = @"
";

    private static readonly string _eol = Environment.NewLine;

    private static readonly object _queryBaselineFileLock = new();
    private static readonly HashSet<string> _overriddenMethods = new();
    private static readonly object _queryBaselineRewritingLock = new();
    private static readonly ConcurrentDictionary<string, object> _queryBaselineRewritingLocks = new();

    public TestSqlLoggerFactory()
        : this(_ => true)
    {
    }

    public TestSqlLoggerFactory(Func<string, bool> shouldLogCategory)
        : base(c => shouldLogCategory(c) || c == DbLoggerCategory.Database.Command.Name)
    {
        Logger = new TestSqlLogger(shouldLogCategory(DbLoggerCategory.Database.Command.Name));
    }

    public IReadOnlyList<string> SqlStatements
        => ((TestSqlLogger)Logger).SqlStatements;

    public IReadOnlyList<string> Parameters
        => ((TestSqlLogger)Logger).Parameters;

    public string Sql
        => string.Join(_eol + _eol, SqlStatements);

    public void AssertBaseline(string[] expected, bool assertOrder = true)
    {
        if (_proceduralQueryGeneration)
        {
            return;
        }

        try
        {
            if (assertOrder)
            {
                for (var i = 0; i < expected.Length; i++)
                {
                    Assert.Equal(expected[i], SqlStatements[i], ignoreLineEndingDifferences: true);
                }

                Assert.Empty(SqlStatements.Skip(expected.Length));
            }
            else
            {
                foreach (var expectedFragment in expected)
                {
                    var normalizedExpectedFragment = NormalizeLineEndings(expectedFragment);
                    Assert.Contains(
                        normalizedExpectedFragment,
                        SqlStatements);
                }
            }
        }
        catch
        {
            var methodCallLine = Environment.StackTrace.Split(
                new[] { _eol },
                StringSplitOptions.RemoveEmptyEntries)[3][6..];

            var indexMethodEnding = methodCallLine.IndexOf(')') + 1;
            var testName = methodCallLine.Substring(0, indexMethodEnding);
            var parts = methodCallLine[indexMethodEnding..].Split(" ", StringSplitOptions.RemoveEmptyEntries);
            var fileName = parts[1][..^5];
            var lineNumber = int.Parse(parts[2]);

            var currentDirectory = Directory.GetCurrentDirectory();
            var logFile = currentDirectory.Substring(
                    0,
                    currentDirectory.LastIndexOf(
                        $"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}",
                        StringComparison.Ordinal)
                    + 1)
                + "QueryBaseline.txt";

            var testInfo = testName + " : " + lineNumber + FileNewLine;
            const string indent = FileNewLine + "                ";

            if (Environment.GetEnvironmentVariable("EF_TEST_REWRITE_BASELINES")?.ToUpper() is "1" or "TRUE")
            {
                RewriteSourceWithNewBaseline(fileName, lineNumber);
            }

            var sql = string.Join(
                "," + indent + "//" + indent, SqlStatements.Take(9).Select(sql => "@\"" + sql.Replace("\"", "\"\"") + "\""));

            var newBaseLine = $@"            AssertSql(
                {string.Join("," + indent + "//" + indent, SqlStatements.Take(20).Select(sql => "@\"" + sql.Replace("\"", "\"\"") + "\""))});

";

            if (SqlStatements.Count > 20)
            {
                newBaseLine += "Output truncated.";
            }

            Logger.TestOutputHelper?.WriteLine("---- New Baseline -------------------------------------------------------------------");
            Logger.TestOutputHelper?.WriteLine(newBaseLine);

            var contents = testInfo + newBaseLine + FileNewLine + "--------------------" + FileNewLine;

            var indexSimpleMethodEnding = methodCallLine.IndexOf('(');
            var indexSimpleMethodStarting = methodCallLine.LastIndexOf('.', indexSimpleMethodEnding) + 1;
            var methodName = methodCallLine.Substring(indexSimpleMethodStarting, indexSimpleMethodEnding - indexSimpleMethodStarting);

            var manipulatedSql = string.IsNullOrEmpty(sql)
                ? ""
                : @$"
{sql}";

            var overrideString = testName.Contains("Boolean async")
                ? @$"        public override async Task {methodName}(bool async)
        {{
            await base.{methodName}(async);

            AssertSql({manipulatedSql});
        }}

"
                : @$"        public override void {methodName}()
        {{
            base.{methodName}();

            AssertSql({manipulatedSql});
        }}

";

            lock (_queryBaselineFileLock)
            {
                File.AppendAllText(logFile, contents);

                // if (!_overriddenMethods.Any())
                // {
                //     File.Delete(logFile);
                // }
                //
                // if (!_overriddenMethods.Contains(methodName))
                // {
                //     File.AppendAllText(logFile, overrideString);
                //     _overriddenMethods.Add(methodName);
                // }
            }

            throw;
        }

        void RewriteSourceWithNewBaseline(string fileName, int lineNumber)
        {
            var fileLock = _queryBaselineRewritingLocks.GetOrAdd(fileName, _ => new object());
            lock (fileLock)
            {
                // Parse the file to find the line where the relevant AssertSql is
                try
                {
                    // First have Roslyn parse the file
                    SyntaxTree syntaxTree;
                    using (var stream = File.OpenRead(fileName))
                    {
                        syntaxTree = CSharpSyntaxTree.ParseText(SourceText.From(stream));
                    }

                    // Read through the source file, copying contents to a temp file (with the baseline changE)
                    using (var inputStream = File.OpenRead(fileName))
                    using (var outputStream = File.Open(fileName + ".tmp", FileMode.Create, FileAccess.Write))
                    {
                        // Detect whether a byte-order mark (BOM) exists, to write out the same
                        var buffer = new byte[3];
                        inputStream.Read(buffer, 0, 3);
                        inputStream.Position = 0;

                        var hasUtf8ByteOrderMark = (buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF);

                        using var reader = new StreamReader(inputStream);
                        using var writer = new StreamWriter(outputStream, new UTF8Encoding(hasUtf8ByteOrderMark));

                        // First find the char position where our line starts
                        var pos = 0;
                        for (var i = 0; i < lineNumber - 1; i++)
                        {
                            while (true)
                            {
                                if (reader.Peek() == -1)
                                {
                                    return;
                                }

                                pos++;
                                var ch = (char)reader.Read();
                                writer.Write(ch);
                                if (ch == '\n') // Unix
                                {
                                    break;
                                }

                                if (ch == '\r')
                                {
                                    // Mac (just \r) or Windows (\r\n)
                                    if (reader.Peek() >= 0 && (char)reader.Peek() == '\n')
                                    {
                                        _ = reader.Read();
                                        writer.Write('\n');
                                        pos++;
                                    }

                                    break;
                                }
                            }
                        }

                        // We have the character position of the line start. Skip over whitespace (that's the indent) to find the invocation
                        var indentBuilder = new StringBuilder();
                        while (true)
                        {
                            var i = reader.Peek();
                            if (i == -1)
                            {
                                return;
                            }

                            var ch = (char)i;

                            if (ch == ' ')
                            {
                                pos++;
                                indentBuilder.Append(' ');
                                reader.Read();
                                writer.Write(ch);
                            }
                            else
                            {
                                break;
                            }
                        }

                        // We are now at the start of the invocation.
                        var node = syntaxTree.GetRoot().FindNode(TextSpan.FromBounds(pos, pos));

                        // Node should be pointing at the AssertSql identifier. Go up and find the text span for the entire method invocation.
                        if (node is not IdentifierNameSyntax { Parent: InvocationExpressionSyntax invocation })
                        {
                            return;
                        }

                        // Skip over the invocation on the read side, and write the new baseline invocation
                        var tempBuf = new char[Math.Max(1024, invocation.Span.Length)];
                        reader.ReadBlock(tempBuf, 0, invocation.Span.Length);

                        indentBuilder.Append("    ");
                        var indent = indentBuilder.ToString();
                        var newBaseLine = $@"AssertSql(
{indent}{string.Join(",\n" + indent + "//\n" + indent, SqlStatements.Select(sql => "@\"" + sql.Replace("\"", "\"\"") + "\""))})";

                        writer.Write(newBaseLine);

                        // Copy the rest of the file contents as-is
                        int count;
                        while ((count = reader.ReadBlock(tempBuf, 0, 1024)) > 0)
                        {
                            writer.Write(tempBuf, 0, count);
                        }
                    }
                }
                catch
                {
                    File.Delete(fileName + ".tmp");
                    throw;
                }

                File.Move(fileName + ".tmp", fileName, overwrite: true);
            }
        }
    }

    protected class TestSqlLogger : ListLogger
    {
        private readonly bool _shouldLogCommands;

        public TestSqlLogger(bool shouldLogCommands)
        {
            _shouldLogCommands = shouldLogCommands;
        }

        public List<string> SqlStatements { get; } = new();
        public List<string> Parameters { get; } = new();

        private readonly StringBuilder _stringBuilder = new();

        protected override void UnsafeClear()
        {
            base.UnsafeClear();

            SqlStatements.Clear();
            Parameters.Clear();
        }

        protected override void UnsafeLog<TState>(
            LogLevel logLevel,
            EventId eventId,
            string message,
            TState state,
            Exception exception)
        {
            if ((eventId.Id == RelationalEventId.CommandExecuted.Id
                    || eventId.Id == RelationalEventId.CommandError.Id
                    || eventId.Id == RelationalEventId.CommandExecuting.Id))
            {
                if (_shouldLogCommands)
                {
                    base.UnsafeLog(logLevel, eventId, message, state, exception);
                }

                if (!IsRecordingSuspended
                    && message != null
                    && eventId.Id != RelationalEventId.CommandExecuting.Id)
                {
                    var structure = (IReadOnlyList<KeyValuePair<string, object>>)state;

                    var parameters = structure.Where(i => i.Key == "parameters").Select(i => (string)i.Value).First();
                    var commandText = structure.Where(i => i.Key == "commandText").Select(i => (string)i.Value).First();

                    if (!string.IsNullOrWhiteSpace(parameters))
                    {
                        Parameters.Add(parameters);

                        _stringBuilder.Clear();

                        var inQuotes = false;
                        var inCurlies = false;
                        for (var i = 0; i < parameters.Length; i++)
                        {
                            var c = parameters[i];
                            switch (c)
                            {
                                case '\'':
                                    inQuotes = !inQuotes;
                                    goto default;
                                case '{':
                                    inCurlies = true;
                                    goto default;
                                case '}':
                                    inCurlies = false;
                                    goto default;
                                case ',' when parameters[i + 1] == ' ' && !inQuotes && !inCurlies:
                                    _stringBuilder.Append(_eol);
                                    i++;
                                    continue;
                                default:
                                    _stringBuilder.Append(c);
                                    continue;
                            }
                        }

                        _stringBuilder.Append(_eol).Append(_eol);
                        parameters = _stringBuilder.ToString();
                    }

                    SqlStatements.Add(parameters + commandText);
                }
            }
            else
            {
                base.UnsafeLog(logLevel, eventId, message, state, exception);
            }
        }
    }
}
