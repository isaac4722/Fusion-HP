// ============================================================================
//  Fusion-HP · FusionStudio/Services/ObsClient.cs — cliente obs-websocket 5.x
//  restaurado [v3.0.0, SPEC §8.4.1]. Antes de la v2.3 existía un cliente que
//  fue eliminado; el operador necesita que la API «funcione para OBS»: este
//  cliente cambia escenas de OBS Studio desde Triggers (obs.scene) y desde la
//  consola, con autenticación SHA-256 (RFC obs-websocket), reconexión
//  automática con retroceso y registro de fallos [SPEC §8.4.1].
//  Solo net48 (ClientWebSocket); la variante Lite (net35) lo compila fuera.
// ============================================================================
#if !LITE
using System;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Fusion.Studio.Services
{
    public class ObsClient
    {
        ClientWebSocket ws;
        CancellationTokenSource cts;
        string host = "127.0.0.1";
        int port = 4455;
        string password = "";
        int attempts;                       // retroceso de reconexión
        volatile bool running;

        public event Action<string> Logged;
        public event Action<bool> ConnectedChanged;

        public bool Connected
        {
            get { return ws != null && ws.State == WebSocketState.Open; }
        }

        public void Configure(string h, int p, string pass)
        {
            host = string.IsNullOrEmpty(h) ? "127.0.0.1" : h;
            port = p > 0 ? p : 4455;
            password = pass ?? "";
        }

        public void Start()
        {
            if (running) return;
            running = true;
            cts = new CancellationTokenSource();
            Task.Run((Action)Loop);
        }

        public void Stop()
        {
            running = false;
            try { if (cts != null) cts.Cancel(); } catch { }
            try { if (ws != null) ws.Dispose(); } catch { }
            ws = null;
            FireConnected(false);
        }

        async void Loop()
        {
            while (running)
            {
                try
                {
                    using (var socket = new ClientWebSocket())
                    {
                        ws = socket;
                        var uri = new Uri("ws://" + host + ":" + port + "/");
                        using (var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4)))
                        using (var linked = CancellationTokenSource.CreateLinkedTokenSource(cts.Token, timeout.Token))
                            await socket.ConnectAsync(uri, linked.Token).ConfigureAwait(false);
                        Log("conectado a obs-websocket " + host + ":" + port);
                        FireConnected(true);
                        attempts = 0;
                        await Session(socket).ConfigureAwait(false);
                    }
                }
                catch (Exception ex)
                {
                    Log("sin conexión con OBS (" + Short(ex.Message) + "); reintentando");
                }
                FireConnected(false);
                try { if (ws != null) ws.Dispose(); } catch { }
                ws = null;
                if (!running) break;
                // retroceso: 1s, 2s, 4s… máx 15 s [SPEC §8.4.1 reconexión]
                attempts = Math.Min(attempts + 1, 4);
                int delay = (int)Math.Pow(2, attempts) * 500;
                try { await Task.Delay(delay, cts.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }

        async Task Session(ClientWebSocket socket)
        {
            var buf = new byte[64 * 1024];
            // 1) Hello (op:0) → Identify (op:1) → Identified (op:2)
            string hello = await Receive(socket, buf).ConfigureAwait(false);
            var h = Fusion.Shared.JsonValue.Parse(hello ?? "{}");
            int op = h.GetInt("op", -1);
            if (op != 0) { Log("saludo de OBS no reconocido"); return; }
            var identify = Fusion.Shared.JsonValue.Object();
            identify.Set("op", Fusion.Shared.JsonValue.Make(1));
            var d = Fusion.Shared.JsonValue.Object();
            d.Set("rpcVersion", Fusion.Shared.JsonValue.Make(1));
            d.Set("eventSubscriptions", Fusion.Shared.JsonValue.Make(0));
            var auth = h.Get("d").Get("authentication");
            if (auth != null && auth.Type == Fusion.Shared.JsonValue.Kind.Object)
            {
                string salt = auth.GetStr("salt", "");
                string challenge = auth.GetStr("challenge", "");
                d.Set("authentication", Fusion.Shared.JsonValue.Make(AuthHash(password, salt, challenge)));
            }
            identify.Set("d", d);
            Send(socket, identify.ToJsonString());

            string identified = await Receive(socket, buf).ConfigureAwait(false);
            var ir = Fusion.Shared.JsonValue.Parse(identified ?? "{}");
            if (ir.GetInt("op", -1) != 2) { Log("OBS rechazó la autenticación (revisa la contraseña)"); return; }
            Log("autenticado en OBS (rpc 5.x)");

            // 2) bucle: respuestas (op:7) y eventos (op:5)
            while (running && socket.State == WebSocketState.Open)
            {
                string msg = await Receive(socket, buf).ConfigureAwait(false);
                if (msg == null) break;
            }
        }

        /// <summary>Cambia la escena actual de OBS (SetCurrentProgramScene).</summary>
        public void SetScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName)) return;
            if (!Connected) { Log("SetScene ignorado: OBS no conectado"); return; }
            var req = Fusion.Shared.JsonValue.Object();
            req.Set("op", Fusion.Shared.JsonValue.Make(6));
            var d = Fusion.Shared.JsonValue.Object();
            d.Set("requestType", Fusion.Shared.JsonValue.Make("SetCurrentProgramScene"));
            d.Set("requestId", Fusion.Shared.JsonValue.Make("ahp-" + Guid.NewGuid().ToString("N").Substring(0, 8)));
            var rd = Fusion.Shared.JsonValue.Object();
            rd.Set("sceneName", Fusion.Shared.JsonValue.Make(sceneName));
            d.Set("requestData", rd);
            req.Set("d", d);
            Send(ws, req.ToJsonString());
        }

        // ------------------------------------------------------------ protocolo
        static async Task<string> Receive(ClientWebSocket s, byte[] buf)
        {
            var ms = new System.IO.MemoryStream();
            while (true)
            {
                var seg = new ArraySegment<byte>(buf);
                var res = await s.ReceiveAsync(seg, CancellationToken.None).ConfigureAwait(false);
                if (res.MessageType == WebSocketMessageType.Close)
                    return null;
                ms.Write(buf, 0, res.Count);
                if (res.EndOfMessage) break;
            }
            return Encoding.UTF8.GetString(ms.ToArray());
        }

        // Envío serializado (baja frecuencia: escenas desde Triggers). El envío
        // bloqueante acotado evita solapamiento de frames sin necesitar colas.
        static void Send(ClientWebSocket s, string text)
        {
            if (s == null || s.State != WebSocketState.Open) return;
            var bytes = Encoding.UTF8.GetBytes(text);
            s.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None).Wait(2000);
        }

        /// <summary>Hash de autenticación obs-websocket 5.x:
        /// base64(sha256( base64(sha256(password+salt)) + challenge )).
        /// Público para pruebas unitarias (contrato del protocolo).</summary>
        public static string AuthHash(string password, string salt, string challenge)
        {
            string secret = Base64Sha256((password ?? "") + (salt ?? ""));
            return Base64Sha256(secret + (challenge ?? ""));
        }

        static string Base64Sha256(string text)
        {
            using (var sha = SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(text)));
        }

        void FireConnected(bool on)
        {
            var h = ConnectedChanged;
            if (h != null) try { h(on); } catch { }
        }

        void Log(string m)
        {
            var h = Logged;
            if (h != null) try { h(m); } catch { }
        }

        static string Short(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length > 80 ? s.Substring(0, 80) : s;
        }
    }
}
#endif
