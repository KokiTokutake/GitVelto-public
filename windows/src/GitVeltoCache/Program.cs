using System.IO.Pipes;
using System.Text;
using GitVelto.Core;

namespace GitVelto.Cache;

/// <summary>
/// GitVeltoCache.exe — overlay status cache (TGitCache equivalent).
///
/// The shell extension asks over the named pipe \\.\pipe\GitVeltoCache-&lt;user&gt;, one request per connection:
///   "dir\t&lt;folder&gt;\n"      → "repo\t&lt;root or empty&gt;\tready|pending\n" then "&lt;child name&gt;\t&lt;badge&gt;\n" per child with a badge
///   "invalidate\t&lt;root&gt;\n" → recompute a repository (sent by GitVelto.exe after it changed something)
///   "settings\n"             → reload settings from the registry
///   "ping\n"                 → "pong\n"
/// The cache watches repositories with FileSystemWatcher and refreshes Explorer with SHChangeNotify.
/// </summary>
public static class Program
{
    public static string PipeName => $"{KG.CachePipe}-{Environment.UserName}";

    [STAThread]
    public static int Main(string[] args)
    {
        using var mutex = new Mutex(true, $@"Local\GitVeltoCache-{Environment.UserName}", out bool first);
        if (!first) return 0;

        var log = args.Contains("--log");
        var service = new StatusService(log);
        var cts = new CancellationTokenSource();
        // A few listeners so that concurrent requests from Explorer threads do not queue up.
        var listeners = Enumerable.Range(0, 4).Select(_ => Task.Run(() => Listen(service, cts.Token))).ToArray();
        Task.WaitAll(listeners);
        return 0;
    }

    private static async Task Listen(StatusService service, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(PipeName, PipeDirection.InOut, NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(token);
                var line = await ReadLine(pipe, token);
                if (line == null) continue;
                var response = service.Handle(line);
                if (response.Length > 0)
                {
                    var bytes = Encoding.UTF8.GetBytes(response);
                    await pipe.WriteAsync(bytes, token);
                    await pipe.FlushAsync(token);
                }
                try { pipe.WaitForPipeDrain(); } catch { }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e)
            {
                service.Log("pipe error: " + e.Message);
                await Task.Delay(50, CancellationToken.None);
            }
        }
    }

    private static async Task<string?> ReadLine(Stream s, CancellationToken token)
    {
        var buf = new List<byte>(256);
        var one = new byte[512];
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(2000);
        while (true)
        {
            int n;
            try { n = await s.ReadAsync(one, timeout.Token); }
            catch (OperationCanceledException) { return null; }
            if (n == 0) break;
            for (int i = 0; i < n; i++)
            {
                if (one[i] == (byte)'\n') return Encoding.UTF8.GetString(buf.ToArray()).TrimEnd('\r');
                buf.Add(one[i]);
            }
            if (buf.Count > 64 * 1024) return null;
        }
        return buf.Count > 0 ? Encoding.UTF8.GetString(buf.ToArray()) : null;
    }
}
