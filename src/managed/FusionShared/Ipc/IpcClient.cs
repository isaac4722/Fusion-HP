// ============================================================================
//  Fusion-HP · FusionShared/Ipc/IpcClient.cs — cliente del pipe ipc.v1
//  Habla el protocolo de longitud prefijada + JSON del núcleo C++ [SPEC §3.4].
//  Un SOLO hilo lector: despacha eventos {evt} y completa comandos {id}.
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;

namespace Fusion.Shared.Ipc
{
    public class IpcClient : IDisposable
    {
        public const string PipeName = "FusionHP.ipc.v1";
        NamedPipeClientStream _pipe;
        Thread _reader;
        volatile bool _running;
        readonly object _sendLock = new object();
        int _nextId = 1;
        readonly Dictionary<string, PendingResponse> _pending = new Dictionary<string, PendingResponse>();

        class PendingResponse
        {
            public ManualResetEvent Done = new ManualResetEvent(false);
            public JsonValue Value;
        }

        /// <summary>Se dispara (en hilo de lectura) con cada evento {evt, data}.</summary>
        public event Action<string, JsonValue> Event;
        /// <summary>Se dispara cuando se pierde la conexión con el núcleo.</summary>
        public event Action Disconnected;

        public bool Connected
        {
            get { return _pipe != null && _pipe.IsConnected; }
        }

        public bool Connect(int timeoutMs)
        {
            try
            {
                _pipe = new NamedPipeClientStream(".", PipeName, PipeDirection.InOut);
                _pipe.Connect(timeoutMs);
                _pipe.ReadMode = PipeTransmissionMode.Byte;
                _running = true;
                _reader = new Thread(ReadLoop);
                _reader.IsBackground = true;
                _reader.Start();
                return true;
            }
            catch
            {
                if (_pipe != null) { try { _pipe.Dispose(); } catch { } _pipe = null; }
                return false;
            }
        }

        void ReadLoop()
        {
            try
            {
                while (_running)
                {
                    var j = ReadMessage();
                    if (j == null) break;
                    if (j.Type != JsonValue.Kind.Object) continue;
                    if (j.Has("evt"))
                    {
                        string evt = j.GetStr("evt");
                        var h = Event;
                        if (h != null)
                        {
                            try { h(evt, j.Get("data")); } catch { }
                        }
                    }
                    else if (j.Has("id"))
                    {
                        string id = j.GetStr("id");
                        PendingResponse pr = null;
                        lock (_pending)
                        {
                            if (_pending.TryGetValue(id, out pr)) _pending.Remove(id);
                        }
                        if (pr != null)
                        {
                            pr.Value = j;
                            pr.Done.Set();
                        }
                    }
                }
            }
            catch
            {
                // desconexión: cae al finally
            }
            finally
            {
                var d = Disconnected;
                if (d != null)
                {
                    try { d(); } catch { }
                }
            }
        }

        JsonValue ReadMessage()
        {
            byte[] lenBuf = ReadExact(4);
            if (lenBuf == null) return null;
            int len = lenBuf[0] | (lenBuf[1] << 8) | (lenBuf[2] << 16) | (lenBuf[3] << 24);
            if (len <= 0 || len > 8 * 1024 * 1024) return null;
            byte[] body = ReadExact(len);
            if (body == null) return null;
            return JsonValue.Parse(Encoding.UTF8.GetString(body));
        }

        byte[] ReadExact(int n)
        {
            byte[] buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = _pipe.Read(buf, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return buf;
        }

        /// <summary>Envía un comando y espera su respuesta. Devuelve null si falla/tarda.</summary>
        public JsonValue Command(string cmd, JsonValue payload, int timeoutMs)
        {
            if (!Connected) return null;
            var msg = JsonValue.Object();
            string id = "c" + Interlocked.Increment(ref _nextId);
            msg.Set("v", JsonValue.Make(1));
            msg.Set("id", JsonValue.Make(id));
            msg.Set("cmd", JsonValue.Make(cmd));
            msg.Set("payload", payload ?? JsonValue.Object());

            var pr = new PendingResponse();
            lock (_pending) _pending[id] = pr;

            byte[] body = Encoding.UTF8.GetBytes(msg.ToJsonString());
            byte[] len = BitConverter.GetBytes(body.Length);
            try
            {
                lock (_sendLock)
                {
                    _pipe.Write(len, 0, 4);
                    _pipe.Write(body, 0, body.Length);
                    _pipe.Flush();
                }
            }
            catch
            {
                lock (_pending) _pending.Remove(id);
                return null;
            }

            if (!pr.Done.WaitOne(timeoutMs, false))
            {
                lock (_pending) _pending.Remove(id);
                return null;
            }
            return pr.Value;
        }

        /// <summary>Envía un comando sin esperar respuesta (fire & forget).</summary>
        public void Post(string cmd, JsonValue payload)
        {
            if (!Connected) return;
            var msg = JsonValue.Object();
            msg.Set("v", JsonValue.Make(1));
            msg.Set("id", JsonValue.Make("p" + Interlocked.Increment(ref _nextId)));
            msg.Set("cmd", JsonValue.Make(cmd));
            msg.Set("payload", payload ?? JsonValue.Object());
            byte[] body = Encoding.UTF8.GetBytes(msg.ToJsonString());
            byte[] len = BitConverter.GetBytes(body.Length);
            try
            {
                lock (_sendLock)
                {
                    _pipe.Write(len, 0, 4);
                    _pipe.Write(body, 0, body.Length);
                    _pipe.Flush();
                }
            }
            catch { }
        }

        public void Dispose()
        {
            _running = false;
            try { if (_pipe != null) _pipe.Dispose(); } catch { }
            _pipe = null;
        }
    }
}
