// ============================================================================
//  LuminaPresentation / LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Integrations/GoogleDriveClient.cs : sincronización bidireccional con
//  Google Drive (F6.04 — Sección 8.7).
//
//  Reglas del plan:
//   1. Copias de canciones. 2. Biblias. 3. Configuraciones. 4. Proyectos.
//   5. Modo OFFLINE PRIMERO (la app funciona SIEMPRE; la nube es respaldo).
//   6. Resolución de conflictos por VERSIÓN ahp.v1 (campo version/modified).
//   7. Sin pérdida silenciosa. 8. Historial de conflictos.
//
//  OAuth 2.0: flujo de dispositivo NO (requiere consola); aquí el flujo
//  INSTALADO de aplicación nativa con loopback 127.0.0.1:puerto-aleatorio
//  (el navegador abre http://localhost:<p> con el código — sin copiar a mano).
//  Endpoint base inyectable → el arnés prueba contra un servidor simulado.
//  net35: HttpWebRequest síncrono; CERO dependencias nuevas.
//
//  F6.04 (v1.0.0-beta.1) — ciclo completo:
//   * TODO el HTTP real queda tras IDriveTransport (inyectable; el arnés
//     usa un transporte simulado — sin credenciales en el repo).
//   * Copias de canciones, biblias, configuraciones y proyectos
//     (nomenclatura "<categoría>/<archivo>" dentro de appDataFolder).
//   * Modo OFFLINE PRIMERO: DriveSyncEngine encola operaciones cuando no
//     hay red (cola persistente) y las descarga al reconectar.
//   * Conflictos por VERSIÓN ahp.v1 (semilla nextId del proyecto): local /
//     nube / copia-conservada — la versión perdedora SIEMPRE queda como
//     copia (JAMÁS pérdida silenciosa) y todo queda en el historial
//     persistente de conflictos (WhenUtc/Resource/Resolution).
// ============================================================================
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using lumina.core;

// net8 marca HttpWebRequest como obsoleta (SYSLIB0014) pero es LA vía
// portable para net35/net48/net8 con una sola base de código (regla del
// repo: sin dependencias nuevas; HttpClient no existe en net35).
#pragma warning disable SYSLIB0014
namespace lumina.core.integrations
{
    /// <summary>Entrada del historial de conflictos (F6.04.8).</summary>
    public sealed class DriveConflict
    {
        public DateTime WhenUtc;
        public string Resource = "";      // "proyectos/culto.ahp"
        public string Resolution = "";    // "local" | "nube" | "copia-conservada"
        public string LocalVersion = "";
        public string CloudVersion = "";
    }

    /// <summary>Estado visible de la sincronización (F5.10: «Drive»).</summary>
    public sealed class DriveStatus
    {
        public bool Connected;
        public string Account = "";
        public DateTime LastSyncUtc;
        public int Uploaded, Downloaded, Conflicts;
        public string LastError = "";
    }

    // ------------------------------------------ transporte inyectable (F6.04) --

    /// <summary>Petición HTTP neutra (para el transporte inyectable).</summary>
    public sealed class DriveHttpRequest
    {
        public string Method = "GET";
        public string Url = "";
        public string ContentType = "";
        public byte[] Body;                        // null = sin cuerpo
        public readonly Dictionary<string, string> Headers =
            new Dictionary<string, string>(StringComparer.Ordinal);
        public int TimeoutMs = 20000;
    }

    /// <summary>Respuesta HTTP neutra (status + cuerpo).</summary>
    public sealed class DriveHttpResponse
    {
        public int StatusCode;
        public byte[] Body;
    }

    /// <summary>
    /// F6.04: TODO el tráfico de red pasa por esta interfaz — el programa usa
    /// HttpDriveTransport (HttpWebRequest, portable net35/net48/net8) y el
    /// arnés inyecta un transporte simulado. SIN credenciales en el repo: el
    /// cliente/clientSecret llegan por parámetro en tiempo de ejecución.
    /// </summary>
    public interface IDriveTransport
    {
        /// <summary>Ejecuta la petición. Devuelve status+cuerpo (también para
        /// 4xx/5xx CON cuerpo); lanza en fallo de red SIN respuesta (el
        /// llamador aplica su política offline).</summary>
        DriveHttpResponse Send(DriveHttpRequest request);
    }

    /// <summary>Transporte HTTP REAL (HttpWebRequest síncrono — net35 OK).</summary>
    public sealed class HttpDriveTransport : IDriveTransport
    {
        public static readonly HttpDriveTransport Default = new HttpDriveTransport();

