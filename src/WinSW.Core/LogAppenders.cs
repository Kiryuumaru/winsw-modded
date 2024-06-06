using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using WinSW.Util;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace WinSW
{
    public interface IEventLogger
    {
        void WriteEntry(string message);

        void WriteEntry(string message, EventLogEntryType type);
    }

    internal sealed class TempLogHandler : AbstractFileLogAppender
    {
        private readonly string? outputPath;
        private readonly string? errorPath;
        private readonly string? combinedPath;

        public TempLogHandler(string? outputPath, string? errorPath, string? combinedPath)
            : base(string.Empty, string.Empty, IsDisabled(outputPath), IsDisabled(errorPath), IsDisabled(combinedPath), string.Empty, string.Empty, string.Empty)
        {
            this.outputPath = outputPath;
            this.errorPath = errorPath;
            this.combinedPath = combinedPath;
        }

        private static bool IsDisabled(string? path) => string.IsNullOrEmpty(path) || path!.Equals("NUL", StringComparison.OrdinalIgnoreCase);

        protected override Task LogOutput(StreamReader outputReader)
        {
            return this.CopyStreamAsync([outputReader.BaseStream], new FileStream(this.outputPath!, FileMode.OpenOrCreate));
        }

        protected override Task LogError(StreamReader errorReader)
        {
            return this.CopyStreamAsync([errorReader.BaseStream], new FileStream(this.errorPath!, FileMode.OpenOrCreate));
        }

        protected override Task LogCombined(StreamReader outputReader, StreamReader errorReader)
        {
            return this.CopyStreamAsync([outputReader.BaseStream, errorReader.BaseStream], new FileStream(this.errorPath!, FileMode.OpenOrCreate));
        }
    }

    /// <summary>
    /// Abstraction for handling log.
    /// </summary>
    public abstract class LogHandler
    {
#pragma warning disable CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        protected LogHandler(bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled)
#pragma warning restore CS8618 // Non-nullable field is uninitialized. Consider declaring as nullable.
        {
            this.OutFileDisabled = outFileDisabled;
            this.ErrFileDisabled = errFileDisabled;
            this.CombinedFileDisabled = combinedFileDisabled;
        }

        /// <summary>
        /// Error and information about logging should be reported here.
        /// </summary>
        public IEventLogger EventLogger { get; set; }

        public bool OutFileDisabled { get; }

        public bool ErrFileDisabled { get; }

        public bool CombinedFileDisabled { get; }

        public abstract void Log(StreamReader outputReader, StreamReader errorReader);

        /// <summary>
        /// Convenience method to copy stuff from StreamReader to StreamWriter
        /// </summary>
        protected async Task CopyStreamAsync(Stream[] readers, Stream writer)
        {
            var copy = new StreamCopyOperation(readers, writer);
            while (await copy.CopyLineAsync() != 0)
            {
            }

            foreach (var reader in readers)
            {
                reader.Dispose();
            }

            writer.Dispose();
        }

        /// <summary>
        /// File replacement.
        /// </summary>
        protected void MoveFile(string sourceFileName, string destFileName)
        {
            try
            {
                FileHelper.MoveOrReplaceFile(sourceFileName, destFileName);
            }
            catch (IOException e)
            {
                this.EventLogger.WriteEntry("Failed to move :" + sourceFileName + " to " + destFileName + " because " + e.Message);
            }
        }
    }

    /// <summary>
    /// Base class for file-based loggers
    /// </summary>
    public abstract class AbstractFileLogAppender : LogHandler
    {
        protected string BaseLogFileName { get; }

        protected string OutFilePattern { get; }

        protected string ErrFilePattern { get; }

        protected string CombinedFilePattern { get; }

        protected AbstractFileLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern)
            : base(outFileDisabled, errFileDisabled, combinedFileDisabled)
        {
            this.BaseLogFileName = Path.Combine(logDirectory, baseName);
            this.OutFilePattern = outFilePattern;
            this.ErrFilePattern = errFilePattern;
            this.CombinedFilePattern = combinedFilePattern;
        }

        public override void Log(StreamReader outputReader, StreamReader errorReader)
        {
            if (!this.OutFileDisabled)
            {
                this.SafeLogOutput(outputReader);
            }

            if (!this.ErrFileDisabled)
            {
                this.SafeLogError(errorReader);
            }

            if (!this.CombinedFileDisabled)
            {
                this.SafeLogCombined(outputReader, errorReader);
            }
        }

        protected abstract Task LogOutput(StreamReader outputReader);

        protected abstract Task LogError(StreamReader errorReader);

        protected abstract Task LogCombined(StreamReader outputReader, StreamReader errorReader);

        private async void SafeLogOutput(StreamReader outputReader)
        {
            try
            {
                await this.LogOutput(outputReader);
            }
            catch (Exception e)
            {
                this.EventLogger.WriteEntry("Unhandled exception in task. " + e, EventLogEntryType.Error);
            }
        }

        private async void SafeLogError(StreamReader errorReader)
        {
            try
            {
                await this.LogError(errorReader);
            }
            catch (Exception e)
            {
                this.EventLogger.WriteEntry("Unhandled exception in task. " + e, EventLogEntryType.Error);
            }
        }

        private async void SafeLogCombined(StreamReader outputReader, StreamReader errorReader)
        {
            try
            {
                await this.LogCombined(outputReader, errorReader);
            }
            catch (Exception e)
            {
                this.EventLogger.WriteEntry("Unhandled exception in task. " + e, EventLogEntryType.Error);
            }
        }
    }

    public abstract class SimpleLogAppender : AbstractFileLogAppender
    {
        public FileMode FileMode { get; }

        public string OutputLogFileName { get; }

        public string ErrorLogFileName { get; }

        public string CombinedLogFileName { get; }

        protected SimpleLogAppender(string logDirectory, string baseName, FileMode fileMode, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern)
            : base(logDirectory, baseName, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
            this.FileMode = fileMode;
            this.OutputLogFileName = this.BaseLogFileName + this.OutFilePattern;
            this.ErrorLogFileName = this.BaseLogFileName + this.ErrFilePattern;
            this.CombinedLogFileName = this.BaseLogFileName + this.CombinedFilePattern;
        }

        protected override Task LogOutput(StreamReader outputReader)
        {
            return this.CopyStreamAsync([outputReader.BaseStream], new FileStream(this.OutputLogFileName, this.FileMode));
        }

        protected override Task LogError(StreamReader errorReader)
        {
            return this.CopyStreamAsync([errorReader.BaseStream], new FileStream(this.ErrorLogFileName, this.FileMode));
        }

        protected override Task LogCombined(StreamReader outputReader, StreamReader errorReader)
        {
            return this.CopyStreamAsync([outputReader.BaseStream, errorReader.BaseStream], new FileStream(this.ErrorLogFileName, this.FileMode));
        }
    }

    public class DefaultLogAppender : SimpleLogAppender
    {
        public DefaultLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern)
            : base(logDirectory, baseName, FileMode.Append, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
        }
    }

    public class ResetLogAppender : SimpleLogAppender
    {
        public ResetLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern)
            : base(logDirectory, baseName, FileMode.Create, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
        }
    }

    /// <summary>
    /// LogHandler that throws away output
    /// </summary>
    public class IgnoreLogAppender : LogHandler
    {
        public IgnoreLogAppender()
            : base(true, true, true)
        {
        }

        public override void Log(StreamReader outputReader, StreamReader errorReader)
        {
        }
    }

    public class TimeBasedRollingLogAppender : AbstractFileLogAppender
    {
        public string Pattern { get; }

        public int Period { get; }

        public TimeBasedRollingLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern, string pattern, int period)
            : base(logDirectory, baseName, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
            this.Pattern = pattern;
            this.Period = period;
        }

        protected override Task LogOutput(StreamReader outputReader)
        {
            return this.CopyStreamWithDateRotationAsync([outputReader], this.OutFilePattern);
        }

        protected override Task LogError(StreamReader errorReader)
        {
            return this.CopyStreamWithDateRotationAsync([errorReader], this.ErrFilePattern);
        }

        protected override Task LogCombined(StreamReader outputReader, StreamReader errorReader)
        {
            return this.CopyStreamWithDateRotationAsync([outputReader, errorReader], this.CombinedFilePattern);
        }

        /// <summary>
        /// Works like the CopyStream method but does a log rotation based on time.
        /// </summary>
        private async Task CopyStreamWithDateRotationAsync(StreamReader[] readers, string ext)
        {
            var periodicRollingCalendar = new PeriodicRollingCalendar(this.Pattern, this.Period);
            periodicRollingCalendar.Init();

            var writer = new FileStream(this.BaseLogFileName + "_" + periodicRollingCalendar.Format + ext, FileMode.Append);
            var copy = new StreamCopyOperation(readers.Select(i => i.BaseStream).ToArray(), writer);
            while (await copy.CopyLineAsync() != 0)
            {
                if (periodicRollingCalendar.ShouldRoll)
                {
                    writer.Dispose();
                    copy.Writer = writer = new FileStream(this.BaseLogFileName + "_" + periodicRollingCalendar.Format + ext, FileMode.Create);
                }
            }

            foreach (var reader in readers)
            {
                reader.Dispose();
            }

            writer.Dispose();
        }
    }

    public class SizeBasedRollingLogAppender : AbstractFileLogAppender
    {
        public const int BytesPerKB = 1024;
        public const int BytesPerMB = 1024 * BytesPerKB;
        public const int DefaultSizeThreshold = 10 * BytesPerMB; // roll every 10MB.
        public const int DefaultFilesToKeep = 8;

        public int SizeThreshold { get; }

        public int FilesToKeep { get; }

        public SizeBasedRollingLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern, int sizeThreshold, int filesToKeep)
            : base(logDirectory, baseName, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
            this.SizeThreshold = sizeThreshold;
            this.FilesToKeep = filesToKeep;
        }

        public SizeBasedRollingLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern)
            : this(logDirectory, baseName, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern, DefaultSizeThreshold, DefaultFilesToKeep)
        {
        }

        protected override Task LogOutput(StreamReader outputReader)
        {
            return this.CopyStreamWithRotationAsync([outputReader], this.OutFilePattern);
        }

        protected override Task LogError(StreamReader errorReader)
        {
            return this.CopyStreamWithRotationAsync([errorReader], this.ErrFilePattern);
        }

        protected override Task LogCombined(StreamReader outputReader, StreamReader errorReader)
        {
            return this.CopyStreamWithRotationAsync([outputReader, errorReader], this.CombinedFilePattern);
        }

        /// <summary>
        /// Works like the CopyStream method but does a log rotation.
        /// </summary>
        private async Task CopyStreamWithRotationAsync(StreamReader[] readers, string ext)
        {
            var writer = new FileStream(this.BaseLogFileName + ext, FileMode.Append);
            var copy = new StreamCopyOperation(readers.Select(i => i.BaseStream).ToArray(), writer);
            long fileLength = new FileInfo(this.BaseLogFileName + ext).Length;

            int written;
            while ((written = await copy.CopyLineAsync()) != 0)
            {
                fileLength += written;
                if (fileLength > this.SizeThreshold)
                {
                    writer.Dispose();

                    try
                    {
                        for (int j = this.FilesToKeep; j >= 2; j--)
                        {
                            string dst = this.BaseLogFileName + "." + (j - 1) + ext;
                            string src = this.BaseLogFileName + "." + (j - 2) + ext;
                            if (File.Exists(dst))
                            {
                                File.Delete(dst);
                            }

                            if (File.Exists(src))
                            {
                                File.Move(src, dst);
                            }
                        }

                        File.Move(this.BaseLogFileName + ext, this.BaseLogFileName + ".0" + ext);
                    }
                    catch (IOException e)
                    {
                        this.EventLogger.WriteEntry("Failed to roll log: " + e.Message);
                    }

                    // even if the log rotation fails, create a new one, or else
                    // we'll infinitely try to roll.
                    copy.Writer = writer = new FileStream(this.BaseLogFileName + ext, FileMode.Create);
                    fileLength = new FileInfo(this.BaseLogFileName + ext).Length;
                }
            }

            foreach (var reader in readers)
            {
                reader.Dispose();
            }

            writer.Dispose();
        }
    }

    /// <summary>
    /// Roll log when a service is newly started.
    /// </summary>
    public class RollingLogAppender : SimpleLogAppender
    {
        public RollingLogAppender(string logDirectory, string baseName, bool outFileDisabled, bool errFileDisabled, bool combinedFileDisabled, string outFilePattern, string errFilePattern, string combinedFilePattern)
            : base(logDirectory, baseName, FileMode.Append, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
        }

        public override void Log(StreamReader outputReader, StreamReader errorReader)
        {
            if (!this.OutFileDisabled)
            {
                this.MoveFile(this.OutputLogFileName, this.OutputLogFileName + ".old");
            }

            if (!this.ErrFileDisabled)
            {
                this.MoveFile(this.ErrorLogFileName, this.ErrorLogFileName + ".old");
            }

            if (!this.CombinedFileDisabled)
            {
                this.MoveFile(this.CombinedLogFileName, this.CombinedLogFileName + ".old");
            }

            base.Log(outputReader, errorReader);
        }
    }

    public class RollingSizeTimeLogAppender : AbstractFileLogAppender
    {
        public const int BytesPerKB = 1024;

        public int SizeThreshold { get; }

        public string FilePattern { get; }

        public TimeSpan? AutoRollAtTime { get; }

        public int? ZipOlderThanNumDays { get; }

        public string ZipDateFormat { get; }

        public RollingSizeTimeLogAppender(
            string logDirectory,
            string baseName,
            bool outFileDisabled,
            bool errFileDisabled,
            bool combinedFileDisabled,
            string outFilePattern,
            string errFilePattern,
            string combinedFilePattern,
            int sizeThreshold,
            string filePattern,
            TimeSpan? autoRollAtTime,
            int? zipolderthannumdays,
            string zipdateformat)
            : base(logDirectory, baseName, outFileDisabled, errFileDisabled, combinedFileDisabled, outFilePattern, errFilePattern, combinedFilePattern)
        {
            this.SizeThreshold = sizeThreshold;
            this.FilePattern = filePattern;
            this.AutoRollAtTime = autoRollAtTime;
            this.ZipOlderThanNumDays = zipolderthannumdays;
            this.ZipDateFormat = zipdateformat;
        }

        protected override Task LogOutput(StreamReader outputReader)
        {
            return this.CopyStreamWithRotationAsync([outputReader], this.OutFilePattern);
        }

        protected override Task LogError(StreamReader errorReader)
        {
            return this.CopyStreamWithRotationAsync([errorReader], this.ErrFilePattern);
        }

        protected override Task LogCombined(StreamReader outputReader, StreamReader errorReader)
        {
            return this.CopyStreamWithRotationAsync([outputReader, errorReader], this.CombinedFilePattern);
        }

        private async Task CopyStreamWithRotationAsync(StreamReader[] readers, string extension)
        {
            // lock required as the timer thread and the thread that will write to the stream could try and access the file stream at the same time
            object? fileLock = new();

            string? baseDirectory = Path.GetDirectoryName(this.BaseLogFileName)!;
            string? baseFileName = Path.GetFileName(this.BaseLogFileName);
            string? logFile = this.BaseLogFileName + extension;

            var writer = new FileStream(logFile, FileMode.Append);
            var copy = new StreamCopyOperation(readers.Select(i => i.BaseStream).ToArray(), writer);
            long fileLength = new FileInfo(logFile).Length;

            // We auto roll at time is configured then we need to create a timer and wait until time is elasped and roll the file over
            if (this.AutoRollAtTime is TimeSpan autoRollAtTime)
            {
                // Run at start
                double tickTime = this.SetupRollTimer(autoRollAtTime);
                var timer = new System.Timers.Timer(tickTime);
                timer.Elapsed += (_, _) =>
                {
                    try
                    {
                        timer.Stop();
                        lock (fileLock)
                        {
                            writer.Dispose();

                            var now = DateTime.Now.AddDays(-1);
                            int nextFileNumber = this.GetNextFileNumber(extension, baseDirectory, baseFileName, now);
                            string? nextFileName = Path.Combine(baseDirectory, string.Format("{0}.{1}.#{2:D4}{3}", baseFileName, now.ToString(this.FilePattern), nextFileNumber, extension));
                            File.Move(logFile, nextFileName);

                            copy.Writer = writer = new FileStream(logFile, FileMode.Create);
                            fileLength = new FileInfo(logFile).Length;
                        }

                        // Next day so check if file can be zipped
                        this.ZipFiles(baseDirectory, extension, baseFileName);
                    }
                    catch (Exception ex)
                    {
                        this.EventLogger.WriteEntry($"Failed to to trigger auto roll at time event due to: {ex.Message}");
                    }
                    finally
                    {
                        // Recalculate the next interval
                        timer.Interval = this.SetupRollTimer(autoRollAtTime);
                        timer.Start();
                    }
                };
                timer.Start();
            }

            int written;
            while ((written = await copy.CopyLineAsync()) != 0)
            {
                lock (fileLock)
                {
                    fileLength += written;
                    if (fileLength > this.SizeThreshold)
                    {
                        try
                        {
                            // roll file
                            var now = DateTime.Now;
                            int nextFileNumber = this.GetNextFileNumber(extension, baseDirectory, baseFileName, now);
                            string? nextFileName = Path.Combine(
                                    baseDirectory,
                                    string.Format("{0}.{1}.#{2:D4}{3}", baseFileName, now.ToString(this.FilePattern), nextFileNumber, extension));
                            File.Move(logFile, nextFileName);

                            // even if the log rotation fails, create a new one, or else
                            // we'll infinitely try to roll.
                            copy.Writer = writer = new FileStream(logFile, FileMode.Create);
                            fileLength = new FileInfo(logFile).Length;
                        }
                        catch (Exception e)
                        {
                            this.EventLogger.WriteEntry($"Failed to roll size time log: {e.Message}");
                        }
                    }
                }
            }

            foreach (var reader in readers)
            {
                reader.Dispose();
            }

            writer.Dispose();
        }

        private void ZipFiles(string directory, string fileExtension, string zipFileBaseName)
        {
            if (this.ZipOlderThanNumDays is null || this.ZipOlderThanNumDays <= 0)
            {
                return;
            }

            try
            {
                foreach (string path in Directory.GetFiles(directory, "*" + fileExtension))
                {
                    var fileInfo = new FileInfo(path);
                    if (fileInfo.LastWriteTimeUtc >= DateTime.UtcNow.AddDays(-this.ZipOlderThanNumDays.Value))
                    {
                        continue;
                    }

                    string sourceFileName = Path.GetFileName(path);
                    string zipFilePattern = fileInfo.LastAccessTimeUtc.ToString(this.ZipDateFormat);
                    string zipFilePath = Path.Combine(directory, $"{zipFileBaseName}.{zipFilePattern}.zip");
                    this.ZipOneFile(path, sourceFileName, zipFilePath);

                    File.Delete(path);
                }
            }
            catch (Exception e)
            {
                this.EventLogger.WriteEntry($"Failed to Zip files. Error {e.Message}");
            }
        }

        private void ZipOneFile(string sourceFilePath, string entryName, string zipFilePath)
        {
            ZipArchive? zipArchive = null;
            try
            {
                zipArchive = ZipFile.Open(zipFilePath, ZipArchiveMode.Update);

                if (zipArchive.GetEntry(entryName) is null)
                {
                    zipArchive.CreateEntryFromFile(sourceFilePath, entryName);
                }
            }
            catch (Exception e)
            {
                this.EventLogger.WriteEntry($"Failed to Zip the File {sourceFilePath}. Error {e.Message}");
            }
            finally
            {
                zipArchive?.Dispose();
            }
        }

        private double SetupRollTimer(TimeSpan autoRollAtTime)
        {
            var nowTime = DateTime.Now;
            var scheduledTime = new DateTime(
                nowTime.Year,
                nowTime.Month,
                nowTime.Day,
                autoRollAtTime.Hours,
                autoRollAtTime.Minutes,
                autoRollAtTime.Seconds,
                0);
            if (nowTime > scheduledTime)
            {
                scheduledTime = scheduledTime.AddDays(1);
            }

            double tickTime = (scheduledTime - DateTime.Now).TotalMilliseconds;
            return tickTime;
        }

        private int GetNextFileNumber(string ext, string baseDirectory, string baseFileName, DateTime now)
        {
            int nextFileNumber = 0;
            string[]? files = Directory.GetFiles(baseDirectory, string.Format("{0}.{1}.#*{2}", baseFileName, now.ToString(this.FilePattern), ext));
            if (files.Length == 0)
            {
                nextFileNumber = 1;
            }
            else
            {
                foreach (string? f in files)
                {
                    try
                    {
                        string? filenameOnly = Path.GetFileNameWithoutExtension(f);
                        int hashIndex = filenameOnly.IndexOf('#');
                        string? lastNumberAsString = filenameOnly.Substring(hashIndex + 1, 4);
                        if (int.TryParse(lastNumberAsString, out int lastNumber))
                        {
                            if (lastNumber > nextFileNumber)
                            {
                                nextFileNumber = lastNumber;
                            }
                        }
                        else
                        {
                            throw new IOException($"File {f} does not follow the pattern provided");
                        }
                    }
                    catch (Exception e)
                    {
                        throw new IOException($"Failed to process file {f} due to error {e.Message}", e);
                    }
                }

                if (nextFileNumber == 0)
                {
                    throw new IOException("Cannot roll the file because matching pattern not found");
                }

                nextFileNumber++;
            }

            return nextFileNumber;
        }
    }

    internal sealed class StreamCopyOperation
    {
        private const int BufferSize = 1024;
        private readonly Stream[] readers;

        private int[] startIndex;
        private int[] endIndex;

        internal Stream Writer;

        internal StreamCopyOperation(Stream reader, Stream writer)
        {
            this.readers = [reader];
            this.startIndex = [0];
            this.endIndex = [0];
            this.Writer = writer;
        }

        internal StreamCopyOperation(Stream[] readers, Stream writer)
        {
            this.readers = readers;
            this.startIndex = new int[readers.Length];
            this.endIndex = new int[readers.Length];
            this.Writer = writer;

            for (int i = 0; i < this.startIndex.Length; i++)
            {
                this.startIndex[i] = 0;
            }

            for (int i = 0; i < this.endIndex.Length; i++)
            {
                this.endIndex[i] = 0;
            }
        }

        internal async Task<int> CopyLineAsync()
        {
            List<Task> tasks = [];
            int allTotal = 0;
            SemaphoreSlim allTotalLocker = new(1);
            SemaphoreSlim writerLocker = new(1);

            for (int i = 0; i < this.readers.Length; i++)
            {
                int index = i;
                tasks.Add(Task.Run(async () =>
                {
                    byte[] buffer = new byte[BufferSize];
                    var source = this.readers[index];
                    int startIndex = this.startIndex[index];
                    int endIndex = this.endIndex[index];
                    var destination = this.Writer;

                    int total = 0;
                    while (true)
                    {
                        if (startIndex == 0)
                        {
                            if ((endIndex = await source.ReadAsync(buffer, 0, BufferSize)) == 0)
                            {
                                break;
                            }
                        }

                        int buffered = endIndex - startIndex;

                        int newLineIndex = Array.IndexOf(buffer, (byte)'\n', startIndex, buffered);
                        if (newLineIndex >= 0)
                        {
                            int count = newLineIndex - startIndex + 1;
                            total += count;
                            try
                            {
                                await writerLocker.WaitAsync();
                                destination.Write(buffer, startIndex, count);
                                destination.Flush();
                            }
                            finally
                            {
                                writerLocker.Release();
                            }

                            startIndex = (newLineIndex + 1) % BufferSize;
                            break;
                        }

                        total += buffered;
                        try
                        {
                            await writerLocker.WaitAsync();
                            destination.Write(buffer, startIndex, buffered);
                            destination.Flush();
                        }
                        finally
                        {
                            writerLocker.Release();
                        }

                        startIndex = 0;
                    }

                    this.startIndex[index] = startIndex;
                    this.endIndex[index] = endIndex;

                    try
                    {
                        await allTotalLocker.WaitAsync();
                        allTotal += total;
                    }
                    finally
                    {
                        allTotalLocker.Release();
                    }
                }));
            }

            await Task.WhenAll(tasks);

            return allTotal;
        }
    }
}
