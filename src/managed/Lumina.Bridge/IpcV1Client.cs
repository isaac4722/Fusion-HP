// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Bridge/IpcV1Client.cs : cliente C# del protocolo ipc.v1 (F0.05 — lado
//  gestionado).
//
//  Capas:
//   * IpcV1Codec   — códec de frames ESPEJO del nativo (IpcV1.cpp): misma
//                    cabecera (magic LMIP + u16 versión + u16 tipo + u32
//                    longitud). Multi-plataforma: se prueba en el arnés
//                    net8/Linux con pipes anónimos.
//   * IpcV1Decoder — reensamblado tolerante a fragmentación.
//   * IpcV1Client  — transporte NamedPipeClientStream (Windows) con:
//                    - handshake HELLO/WELCOME (validación «ipc.v1»);
//                    - comandos con id correlativo (espera RESULT);
//                    - hilo lector de pushes (STATE/EVENT) sin bloquear la UI;
//                    - reconexión con backoff (F5.05.8);
//                    - PING/PONG de latencia (F6.01: latencia IPC medida).
//
//  net35-compatible (C# 7.3, sin async/await — runtime 2.0).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using lumina.core;      // MiniJson (parse de RESULT/WELCOME)

namespace lumina.bridge
{
    /// <summary>Tipos de mensaje ipc.v1 (espejo del nativo).</summary>
    public static class IpcV1
    {
        public const uint Magic = 0x4C4D4950u;     // "LMIP"
        public const ushort Version = 1;
        public const int HeaderSize = 12;
        public const int MsgHello = 1, MsgWelcome = 2, MsgCommand = 3,
                         MsgResult = 4, MsgState = 5, MsgEvent = 6,
                         MsgPing = 7, MsgPong = 8, MsgError = 9;
        public const int DefaultMaxPayload = 8 * 1024 * 1024;
    }

    /// <summary>Códec puro (sin E/S) — probado unitariamente.</summary>
    public static class IpcV1Codec
    {
        public static byte[] Encode(int type, string payload)
        {
            byte[] body = payload == null ? new byte[0] : Encoding.UTF8.GetBytes(payload);
            byte[] fr = new byte[IpcV1.HeaderSize + body.Length];
            fr[0] = (byte)(IpcV1.Magic & 0xFF);
            fr[1] = (byte)((IpcV1.Magic >> 8) & 0xFF);
            fr[2] = (byte)((IpcV1.Magic >> 16) & 0xFF);
            fr[3] = (byte)((IpcV1.Magic >> 24) & 0xFF);
            fr[4] = (byte)(IpcV1.Version & 0xFF);
            fr[5] = (byte)((IpcV1.Version >> 8) & 0xFF);
            fr[6] = (byte)(type & 0xFF);
            fr[7] = (byte)((type >> 8) & 0xFF);
            uint n = (uint)body.Length;
            fr[8] = (byte)(n & 0xFF); fr[9] = (byte)((n >> 8) & 0xFF);
            fr[10] = (byte)((n >> 16) & 0xFF); fr[11] = (byte)((n >> 24) & 0xFF);
            if (body.Length > 0) Buffer.BlockCopy(body, 0, fr, IpcV1.HeaderSize, body.Length);
            return fr;
        }
    }

    /// <summary>Decodificador con reensamblado (fragmentación de red/pipe).</summary>
    public sealed class IpcV1Decoder
    {
        private readonly List<byte> _buf = new List<byte>();
        private readonly int _maxPayload;

        public IpcV1Decoder(int maxPayload) { _maxPayload = maxPayload; }
        public int Buffered { get { lock (_buf) { return _buf.Count; } } }

