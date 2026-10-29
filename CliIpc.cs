using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace FileTagger
{
    public static class CliIpc
    {
        public const string PipeName = "FileTagger_Ipc_v1";
        public const string ManageTagsCollectorPipeName = "FileTagger_ManageTags_Collector_v1";
        private const int DefaultConnectTimeoutMs = 1000;

        public static bool TrySend(string[] args) => TrySend(PipeName, args, DefaultConnectTimeoutMs);

        public static bool TrySend(string pipeName, string[] args, int connectTimeoutMs)
        {
            if (args == null || args.Length == 0)
                return false;

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.Out, PipeOptions.None);
                client.Connect(connectTimeoutMs);

                var payload = string.Join('\n', args);
                var bytes = Encoding.UTF8.GetBytes(payload);
                client.Write(bytes, 0, bytes.Length);
                client.Flush();
                return true;
            }
            catch (TimeoutException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
        }

        public static void StartServer(Action<string[]> onCommand) => StartServer(PipeName, onCommand);

        public static void StartServer(string pipeName, Action<string[]> onCommand)
        {
            var thread = new Thread(() => ServerLoop(pipeName, onCommand))
            {
                IsBackground = true,
                Name = $"CliIpc:{pipeName}"
            };
            thread.Start();
        }

        private static void ServerLoop(string pipeName, Action<string[]> onCommand)
        {
            while (true)
            {
                NamedPipeServerStream server = null;
                try
                {
                    server = new NamedPipeServerStream(
                        pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    server.WaitForConnection();

                    // Read on a worker so the next pipe instance is listening immediately;
                    // Explorer fires many clients at once for a multi-file selection.
                    var connected = server;
                    server = null;
                    Task.Run(() => ReadClient(connected, onCommand));
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch
                {
                    server?.Dispose();
                    Thread.Sleep(50);
                }
            }
        }

        private static void ReadClient(NamedPipeServerStream server, Action<string[]> onCommand)
        {
            try
            {
                using (server)
                using (var reader = new StreamReader(server, Encoding.UTF8, detectEncodingFromByteOrderMarks: false))
                {
                    var payload = reader.ReadToEnd();
                    if (string.IsNullOrWhiteSpace(payload))
                        return;

                    onCommand(payload.Split('\n'));
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"CliIpc read error: {ex.Message}");
            }
        }
    }
}
