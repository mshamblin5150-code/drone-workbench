using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

public static class BoundedRun
{
    public sealed class Result
    {
        public string Failure = "None";
        public int ExitCode = -1;
        public double WallMs;
        public double CpuMs;
        public bool Contained;
        public byte[] Output = new byte[0];
    }

    public static Result Run(string file, string[] arguments, string workingDirectory,
        IDictionary<string, string> setVariables, string[] removeVariables, int timeoutMs, int outputLimit)
    {
        var start = new ProcessStartInfo(file)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) { start.ArgumentList.Add(argument); }
        foreach (var name in removeVariables) { start.Environment.Remove(name); }
        foreach (var pair in setVariables) { start.Environment[pair.Key] = pair.Value; }

        var result = new Result();
        var clock = Stopwatch.StartNew();
        Process process = null;
        SafeFileHandle job = null;
        try
        {
            process = Process.Start(start);
            if (process == null) { result.Failure = "StartFailed"; return result; }
            job = Contain(process);
            result.Contained = job != null;
            process.StandardInput.Close();
            using var overLimit = new CancellationTokenSource();
            var consumed = 0;
            async Task<byte[]> ReadAsync(Stream stream, bool keep)
            {
                using var kept = new MemoryStream();
                var buffer = new byte[4096];
                int count;
                while ((count = await stream.ReadAsync(buffer, 0, buffer.Length, overLimit.Token)) > 0)
                {
                    if (Interlocked.Add(ref consumed, count) > outputLimit) { overLimit.Cancel(); break; }
                    if (keep) { kept.Write(buffer, 0, count); }
                }
                return kept.ToArray();
            }

            var output = ReadAsync(process.StandardOutput.BaseStream, true);
            var errors = ReadAsync(process.StandardError.BaseStream, false);
            var finished = false;
            try
            {
                finished = Task.WhenAll(output, errors, process.WaitForExitAsync()).Wait(timeoutMs, overLimit.Token);
            }
            catch (OperationCanceledException)
            {
            }
            catch (AggregateException)
            {
                finished = process.HasExited;
            }

            if (overLimit.IsCancellationRequested || !finished)
            {
                result.Failure = overLimit.IsCancellationRequested ? "OutputTooLarge" : "Timeout";
                Stop(process, job);
                try { Task.WhenAll(output, errors).Wait(5000); } catch (AggregateException) { }
                return result;
            }

            result.ExitCode = process.ExitCode;
            result.Output = output.IsCompletedSuccessfully ? output.Result : new byte[0];
            return result;
        }
        catch (Exception error) when (error is System.ComponentModel.Win32Exception || error is InvalidOperationException || error is IOException)
        {
            result.Failure = "StartFailed";
            return result;
        }
        finally
        {
            if (process != null)
            {
                if (result.Failure != "None") { Stop(process, job); }
                clock.Stop();
                result.WallMs = clock.Elapsed.TotalMilliseconds;
                try { result.CpuMs = process.TotalProcessorTime.TotalMilliseconds; }
                catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception || error is NotSupportedException) { }
                process.Dispose();
            }

            job?.Dispose();
        }
    }

    // Membership passes to every process the child starts, so terminating the job also stops
    // descendants whose parent has already exited, which a process-tree kill cannot reach.
    private static SafeFileHandle Contain(Process process)
    {
        var job = CreateJobObjectW(IntPtr.Zero, null);
        if (job.IsInvalid) { job.Dispose(); return null; }
        if (AssignProcessToJobObject(job, process.Handle)) { return job; }
        job.Dispose();
        return null;
    }

    private static void Stop(Process process, SafeFileHandle job)
    {
        if (job != null && !job.IsClosed) { TerminateJobObject(job, 1); }
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                process.WaitForExit(5000);
            }
        }
        catch (Exception error) when (error is InvalidOperationException || error is System.ComponentModel.Win32Exception ||
            error is AggregateException || error is NotSupportedException)
        {
        }
    }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern SafeFileHandle CreateJobObjectW(IntPtr attributes, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(SafeFileHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateJobObject(SafeFileHandle job, uint exitCode);
}