        public DriveHttpResponse Send(DriveHttpRequest request)
        {
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(request.Url);
            rq.Method = request.Method;
            rq.Timeout = request.TimeoutMs > 0 ? request.TimeoutMs : 20000;
            if (!string.IsNullOrEmpty(request.ContentType)) rq.ContentType = request.ContentType;
            if (request.Headers != null)
            {
                foreach (KeyValuePair<string, string> kv in request.Headers)
                {
                    if (string.Equals(kv.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        continue;                       // va por propiedad propia
                    rq.Headers[kv.Key] = kv.Value;
                }
            }
            if (request.Body != null &&
                !string.Equals(request.Method, "GET", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(request.Method, "HEAD", StringComparison.OrdinalIgnoreCase))
            {
                rq.ContentLength = request.Body.Length;
                using (Stream s = rq.GetRequestStream()) s.Write(request.Body, 0, request.Body.Length);
            }
            try
            {
                using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
                    return ReadResponse(rs);
            }
            catch (WebException we)
            {
                HttpWebResponse rs = we.Response as HttpWebResponse;
                if (rs != null) return ReadResponse(rs);   // 4xx/5xx con cuerpo
                throw;                                     // red caída → política offline
            }
        }

        private static DriveHttpResponse ReadResponse(HttpWebResponse rs)
        {
            DriveHttpResponse outResp = new DriveHttpResponse();
            outResp.StatusCode = (int)rs.StatusCode;
            using (Stream s = rs.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[16384];
                int n;
                while (s != null && (n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                outResp.Body = ms.ToArray();
            }
            return outResp;
        }
    }

    public sealed class GoogleDriveClient : IDisposable
    {
        private string _accessToken = "";
        private string _refreshToken = "";
        private readonly string _clientId, _clientSecret;
        private readonly string _authBase, _tokenBase, _uploadBase, _apiBase;
        private int _timeoutMs = 20000;

        private readonly List<DriveConflict> _conflicts = new List<DriveConflict>();
        private int _uploaded, _downloaded;

        public GoogleDriveClient(string clientId, string clientSecret,
                                 string authBase, string tokenBase,
                                 string apiBase, string uploadBase)
        {
            _clientId = clientId ?? "";
            _clientSecret = clientSecret ?? "";
            _authBase = string.IsNullOrEmpty(authBase)
                ? "https://accounts.google.com/o/oauth2/v2/auth" : authBase;
            _tokenBase = string.IsNullOrEmpty(tokenBase)
                ? "https://oauth2.googleapis.com/token" : tokenBase;
            _apiBase = string.IsNullOrEmpty(apiBase)
                ? "https://www.googleapis.com/drive/v3" : apiBase;
            _uploadBase = string.IsNullOrEmpty(uploadBase)
                ? "https://www.googleapis.com/upload/drive/v3" : uploadBase;
        }

        public int TimeoutMs { get { return _timeoutMs; } set { _timeoutMs = value; } }

        /// <summary>F6.04: transporte inyectable (tests). null → HTTP real.</summary>
        public IDriveTransport Transport { get; set; }

        /// <summary>
        /// Restaura una sesión OAuth persistida (Settings locales — el usuario
        /// ya autorizó en un arranque anterior) o la inyecta en pruebas con el
        /// transporte simulado. No toca la red: el refresh ocurre en
        /// EnsureToken() cuando hace falta (F6.04.5 offline primero).
        /// </summary>
        public void SetSession(string refreshToken, string accessToken)
        {
            _refreshToken = refreshToken ?? "";
            _accessToken = accessToken ?? "";
        }

        /// <summary>F6.04.8: archivo del historial de conflictos (JSON
        /// persistente con WhenUtc/Resource/Resolution). Vacío = solo memoria.</summary>
        public string ConflictHistoryFile { get; set; }

        private DriveHttpResponse Send(DriveHttpRequest rq)
        {
            IDriveTransport t = Transport ?? HttpDriveTransport.Default;
            return t.Send(rq);
        }

        public DriveStatus Status()
        {
            DriveStatus st = new DriveStatus();
            st.Connected = _accessToken.Length > 0 || _refreshToken.Length > 0;
            st.Uploaded = _uploaded;
            st.Downloaded = _downloaded;
            st.Conflicts = _conflicts.Count;
            foreach (DriveConflict c in _conflicts)
            {
                st.LastError = "Conflicto resuelto (" + c.Resolution + "): " + c.Resource;
                if (c.WhenUtc > st.LastSyncUtc) st.LastSyncUtc = c.WhenUtc;
            }
            return st;
        }
        public List<DriveConflict> ConflictHistory()
        {
            lock (_conflicts) { return new List<DriveConflict>(_conflicts); }
        }

        /// <summary>Registra un conflicto y lo PERSISTE si hay archivo (F6.04.8).</summary>
        public DriveConflict RecordConflict(string resource, string localVersion,
                                            string cloudVersion, string resolution)
        {
            DriveConflict c = new DriveConflict();
            c.WhenUtc = DateTime.UtcNow;
            c.Resource = resource ?? "";
            c.LocalVersion = localVersion ?? "";
            c.CloudVersion = cloudVersion ?? "";
            c.Resolution = resolution ?? "";
            lock (_conflicts)
            {
                _conflicts.Add(c);
                if (_conflicts.Count > 500) _conflicts.RemoveRange(0, _conflicts.Count - 500);
            }
            SaveConflictHistory();
            return c;
        }

        /// <summary>Escribe el historial de conflictos a ConflictHistoryFile.</summary>
        public void SaveConflictHistory()
        {
            string path = ConflictHistoryFile;
            if (string.IsNullOrEmpty(path)) return;
            try
            {
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                List<object> arr = new List<object>();
                lock (_conflicts)
                {
                    foreach (DriveConflict c in _conflicts)
                    {
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["whenUtc"] = c.WhenUtc.ToString("yyyy-MM-ddTHH:mm:ssZ",
                            CultureInfo.InvariantCulture);
                        o["resource"] = c.Resource;
                        o["resolution"] = c.Resolution;
                        o["localVersion"] = c.LocalVersion;
                        o["cloudVersion"] = c.CloudVersion;
                        arr.Add(o);
                    }
                }
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["format"] = "lumina.drive-conflicts.v1";
                root["conflicts"] = arr;
                File.WriteAllText(path, MiniJson.Serialize(root) + "\n", new UTF8Encoding(false));
            }
            catch (IOException)
            {
                // Sin permisos/espacio: el historial queda en memoria (visible).
            }
        }

        /// <summary>Carga (fusiona) el historial persistido — tolerante a
        /// archivo ausente/corrupto (nunca bloquea el arranque).</summary>
        public void LoadConflictHistory()
        {
            string path = ConflictHistoryFile;
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(
                    File.ReadAllText(path, new UTF8Encoding(false)));
                List<object> arr = MiniJson.GetArray(root, "conflicts");
                lock (_conflicts)
                {
                    foreach (object eo in arr)
                    {
                        Dictionary<string, object> o = eo as Dictionary<string, object>;
                        if (o == null) continue;
                        DriveConflict c = new DriveConflict();
                        c.Resource = MiniJson.GetString(o, "resource", "");
                        c.Resolution = MiniJson.GetString(o, "resolution", "");
                        c.LocalVersion = MiniJson.GetString(o, "localVersion", "");
                        c.CloudVersion = MiniJson.GetString(o, "cloudVersion", "");
                        DateTime when;
                        if (DateTime.TryParse(MiniJson.GetString(o, "whenUtc", ""),
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
                            DateTimeStyles.AdjustToUniversal, out when))
                            c.WhenUtc = when;
                        else c.WhenUtc = DateTime.UtcNow;
                        // dedupe por (when, resource, resolution)
                        bool dup = false;
                        foreach (DriveConflict e in _conflicts)
                        {
                            if (e.WhenUtc == c.WhenUtc && e.Resource == c.Resource &&
                                e.Resolution == c.Resolution) { dup = true; break; }
                        }
                        if (!dup) _conflicts.Add(c);
                    }
                }
            }
            catch (Exception)
            {
                // Historial corrupto: se conserva lo que hay en memoria.
            }
        }

        // -------------------------------------------------- OAuth loopback --
        /// <summary>
        /// Autorización con navegador + loopback (F6.04): abre el navegador,
        /// espera el código en 127.0.0.1:<puerto aleatorio> y lo canjea por
        /// tokens. Devuelve true si quedó conectado. SIN interacción manual
        /// de copiar códigos.
        /// </summary>
        public bool AuthorizeWithBrowser(string[] scopes, Action<string> openUrl)
        {
            // 1) socket loopback efímero (el redirect_uri ES el socket).
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            string redirect = "http://127.0.0.1:" + port + "/";
            string scope = scopes == null ? "" : string.Join(" ", scopes);
            string url = _authBase + "?response_type=code&client_id=" + Uri.EscapeDataString(_clientId) +
                "&redirect_uri=" + Uri.EscapeDataString(redirect) +
                "&scope=" + Uri.EscapeDataString(scope) + "&access_type=offline&prompt=consent";
            if (openUrl != null) openUrl(url);

            string code = null;
            ManualResetEvent got = new ManualResetEvent(false);
            ThreadPool.QueueUserWorkItem(delegate
            {
                try
                {
                    using (TcpClient cli = listener.AcceptTcpClient())
                    using (NetworkStream ns = cli.GetStream())
                    {
                        byte[] buf = new byte[4096];
                        int n = ns.Read(buf, 0, buf.Length);
                        string req = Encoding.ASCII.GetString(buf, 0, n);
                        int sp = req.IndexOf(' ');
                        string path = sp < 0 ? "/" : req.Substring(sp + 1,
                            req.IndexOf(' ', sp + 1) - sp - 1);
                        int q = path.IndexOf('?');
                        if (q > 0)
                        {
                            foreach (string kv in path.Substring(q + 1).Split('&'))
                            {
                                int eq = kv.IndexOf('=');
                                if (eq > 0 && kv.Substring(0, eq) == "code")
                                    code = Uri.UnescapeDataString(kv.Substring(eq + 1));
                            }
                        }
                        byte[] body = Encoding.ASCII.GetBytes(
                            "HTTP/1.0 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" +
                            "Connection: close\r\n\r\n" +
                            "<html><body style=\"font-family:Segoe UI;background:#0b1f2a;" +
                            "color:#fff\"><h3>LuminaPresentation conectado a Drive</h3>" +
                            "<p>Puede cerrar esta pestaña y volver al programa.</p></body></html>");
                        ns.Write(body, 0, body.Length);
                        ns.Flush();
                    }
                }
                catch (Exception) { }
                finally { got.Set(); }
            });
            if (!got.WaitOne(180000))      // 3 minutos para el usuario
            {
                listener.Stop();
                return false;
            }
            listener.Stop();
            if (string.IsNullOrEmpty(code)) return false;

            // 2) canje code → tokens (POST x-www-form-urlencoded).
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["code"] = code;
            form["client_id"] = _clientId;
            form["client_secret"] = _clientSecret;
            form["redirect_uri"] = redirect;
            form["grant_type"] = "authorization_code";
            string resp = PostForm(_tokenBase, form);
            Dictionary<string, object> j = MiniJson.Parse(resp);
            _accessToken = MiniJson.GetString(j, "access_token", "");
            _refreshToken = MiniJson.GetString(j, "refresh_token", _refreshToken);
            return _accessToken.Length > 0;
        }

        /// <summary>Renueva el access token con el refresh token.</summary>
        public bool Refresh()
        {
            if (_refreshToken.Length == 0) return false;
            Dictionary<string, string> form = new Dictionary<string, string>();
            form["refresh_token"] = _refreshToken;
            form["client_id"] = _clientId;
            form["client_secret"] = _clientSecret;
            form["grant_type"] = "refresh_token";
            try
            {
                string resp = PostForm(_tokenBase, form);
                Dictionary<string, object> j = MiniJson.Parse(resp);
                _accessToken = MiniJson.GetString(j, "access_token", "");
                return _accessToken.Length > 0;
            }
            catch (Exception) { return false; }
        }

        // --------------------------------------------------------- subida --
        /// <summary>Sube/actualiza un archivo a la carpeta oculta de la app
        /// (F6.04.1-4: canciones/biblias/configuraciones/proyectos).
        /// Devuelve true si el servidor aceptó (2xx).</summary>
        public bool Upload(string remoteName, byte[] data, string localVersion)
        {
            if (!EnsureToken()) return false;
            // resumable simple: multipart POST al endpoint de upload con
            // metadata + contenido en una tanda (los tamaños de la app son
            // modestos: biblias ≤64 MB, resto KB).
            string boundary = "lumina-" + Guid.NewGuid().ToString("N");
            StringBuilder head = new StringBuilder();
            head.Append("--" + boundary + "\r\nContent-Type: application/json; charset=UTF-8\r\n\r\n");
            head.Append("{\"name\":\"" + JsonEscape(remoteName) + "\",\"parents\":[\"appDataFolder\"]}");
            head.Append("\r\n--" + boundary + "\r\nContent-Type: application/octet-stream\r\n\r\n");
            byte[] h = Encoding.UTF8.GetBytes(head.ToString());
            byte[] tail = Encoding.UTF8.GetBytes("\r\n--" + boundary + "--\r\n");
            byte[] all = new byte[h.Length + data.Length + tail.Length];
            Buffer.BlockCopy(h, 0, all, 0, h.Length);
            Buffer.BlockCopy(data, 0, all, h.Length, data.Length);
            Buffer.BlockCopy(tail, 0, all, h.Length + data.Length, tail.Length);
            DriveHttpRequest rq = new DriveHttpRequest();
            rq.Method = "POST";
            rq.Url = _uploadBase + "/files?uploadType=multipart";
            rq.Headers["Authorization"] = "Bearer " + _accessToken;
            rq.ContentType = "multipart/related; boundary=" + boundary;
            rq.TimeoutMs = _timeoutMs;
            rq.Body = all;
            DriveHttpResponse rs = Send(rq);
            if (rs.StatusCode >= 200 && rs.StatusCode < 300)
            {
                _uploaded++;
                return true;
            }
            return false;
        }

        /// <summary>Descarga un archivo por nombre dentro de appDataFolder.
        /// null si no existe en la nube (o fallo de autorización).</summary>
        public byte[] Download(string remoteName)
        {
            if (!EnsureToken()) return null;
            string q = Uri.EscapeDataString("name = '" + remoteName + "'");
            DriveHttpRequest rq = new DriveHttpRequest();
            rq.Method = "GET";
            rq.Url = _apiBase + "/files?spaces=appDataFolder&q=" + q +
                "&fields=files(id,size,name)";
            rq.Headers["Authorization"] = "Bearer " + _accessToken;
            rq.TimeoutMs = _timeoutMs;
            DriveHttpResponse rs = Send(rq);
            if (rs.StatusCode < 200 || rs.StatusCode >= 300) return null;
            string list = MiniJson.Utf8BytesToString(rs.Body);
            Dictionary<string, object> lj = MiniJson.Parse(list);
            List<object> files = MiniJson.GetArray(lj, "files");
            if (files.Count == 0) return null;
            Dictionary<string, object> f0 = files[0] as Dictionary<string, object>;
            string id = f0 == null ? "" : MiniJson.GetString(f0, "id", "");
            if (id.Length == 0) return null;
            DriveHttpRequest dl = new DriveHttpRequest();
            dl.Method = "GET";
            dl.Url = _apiBase + "/files/" + id + "?alt=media";
            dl.Headers["Authorization"] = "Bearer " + _accessToken;
            dl.TimeoutMs = _timeoutMs;
            DriveHttpResponse media = Send(dl);
            if (media.StatusCode < 200 || media.StatusCode >= 300) return null;
            _downloaded++;
            return media.Body ?? new byte[0];
        }

        // --------------------------------------------- resolución conflicto --
        /// <summary>
        /// F6.04.6-8: elige la versión por el campo «version» del ahp.v1
        /// (semilla de edición). SIN pérdida silenciosa: la versión perdedora
        /// se conserva como copia y todo queda en el historial.
        /// Devuelve true si la LOCAL es la ganadora (se sube).
        /// </summary>
        public bool ResolveConflict(string resource, string localVersion, string cloudVersion,
                                    out byte[] winner)
        {
            winner = null;
            long lv, cv;
            bool lok = long.TryParse(localVersion, out lv);
            bool cok = long.TryParse(cloudVersion, out cv);
            bool localWins = !cok || (lok && lv > cv);
            RecordConflict(resource, localVersion, cloudVersion,
                localWins ? "local" : "nube");
            // SIN pérdida silenciosa: la perdedora se descarga/conserva aparte.
            if (localWins)
            {
                byte[] cloud = Download(resource);
                if (cloud != null)
                    SaveLoserCopy(resource, cloud, "conflicto-nube");
                winner = File.ReadAllBytes(resource);
            }
            else
            {
                byte[] cloud = Download(resource);
                if (cloud == null) { localWins = true; }
                else
                {
                    SaveLoserCopy(resource, File.ReadAllBytes(resource), "conflicto-local");
                    winner = cloud;
                }
            }
            return localWins;
        }

        /// <summary>Guarda la copia CONSERVADA de la versión perdedora junto
        /// al archivo (F6.04.7: JAMÁS pérdida silenciosa). Tolerante a fallos.</summary>
        public static void SaveLoserCopy(string localPath, byte[] data, string suffix)
        {
            try
            {
                string copy = localPath + "." + suffix + "-" +
                    DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + ".bak";
                File.WriteAllBytes(copy, data);
            }
            catch (IOException) { }
        }

        // ----------------------------------------------------------- HTTP --
        private bool EnsureToken()
        {
            if (_accessToken.Length > 0) return true;
            return Refresh();
        }

        private string PostForm(string url, Dictionary<string, string> form)
        {
            StringBuilder sb = new StringBuilder();
            foreach (KeyValuePair<string, string> kv in form)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(Uri.EscapeDataString(kv.Key));
                sb.Append('=');
                sb.Append(Uri.EscapeDataString(kv.Value));
            }
            DriveHttpRequest rq = new DriveHttpRequest();
            rq.Method = "POST";
            rq.Url = url;
            rq.ContentType = "application/x-www-form-urlencoded";
            rq.TimeoutMs = _timeoutMs;
            rq.Body = Encoding.UTF8.GetBytes(sb.ToString());
            DriveHttpResponse rs = Send(rq);
            return MiniJson.Utf8BytesToString(rs.Body);
        }

        private static string JsonEscape(string s)
        {
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\').Append(c);
                else if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                else sb.Append(c);
            }
            return sb.ToString();
        }

        public void Dispose() { }
    }

    // ========================================================================
    // F6.04 — Sincronización OFFLINE PRIMERO (cola persistente + conflictos).
    // ========================================================================

    /// <summary>Operación pendiente de la cola offline-first (F6.04.5).</summary>
    public sealed class DrivePendingOp
    {
        public string Kind = "upload";      // "upload" | "download"
        public string Resource = "";        // nombre remoto: "proyectos/culto.ahp", "canciones/…"
        public string LocalPath = "";       // archivo local correspondiente
        public string Version = "";         // versión local al encolar (semilla ahp.v1)
        public DateTime QueuedUtc;
        public int Attempts;
    }

    /// <summary>Resultado de un ciclo de descarga (Flush) del motor de sync.</summary>
    public sealed class DriveSyncResult
    {
        public int Uploaded, Downloaded, Conflicts, Skipped;
        public string LastError = "";
        public bool Ok { get { return LastError.Length == 0; } }
    }

    /// <summary>
    /// Motor de sincronización Drive (F6.04): OFFLINE PRIMERO — la app funciona
    /// SIEMPRE sin red; las operaciones se encolan (cola persistente en JSON)
    /// y se descargan al reconectar (Flush). Los conflictos se resuelven por
    /// VERSIÓN ahp.v1 (semilla nextId del proyecto): elige local / nube /
    /// copia-conservada; la versión perdedora SIEMPRE queda como copia
    /// (JAMÁS pérdida silenciosa) y cada decisión queda en el historial
    /// persistente del cliente (WhenUtc/Resource/Resolution).
    /// Todo el transporte pasa por IDriveTransport → testeable sin red real.
    /// </summary>
    public sealed class DriveSyncEngine : IDisposable
    {
        public const string FormatV1 = "lumina.drive-queue.v1";

        private readonly GoogleDriveClient _client;
        private readonly string _queueFile;
        private readonly object _sync = new object();
        private readonly List<DrivePendingOp> _queue = new List<DrivePendingOp>();
        private bool _online;

        public DriveSyncEngine(GoogleDriveClient client, string queueFile)
        {
            _client = client;
            _queueFile = queueFile;
            LoadQueue();
        }

        /// <summary>¿Hay red? La app lo ajusta al detectar conectividad; los
        /// tests lo fijan explícitamente (offline primero).</summary>
        public bool IsOnline
        {
            get { lock (_sync) { return _online; } }
            set { lock (_sync) { _online = value; } }
        }

        public int PendingCount { get { lock (_sync) { return _queue.Count; } } }

        /// <summary>Copia de la cola pendiente (diagnóstico F5.10: «Drive»).</summary>
        public List<DrivePendingOp> PendingSnapshot()
        {
            lock (_sync) { return new List<DrivePendingOp>(_queue); }
        }

        // -------------------------------------------------------- categorías --
        /// <summary>Nombre remoto normalizado por categoría (F6.04.1-4):
        /// canciones / biblias / configuraciones / proyectos.</summary>
        public static string ResourceFor(string category, string fileName)
        {
            string cat = (category ?? "").Trim().ToLowerInvariant();
            string name = (fileName ?? "").Replace('\\', '/').TrimStart('/');
            if (cat == "canciones" || cat == "biblias" || cat == "configuraciones" ||
                cat == "proyectos")
                return cat + "/" + name;
            return name;   // categoría desconocida: raíz del appDataFolder
        }

        // ----------------------------------------------------------- encolar --
        /// <summary>Encola la SUBIDA de un archivo local (sin red: offline primero).</summary>
        public void QueueUpload(string resource, string localPath)
        {
            if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(localPath)) return;
            DrivePendingOp op = new DrivePendingOp();
            op.Kind = "upload";
            op.Resource = resource;
            op.LocalPath = localPath;
            op.Version = ExtractVersion(SafeRead(localPath));
            op.QueuedUtc = DateTime.UtcNow;
            lock (_sync) _queue.Add(op);
            SaveQueue();
        }

        /// <summary>Encola la DESCARGA de un recurso de la nube (se materializa
        /// al reconectar — F6.04.5).</summary>
        public void QueueDownload(string resource, string localPath)
        {
            if (string.IsNullOrEmpty(resource) || string.IsNullOrEmpty(localPath)) return;
            DrivePendingOp op = new DrivePendingOp();
            op.Kind = "download";
            op.Resource = resource;
            op.LocalPath = localPath;
            op.Version = File.Exists(localPath)
                ? ExtractVersion(SafeRead(localPath)) : "";
            op.QueuedUtc = DateTime.UtcNow;
            lock (_sync) _queue.Add(op);
            SaveQueue();
        }

        /// <summary>Procesa la cola EN ORDEN si hay red. Sin red: no toca nada
        /// (las operaciones siguen pendientes — offline primero). Tolerante:
        /// un fallo de red en una operación NO descarta el resto.</summary>
        public DriveSyncResult Flush()
        {
            DriveSyncResult res = new DriveSyncResult();
            if (!IsOnline)
            {
                res.LastError = "sin red: las operaciones siguen en cola (offline primero)";
                res.Skipped = PendingCount;
                return res;
            }
            List<DrivePendingOp> batch;
            lock (_sync) { batch = new List<DrivePendingOp>(_queue); _queue.Clear(); }
            foreach (DrivePendingOp op in batch)
            {
                try
                {
                    if (op.Kind == "download") ProcessDownload(op, res);
                    else ProcessUpload(op, res);
                }
                catch (Exception ex)
                {
                    // Fallo de red/transporte: la operación VUELVE a la cola
                    // (nunca se pierde) y el ciclo continúa con el resto.
                    op.Attempts++;
                    res.Skipped++;
                    res.LastError = op.Resource + ": " + ex.Message;
                    lock (_sync) _queue.Add(op);
                }
            }
            SaveQueue();
            return res;
        }

        // ------------------------------------------------------- procesadores --
        private void ProcessUpload(DrivePendingOp op, DriveSyncResult res)
        {
            if (!File.Exists(op.LocalPath))
            {
                // El archivo local desapareció: queda registrado, no se inventa.
                res.Skipped++;
                res.LastError = op.Resource + ": el archivo local ya no existe";
                return;
            }
            byte[] local = File.ReadAllBytes(op.LocalPath);
            string localV = ExtractVersion(local);
            byte[] cloud = _client.Download(op.Resource);
            if (cloud == null)
            {
                // Sin copia en la nube: subida directa (F6.04.1-4).
                if (_client.Upload(op.Resource, local, localV)) res.Uploaded++;
                else { res.Skipped++; res.LastError = op.Resource + ": subida rechazada"; }
                return;
            }
            string cloudV = ExtractVersion(cloud);
            long lv, cv;
            bool lok = long.TryParse(localV, out lv);
            bool cok = long.TryParse(cloudV, out cv);
            if (lok && cok && lv == cv)
            {
                // Misma versión: ya sincronizado (subida idempotente innecesaria).
                return;
            }
            if (lok && cok)
            {
                // CONFLICTO real ahp.v1 (F6.04.6-7): gana la MAYOR versión;
                // la perdedora SIEMPRE queda como copia junto al archivo.
                bool localWins = lv > cv;
                res.Conflicts++;
                if (localWins)
                {
                    GoogleDriveClient.SaveLoserCopy(op.LocalPath, cloud, "conflicto-nube");
                    _client.RecordConflict(op.Resource, localV, cloudV, "local");
                    if (_client.Upload(op.Resource, local, localV)) res.Uploaded++;
                    else { res.Skipped++; res.LastError = op.Resource + ": subida rechazada"; }
                }
                else
                {
                    GoogleDriveClient.SaveLoserCopy(op.LocalPath, local, "conflicto-local");
                    _client.RecordConflict(op.Resource, localV, cloudV, "nube");
                    File.WriteAllBytes(op.LocalPath, cloud);
                    res.Downloaded++;
                }
                return;
            }
            // Versiones NO comparables (no ahp.v1 / sin semilla): COPIA-
            // CONSERVADA — nada se pisa; la local sube con nombre nuevo y
            // la nube queda intacta (JAMÁS pérdida silenciosa, F6.04.7).
            res.Conflicts++;
            _client.RecordConflict(op.Resource, localV, cloudV, "copia-conservada");
            string alt = op.Resource + ".local-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            if (_client.Upload(alt, local, localV)) res.Uploaded++;
            else { res.Skipped++; res.LastError = op.Resource + ": copia conservada sin subir"; }
        }

        private void ProcessDownload(DrivePendingOp op, DriveSyncResult res)
        {
            byte[] cloud = _client.Download(op.Resource);
            if (cloud == null)
            {
                res.Skipped++;
                res.LastError = op.Resource + ": no existe en la nube";
                return;
            }
            string cloudV = ExtractVersion(cloud);
            if (!File.Exists(op.LocalPath))
            {
                // Primera descarga: materializar al reconectar (F6.04.5).
                string dir = Path.GetDirectoryName(op.LocalPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(op.LocalPath, cloud);
                res.Downloaded++;
                return;
            }
            byte[] local = File.ReadAllBytes(op.LocalPath);
            string localV = ExtractVersion(local);
            long lv, cv;
            bool lok = long.TryParse(localV, out lv);
            bool cok = long.TryParse(cloudV, out cv);
            if (lok && cok && lv == cv) return;                 // ya iguales
            if (lok && cok)
            {
                res.Conflicts++;
                if (cv > lv)
                {
                    // Nube más nueva: la local perdedora queda como copia.
                    GoogleDriveClient.SaveLoserCopy(op.LocalPath, local, "conflicto-local");
                    _client.RecordConflict(op.Resource, localV, cloudV, "nube");
                    File.WriteAllBytes(op.LocalPath, cloud);
                    res.Downloaded++;
                }
                else
                {
                    // Local más nueva: NO se pisa; la nube perdedora se
                    // conserva como copia y la local sube (ganará por versión).
                    GoogleDriveClient.SaveLoserCopy(op.LocalPath, cloud, "conflicto-nube");
                    _client.RecordConflict(op.Resource, localV, cloudV, "local");
                    if (_client.Upload(op.Resource, local, localV)) res.Uploaded++;
                    else { res.Skipped++; res.LastError = op.Resource + ": subida rechazada"; }
                }
                return;
            }
            // No comparables: copia-conservada — la nube baja a un archivo
            // aparte, la local queda intacta (nada se pierde).
            res.Conflicts++;
            _client.RecordConflict(op.Resource, localV, cloudV, "copia-conservada");
            string sidecar = op.LocalPath + ".nube-" +
                DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            File.WriteAllBytes(sidecar, cloud);
            res.Downloaded++;
        }

        // ---------------------------------------------------------- versiones --
        /// <summary>Versión comparable de un recurso (F6.04.6): para ahp.v1 es
        /// la semilla nextId del proyecto; para JSON con "version", ese valor;
        /// vacío ("" = no comparable → copia-conservada).</summary>
        public static string ExtractVersion(byte[] data)
        {
            if (data == null || data.Length == 0) return "";
            try
            {
                Dictionary<string, object> o = MiniJson.Parse(
                    MiniJson.Utf8BytesToString(data));
                string format = MiniJson.GetString(o, "format", "");
                if (format == "ahp.v1")
                {
                    Dictionary<string, object> proj = MiniJson.GetObject(o, "project");
                    return proj == null ? "" : MiniJson.GetInt(proj, "nextId", 0)
                        .ToString(CultureInfo.InvariantCulture);
                }
                object v;
                if (o.TryGetValue("version", out v))
                {
                    if (v is long) return ((long)v).ToString(CultureInfo.InvariantCulture);
                    if (v is double) return ((double)v).ToString(CultureInfo.InvariantCulture);
                    string sv = v as string;
                    if (!string.IsNullOrEmpty(sv)) return sv;
                }
            }
            catch (Exception) { }   // binario/corrupto: no comparable
            return "";
        }

        // ------------------------------------------------------------- cola --
        /// <summary>Persiste la cola (JSON en _queueFile) — sobrevive al cierre.
        /// Tolerante: sin permisos la cola queda en memoria.</summary>
        public void SaveQueue()
        {
            if (string.IsNullOrEmpty(_queueFile)) return;
            try
            {
                string dir = Path.GetDirectoryName(_queueFile);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                List<object> arr = new List<object>();
                lock (_sync)
                {
                    foreach (DrivePendingOp op in _queue)
                    {
                        Dictionary<string, object> o = new Dictionary<string, object>();
                        o["kind"] = op.Kind;
                        o["resource"] = op.Resource;
                        o["localPath"] = op.LocalPath;
                        o["version"] = op.Version;
                        o["queuedUtc"] = op.QueuedUtc.ToString("yyyy-MM-ddTHH:mm:ssZ",
                            CultureInfo.InvariantCulture);
                        o["attempts"] = (long)op.Attempts;
                        arr.Add(o);
                    }
                }
                Dictionary<string, object> root = new Dictionary<string, object>();
                root["format"] = FormatV1;
                root["ops"] = arr;
                File.WriteAllText(_queueFile, MiniJson.Serialize(root) + "\n",
                    new UTF8Encoding(false));
            }
            catch (IOException) { }
        }

        /// <summary>Carga la cola persistida (tolerante a archivo ausente/corrupto).</summary>
        public void LoadQueue()
        {
            if (string.IsNullOrEmpty(_queueFile) || !File.Exists(_queueFile)) return;
            try
            {
                Dictionary<string, object> root = MiniJson.Parse(
                    File.ReadAllText(_queueFile, new UTF8Encoding(false)));
                List<object> arr = MiniJson.GetArray(root, "ops");
                lock (_sync)
                {
                    foreach (object eo in arr)
                    {
                        Dictionary<string, object> o = eo as Dictionary<string, object>;
                        if (o == null) continue;
                        DrivePendingOp op = new DrivePendingOp();
                        op.Kind = MiniJson.GetString(o, "kind", "upload");
                        op.Resource = MiniJson.GetString(o, "resource", "");
                        op.LocalPath = MiniJson.GetString(o, "localPath", "");
                        op.Version = MiniJson.GetString(o, "version", "");
                        op.Attempts = (int)MiniJson.GetInt(o, "attempts", 0);
                        DateTime when;
                        if (DateTime.TryParse(MiniJson.GetString(o, "queuedUtc", ""),
                            CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal |
                            DateTimeStyles.AdjustToUniversal, out when))
                            op.QueuedUtc = when;
                        else op.QueuedUtc = DateTime.UtcNow;
                        _queue.Add(op);
                    }
                }
            }
            catch (Exception)
            {
                // Cola corrupta: arranca vacía (los datos locales NUNCA se tocan).
            }
        }

        private static byte[] SafeRead(string path)
        {
            try { return File.ReadAllBytes(path); }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }

        public void Dispose()
        {
            SaveQueue();
        }
    }
}

#pragma warning restore SYSLIB0014
