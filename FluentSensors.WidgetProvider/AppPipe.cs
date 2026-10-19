using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

using FluentSensors.Features.WidgetBoard;


namespace FluentSensors.WidgetProvider
{
    // the pipe server the running app connects to; one app at a time
    // a write never waits on the app: a provider that hangs once is avoided by the board until its widget service
    // restarts, so every write is small, buffered and dropped on failure
    public static class AppPipe
    {
        // === fields ===

        private static readonly object _writeGate = new();
        private static NamedPipeServerStream? _pipe;


        // === public api ===

        public static void Start() => new Thread(Run) { IsBackground = true, Name = "app pipe" }.Start();

        public static void SendActiveSizes(byte[] sizes)
        {
            var pipe = _pipe;
            if (pipe == null) return;

            try
            {
                lock (_writeGate)
                {
                    var writer = new BinaryWriter(pipe);
                    writer.Write(WidgetBoardProtocol.ActiveSizes);
                    writer.Write((byte)sizes.Length);
                    writer.Write(sizes);
                    writer.Flush();
                }
            }
            catch (Exception ex)
            {
                Log.Write("sending the sizes failed " + ex.Message);
            }
        }


        // === private helpers ===

        private static void Run()
        {
            while (true)
            {
                try
                {
                    using var pipe = new NamedPipeServerStream(
                        WidgetBoardProtocol.PipeName,
                        PipeDirection.InOut,
                        1,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly,
                        WidgetBoardProtocol.PipeBufferSize,
                        WidgetBoardProtocol.PipeBufferSize);

                    pipe.WaitForConnection();
                    _pipe = pipe;
                    SendActiveSizes(BoardWidgetProvider.Instance.GetActiveSizes());

                    Read(new BinaryReader(pipe));
                }
                catch (EndOfStreamException)
                {
                    // the app closed
                }
                catch (Exception ex)
                {
                    Log.Write("pipe " + ex.Message);
                    Thread.Sleep(1000); // no tight loop on a pipe that cannot be created
                }
                finally
                {
                    _pipe = null;
                    BoardWidgetProvider.Instance.ClearCards();
                }
            }
        }

        private static void Read(BinaryReader reader)
        {
            while (true)
            {
                switch (reader.ReadByte())
                {
                    case WidgetBoardProtocol.Card:
                        byte size = reader.ReadByte();
                        string template = reader.ReadString();
                        string data = reader.ReadString();
                        BoardWidgetProvider.Instance.ApplyCard(size, template, data);
                        break;

                    case WidgetBoardProtocol.NotRunningText:
                        BoardWidgetProvider.Instance.ApplyNotRunningText(reader.ReadString());
                        break;

                    default:
                        throw new InvalidDataException("unknown message from the app");
                }
            }
        }
    }
}
