using System.Globalization;
using System.Text;
using Microsoft.Extensions.Logging;

namespace OneLauncher.Core.Global.Logging;

/// <summary>只接收 OlanException 的 Error 记录；每次写入后关闭文件，无后台缓冲。</summary>
public sealed class ExceptionFileLogManager : ILoggerProvider
{
    private readonly object _sync = new();
    private bool _disposed;
    private bool _writing;
    private long _sequence;
    private long _failedWriteCount;
    private Exception? _lastWriteException;

    public string LogDirectory { get; }
    public string LogFilePath { get; }
    public long FailedWriteCount { get { lock (_sync) return _failedWriteCount; } }
    public Exception? LastWriteException { get { lock (_sync) return _lastWriteException; } }

    public ExceptionFileLogManager(string logDirectory)
    {
        LogDirectory = Path.GetFullPath(logDirectory);
        string session = DateTimeOffset.UtcNow.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture);
        LogFilePath = Path.Combine(LogDirectory, $"olan-error-{session}-{Environment.ProcessId}-{Guid.NewGuid():N}.log");
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private void Write<TState>(string category, EventId eventId, TState state, Exception exception,
        Func<TState, Exception?, string> formatter)
    {
        lock (_sync)
        {
            if (_disposed || _writing) return;
            _writing = true;
            try
            {
                var record = new StringBuilder();
                record.Append(DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture))
                    .Append(" [Error] ").Append(category).Append(" EventId=").Append(eventId.Id)
                    .Append(" PID=").Append(Environment.ProcessId).Append(" Sequence=").Append(++_sequence)
                    .AppendLine().AppendLine(formatter(state, exception));
                AppendException(record, exception, new HashSet<Exception>(ReferenceEqualityComparer.Instance), 0);
                if (string.IsNullOrEmpty(exception.StackTrace))
                    record.AppendLine("记录调用栈（异常尚无抛出栈）：").AppendLine(Environment.StackTrace);
                record.AppendLine();

                // 延迟到首条错误才创建目录/文件，打开即追加，不覆盖其他会话。
                Directory.CreateDirectory(LogDirectory);
                using var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.Read);
                using var writer = new StreamWriter(stream, new UTF8Encoding(false));
                writer.Write(record.ToString());
                writer.Flush();
                _lastWriteException = null;
            }
            catch (Exception error)
            {
                // 文件与格式化故障只保存状态，不抛出，不通过日志系统报告自身故障。
                _lastWriteException = error;
                _failedWriteCount++;
            }
            finally
            {
                _writing = false;
            }
        }
    }

    private static void AppendException(StringBuilder record, Exception exception, HashSet<Exception> visited, int depth)
    {
        if (depth >= 32 || !visited.Add(exception))
        {
            record.AppendLine("[异常链重复或已达到深度限制]");
            return;
        }

        record.Append("异常层级 ").Append(depth).Append(": ").Append(exception.GetType().FullName)
            .Append(": ").AppendLine(exception.Message);
        if (!string.IsNullOrEmpty(exception.StackTrace)) record.AppendLine(exception.StackTrace);
        if (exception is OlanException { OriginalException: not null } olan)
        {
            record.AppendLine("OriginalException:");
            AppendException(record, olan.OriginalException, visited, depth + 1);
        }
        if (exception is AggregateException aggregate)
        {
            foreach (var inner in aggregate.InnerExceptions)
            {
                record.AppendLine("Aggregate InnerException:");
                AppendException(record, inner, visited, depth + 1);
            }
        }
        else if (exception.InnerException is { } inner)
        {
            record.AppendLine("InnerException:");
            AppendException(record, inner, visited, depth + 1);
        }
    }

    public void Dispose()
    {
        lock (_sync) _disposed = true;
    }

    private sealed class FileLogger(ExceptionFileLogManager manager, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => logLevel == LogLevel.Error
            && category == typeof(OlanException).FullName;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(logLevel) && exception is OlanException)
                manager.Write(category, eventId, state, exception, formatter);
        }
    }
}
