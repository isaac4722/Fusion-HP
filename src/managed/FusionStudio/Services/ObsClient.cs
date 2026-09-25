// ============================================================================
//  Fusion-HP · FusionStudio/Services/ObsClient.cs — cliente obs-websocket 5.x
//  [SPEC §8.4]: conexión saliente, autenticación SHA-256 con reto, envío del
//  texto activo a una fuente de texto y cambio de escena; reconexión
//  automática con registro de fallos [SPEC §8.4.1].
//  Implementación WebSocket RFC6455 con ClientSocket (sin dependencias).
// ============================================================================
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using Fusion.Shared;

namespace Fusion.Studio.Services
{
    /// <summary>Cliente WebSocket mínimo (RFC 6455, texto plano, cliente).</summary>
    public class SimpleWebSocket : IDisposable
    {
        TcpClient tcp;
        Stream stream;
        readonly string host;
        readonly int port;
        volatile bool open;

        public SimpleWebSocket(string host, int port)
        {
            this.host = host;
            this.port = port;
        }

        public bool Open { get { return open; } }

        public bool Connect(int timeoutMs)
        {
            try
            {
                tcp = new TcpClient();
                var ar = tcp.BeginConnect(host, port, null, null);
                if (!ar.AsyncWaitHandle.WaitOne(timeoutMs, false)) { tcp.Close(); return false; }
                tcp.EndConnect(ar);
                stream = (Stream)tcp.GetStream();

                // handshake
                string key = Convert.ToBase64String(Encoding.ASCII.GetBytes(Guid.NewGuid().ToString("N").Substring(0, 16)));
                string req = "GET / HTTP/1.1\r\nHost: " + host + ":" + port + "\r\n" +
                             "Upgrade: websocket\r\nConnection: Upgrade\r\n" +
                             "Sec-WebSocket-Key: " + key + "\r\nSec-WebSocket-Version: 13\r\n\r\n";
                byte[] b = Encoding.ASCII.GetBytes(req);
                stream.Write(b, 0, b.Length);
                var resp = ReadHttpHeader();
                if (resp == null || resp.IndexOf("101", StringComparison.Ordinal) < 0) return false;
                open = true;
                return true;
            }
            catch
            {
                return false;
            }
        }

        string ReadHttpHeader()
        {
            var sb = new StringBuilder();
            var buf = new byte[1];
            while (sb.Length < 8192)
            {
                int n = stream.Read(buf, 0, 1);
                if (n <= 0) break;
                sb.Append((char)buf[0]);
                if (sb.ToString().EndsWith("\r\n\r\n")) break;
            }
            return sb.ToString();
        }

        public void SendText(string text)
        {
            if (!open) return;
            byte[] payload = Encoding.UTF8.GetBytes(text);
            var frame = new List<byte>();
            frame.Add(0x81);   // FIN + text
            if (payload.Length < 126) frame.Add((byte)(0x80 | payload.Length));
            else if (payload.Length < 65536)
            {
                frame.Add((byte)(0x80 | 126));
                frame.Add((byte)(payload.Length >> 8));
                frame.Add((byte)(payload.Length & 0xFF));
            }
            else
            {
                frame.Add((byte)(0x80 | 127));
                for (int i = 7; i >= 0; i--) frame.Add((byte)((long)payload.Length >> (8 * i) & 0xFF));
            }
            // máscara de cliente (4 bytes) — requerida
            var mask = new byte[4];
            new Random().NextBytes(mask);
            frame.AddRange(mask);
            for (int i = 0; i < payload.Length; i++) frame.Add((byte)(payload[i] ^ mask[i % 4]));
            try
            {
                byte[] data = frame.ToArray();
                stream.Write(data, 0, data.Length);
            }
            catch
            {
                open = false;
            }
        }

        /// <summary>Lee un mensaje de texto (bloqueante) o null si cerró/timeout.</summary>
        public string Receive(int timeoutMs)
        {
            if (!open) return null;
            try
            {
                var hdr = ReadExact(2);
                if (hdr == null) { open = false; return null; }
                byte op = (byte)(hdr[0] & 0x0F);
                bool masked = (hdr[1] & 0x80) != 0;
                long len = hdr[1] & 0x7F;
                if (len == 126)
                {
                    var ext = ReadExact(2);
                    if (ext == null) { open = false; return null; }
                    len = (ext[0] << 8) | ext[1];
                }
                else if (len == 127)
                {
                    var ext = ReadExact(8);
                    if (ext == null) { open = false; return null; }
                    len = 0;
                    for (int i = 0; i < 8; i++) len = (len << 8) | ext[i];
                }
                if (len > 8 * 1024 * 1024) { open = false; return null; }
                byte[] mask = masked ? ReadExact(4) : null;
                byte[] payload = ReadExact((int)len);
                if (payload == null) { open = false; return null; }
                if (masked) for (int i = 0; i < payload.Length; i++) payload[i] ^= mask[i % 4];
                if (op == 0x8) { open = false; return null; }          // close
                if (op == 0x9) { /* ping → responder pong */ SendPong(payload); return ""; }
                return Encoding.UTF8.GetString(payload);
            }
            catch
            {
                open = false;
                return null;
            }
        }

        void SendPong(byte[] payload)
        {
            try
            {
                var frame = new List<byte>();
                frame.Add(0x8A);
                frame.Add((byte)(payload.Length));
                frame.AddRange(payload);
                var d = frame.ToArray();
                stream.Write(d, 0, d.Length);
            }
            catch { }
        }