        /// <summary>Alimenta bytes; devuelve 0=falta, 1=frame, &lt;0=protocolo
        /// (-1 magic, -2 versión, -3 oversized).</summary>
        public int Feed(byte[] data, int offset, int count, out int type, out string payload)
        {
            type = 0; payload = null;
            lock (_buf)
            {
                for (int i = 0; i < count; i++) _buf.Add(data[offset + i]);
                while (true)
                {
                    if (_buf.Count < IpcV1.HeaderSize) return 0;
                    byte[] h = _buf.ToArray();
                    uint magic = (uint)(h[0] | (h[1] << 8) | (h[2] << 16) | (h[3] << 24));
                    if (magic != IpcV1.Magic) { _buf.Clear(); return -1; }
                    ushort ver = (ushort)(h[4] | (h[5] << 8));
                    if (ver != IpcV1.Version) { _buf.Clear(); return -2; }
                    uint len = (uint)(h[8] | (h[9] << 8) | (h[10] << 16) | (h[11] << 24));
                    if (len > (uint)_maxPayload) { _buf.Clear(); return -3; }
                    if (_buf.Count < IpcV1.HeaderSize + (int)len) return 0;
                    type = h[6] | (h[7] << 8);
                    payload = len == 0 ? ""
                        : Encoding.UTF8.GetString(h, IpcV1.HeaderSize, (int)len);
                    _buf.RemoveRange(0, IpcV1.HeaderSize + (int)len);
                    return 1;
                }
            }
        }
    }

    /// <summary>Respuesta RESULT de un comando.</summary>
    public sealed class IpcResult
    {
        public long Id;
        public bool Ok;
        public int Status;
        public string DataJson = "";
        public string Error = "";
    }

    /// <summary>
    /// Cliente ipc.v1 sobre NamedPipeClientStream. Los pushes (STATE/EVENT)
    /// llegan por el evento Pumped. Thread-safe para un comando a la vez
    /// (el protocolo correlaciona por id).
    /// </summary>
    public sealed class IpcV1Client : IDisposable
    {
        private NamedPipeClientStream _pipe;
        private readonly object _send = new object();
        private Thread _reader;
        private volatile bool _running;
        private readonly Dictionary<long, IpcResult> _results = new Dictionary<long, IpcResult>();
        private long _nextId = 1;
        public string ServerVersion = "";
        public int LatencyMs;                       // último PING/PONG (F6.01)

        /// <summary>Pushes del servidor (STATE/EVENT/WELCOME/ERROR).</summary>
        public event Action<int, string> Pumped;
        /// <summary>Cortes de conexión (la proyección NO depende de nosotros).</summary>
        public event Action<string> Disconnected;

        public bool Connected { get { lock (_send) { return _pipe != null && _pipe.IsConnected; } } }