        byte[] ReadExact(int n)
        {
            var buf = new byte[n];
            int got = 0;
            while (got < n)
            {
                int r = stream.Read(buf, got, n - got);
                if (r <= 0) return null;
                got += r;
            }
            return buf;
        }

        public void Dispose()
        {
            open = false;
            try { if (stream != null) stream.Dispose(); } catch { }
            try { if (tcp != null) tcp.Close(); } catch { }
        }
    }

    /// <summary>Cliente obs-websocket 5.x con autenticación por reto.</summary>
    public class ObsClient
    {
        readonly AppSettings settings;
        SimpleWebSocket ws;
        Thread reader;
        volatile bool running;
        int reqId = 1;
        public event Action<string> Logged;
        public event Action<string, JsonValue> EventReceived;

        public bool Connected { get { return ws != null && ws.Open; } }

        public ObsClient(AppSettings settings)
        {
            this.settings = settings;
        }

        public void Start()
        {
            if (running) return;
            running = true;
            var t = new Thread(Loop);
            t.IsBackground = true;
            t.Start();
        }

        public void Stop()
        {
            running = false;
            if (ws != null) ws.Dispose();
        }

        void Loop()
        {
            int fails = 0;
            while (running)
            {
                ws = new SimpleWebSocket(settings.ObsHost, settings.ObsPort);
                if (!ws.Connect(3000))
                {
                    fails++;
                    Log("OBS: no se pudo conectar a " + settings.ObsHost + ":" + settings.ObsPort +
                        (fails < 3 ? " (reintentando)" : ""));
                    ws.Dispose();
                    Thread.Sleep(Math.Min(30000, 3000 * fails));   // backoff [SPEC §11.2.2]
                    continue;
                }
                fails = 0;
                Log("OBS: conectado");
                try { Handshake(); }
                catch (Exception ex) { Log("OBS: autenticación falló (" + ex.Message + ")"); }
                while (running && ws.Open)
                {
                    string msg = ws.Receive(1000);
                    if (msg == null) break;
                    if (msg.Length == 0) continue;
                    try
                    {
                        var j = JsonValue.Parse(msg);
                        int op = j.GetInt("op", -1);
                        if (op == 5)
                        {
                            var d = j.Get("d");
                            var h = EventReceived;
                            if (h != null) h(d.GetStr("eventType", ""), d.Get("eventData"));
                        }
                    }
                    catch { }
                }
                if (running)
                {
                    Log("OBS: conexión perdida; reconectando…");
                    Thread.Sleep(3000);
                }
            }
        }

        void Handshake()
        {
            // op 0 Hello {rpcVersion, authentication?: {salt, challenge}}
            string hello = ws.Receive(5000);
            if (hello == null || hello.Length == 0) throw new Exception("sin Hello");
            var h = JsonValue.Parse(hello);
            if (h.GetInt("op", -1) != 0) throw new Exception("primer mensaje no es Hello");
            var identify = JsonValue.Object();
            identify.Set("rpcVersion", JsonValue.Make(1));
            var auth = h.Get("d").Get("authentication");
            if (auth.Type == JsonValue.Kind.Object)
            {
                string salt = auth.GetStr("salt", "");
                string challenge = auth.GetStr("challenge", "");
                // sha256(base64(sha256(password+salt))+challenge) → base64
                string secret = Base64Sha256(settings.ObsPassword + salt);
                string proof = Base64Sha256(secret + challenge);
                identify.Set("authentication", JsonValue.Make(proof));
            }
            var msg = JsonValue.Object();
            msg.Set("op", JsonValue.Make(1));
            msg.Set("d", identify);
            ws.SendText(msg.ToJsonString());
            string resp = ws.Receive(5000);
            if (resp == null) throw new Exception("sin respuesta a Identify");
            var r = JsonValue.Parse(resp);
            if (r.GetInt("op", -1) == 2) Log("OBS: autenticado");
            else throw new Exception("Identify rechazado");
        }

        static string Base64Sha256(string s)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(s)));
        }

        void SendRequest(string type, JsonValue data)
        {
            if (!Connected) return;
            var msg = JsonValue.Object();
            msg.Set("op", JsonValue.Make(6));
            var d = JsonValue.Object();
            d.Set("requestType", JsonValue.Make(type));
            d.Set("requestId", JsonValue.Make("fhp-" + (reqId++)));
            if (data != null) d.Set("requestData", data);
            msg.Set("d", d);
            ws.SendText(msg.ToJsonString());
        }

        /// <summary>Actualiza la fuente de texto de OBS con la línea activa [SPEC §8.4.3].</summary>
        public void PushText(string text)
        {
            if (string.IsNullOrEmpty(settings.ObsTextSource) || !Connected) return;
            var input = JsonValue.Object();
            input.Set("inputName", JsonValue.Make(settings.ObsTextSource));
            var settings2 = JsonValue.Object();
            settings2.Set("text", JsonValue.Make(text));
            input.Set("inputSettings", settings2);
            SendRequest("SetInputSettings", input);
        }

        /// <summary>Cambia de escena [SPEC §8.3.2].</summary>
        public void SetScene(string scene)
        {
            if (string.IsNullOrEmpty(scene) || !Connected) return;
            var d = JsonValue.Object();
            d.Set("sceneName", JsonValue.Make(scene));
            SendRequest("SetCurrentProgramScene", d);
        }

        void Log(string m)
        {
            var h = Logged;
            if (h != null) h(m);
        }
    }
}