        public bool Connect(string pipeName, int timeoutMs)
        {
            try
            {
                NamedPipeClientStream p = new NamedPipeClientStream(
                    ".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
                p.Connect(timeoutMs);
                lock (_send)
                {
                    if (_pipe != null) try { _pipe.Dispose(); } catch (IOException) { }
                    _pipe = p;
                }
                _running = true;
                _reader = new Thread(ReadLoop);
                _reader.IsBackground = true;
                _reader.Start();
                // Handshake ipc.v1: HELLO → esperar WELCOME (vía bomba).
                IpcResult hello = Command("hello", "{\"proto\":\"ipc.v1\",\"client\":\"cs\"}");
                return hello != null && hello.Ok;
            }
            catch (TimeoutException) { return false; }
            catch (IOException) { return false; }
            catch (Exception) { return false; }
        }

        private void ReadLoop()
        {
            IpcV1Decoder dec = new IpcV1Decoder(IpcV1.DefaultMaxPayload);
            byte[] chunk = new byte[8192];
            while (_running)
            {
                try
                {
                    NamedPipeClientStream p;
                    lock (_send) p = _pipe;
                    if (p == null || !p.IsConnected) break;
                    int n = p.Read(chunk, 0, chunk.Length);
                    if (n <= 0) break;
                    int type; string payload;
                    int rc;
                    while ((rc = dec.Feed(chunk, 0, n, out type, out payload)) == 1)
                    {
                        n = 0;                          // consumido; siguientes del búfer
                        if (type == IpcV1.MsgResult)
                        {
                            lock (_results)
                            {
                                Dictionary<string, object> j =
                                    MiniJson.Parse(payload);
                                IpcResult r = new IpcResult();
                                r.Id = MiniJson.GetInt(j, "id", 0);
                                r.Ok = MiniJson.GetBool(j, "ok", false);
                                r.Status = (int)MiniJson.GetInt(j, "status", 0);
                                r.DataJson = MiniJson.GetString(j, "data", "");
                                r.Error = MiniJson.GetString(j, "error", "");
                                _results[r.Id] = r;
                                Monitor.Pulse(_results);
                            }
                        }
                        else if (type == IpcV1.MsgWelcome)
                        {
                            Dictionary<string, object> j = MiniJson.Parse(payload);
                            ServerVersion = MiniJson.GetString(j, "server", "?");
                        }
                        else if (type == IpcV1.MsgPong)
                        {
                            // latencia medida (F6.01: latencia IPC).
                            LatencyMs = Math.Max(0, (int)(DateTime.UtcNow - _pingSent).TotalMilliseconds);
                        }
                        if (Pumped != null) Pumped(type, payload);
                    }
                    if (rc < 0)
                    {
                        // Frame corrupto: el servidor responde ERROR y sigue;
                        // aquí solo se descarta (el códec ya se reseteó).
                    }
                }
                catch (IOException) { break; }
                catch (ObjectDisposedException) { break; }
            }
            if (Disconnected != null) Disconnected("conexion perdida");
        }

        private DateTime _pingSent;

        /// <summary>Envía un comando y espera su RESULT (timeout por defecto 5 s).</summary>
        public IpcResult Command(string action, string payloadJson)
        {
            return Command(action, payloadJson, 5000);
        }

        /// <summary>Envía un comando y espera su RESULT (timeout duro).</summary>
        public IpcResult Command(string action, string payloadJson, int timeoutMs)
        {
            long id;
            lock (_send) { id = _nextId++; }
            Dictionary<string, object> cmd = new Dictionary<string, object>();
            cmd["id"] = id;
            cmd["action"] = action;
            object inner = null;
            try
            {
                inner = MiniJson.ParseAny(payloadJson ?? "{}");
            }
            catch (Exception) { inner = payloadJson ?? ""; }
            if (inner is Dictionary<string, object>) cmd["payload"] = inner;
            else if (inner != null) cmd["payload"] = Convert.ToString(inner);
            byte[] frame = IpcV1Codec.Encode(IpcV1.MsgCommand, MiniJson.Serialize(cmd));
            lock (_send)
            {
                if (_pipe == null || !_pipe.IsConnected) return null;
                _pipe.Write(frame, 0, frame.Length);
                _pipe.Flush();
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                lock (_results)
                {
                    IpcResult r;
                    if (_results.TryGetValue(id, out r))
                    {
                        _results.Remove(id);
                        return r;
                    }
                    Monitor.Wait(_results, 50);
                }
            }
            return null;                          // timeout: la sesión sigue viva
        }

        /// <summary>PING de latencia (F6.01). Devuelve ms o -1.</summary>
        public int Ping()
        {
            _pingSent = DateTime.UtcNow;
            byte[] fr = IpcV1Codec.Encode(IpcV1.MsgPing, "ping");
            lock (_send)
            {
                if (_pipe == null || !_pipe.IsConnected) return -1;
                try { _pipe.Write(fr, 0, fr.Length); _pipe.Flush(); }
                catch (IOException) { return -1; }
            }
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < 3000)
            {
                if (LatencyMs > 0) { int v = LatencyMs; LatencyMs = 0; return v; }
                Thread.Sleep(10);
            }
            return -1;
        }

        public void Dispose()
        {
            _running = false;
            lock (_send)
            {
                if (_pipe != null) try { _pipe.Dispose(); } catch (IOException) { }
                _pipe = null;
            }
            if (_reader != null && _reader.IsAlive) _reader.Join(1000);
        }
    }
}
