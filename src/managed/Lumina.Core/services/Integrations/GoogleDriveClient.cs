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
// ============================================================================
using System;
using System.Collections.Generic;
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
        /// (F6.04.1-4: canciones/biblias/configuraciones/proyectos).</summary>
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
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(
                _uploadBase + "/files?uploadType=multipart");
            rq.Method = "POST";
            rq.Headers["Authorization"] = "Bearer " + _accessToken;
            rq.ContentType = "multipart/related; boundary=" + boundary;
            rq.Timeout = _timeoutMs;
            rq.ContentLength = all.Length;
            using (Stream s = rq.GetRequestStream()) s.Write(all, 0, all.Length);
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
        {
            if ((int)rs.StatusCode >= 200 && (int)rs.StatusCode < 300)
            {
                _uploaded++;
                return true;
            }
        }
            return false;
        }

        /// <summary>Descarga un archivo por nombre dentro de appDataFolder.</summary>
        public byte[] Download(string remoteName)
        {
            if (!EnsureToken()) return null;
            string q = Uri.EscapeDataString("name = '" + remoteName + "'");
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(
                _apiBase + "/files?spaces=appDataFolder&q=" + q + "&fields=files(id,size)");
            rq.Headers["Authorization"] = "Bearer " + _accessToken;
            rq.Timeout = _timeoutMs;
            string list;
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                list = sr.ReadToEnd();
            Dictionary<string, object> lj = MiniJson.Parse(list);
            List<object> files = MiniJson.GetArray(lj, "files");
            if (files.Count == 0) return null;
            Dictionary<string, object> f0 = files[0] as Dictionary<string, object>;
            string id = f0 == null ? "" : MiniJson.GetString(f0, "id", "");
            if (id.Length == 0) return null;
            HttpWebRequest dl = (HttpWebRequest)WebRequest.Create(
                _apiBase + "/files/" + id + "?alt=media");
            dl.Headers["Authorization"] = "Bearer " + _accessToken;
            dl.Timeout = _timeoutMs;
            using (HttpWebResponse rs = (HttpWebResponse)dl.GetResponse())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[16384]; int n;
                Stream s = rs.GetResponseStream();
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                _downloaded++;
                return ms.ToArray();
            }
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
            DriveConflict c = new DriveConflict();
            c.WhenUtc = DateTime.UtcNow;
            c.Resource = resource;
            c.LocalVersion = localVersion;
            c.CloudVersion = cloudVersion;
            c.Resolution = localWins ? "local" : "nube";
            // SIN pérdida silenciosa: la perdedora se descarga/conserva aparte.
            if (localWins)
            {
                byte[] cloud = Download(resource);
                if (cloud != null)
                    SaveCopy(resource, cloud, "conflicto-nube");
                winner = File.ReadAllBytes(resource);
            }
            else
            {
                byte[] cloud = Download(resource);
                if (cloud == null) { localWins = true; }
                else
                {
                    SaveCopy(resource, File.ReadAllBytes(resource), "conflicto-local");
                    winner = cloud;
                }
            }
            lock (_conflicts) { _conflicts.Add(c); }
            return localWins;
        }

        private static void SaveCopy(string resource, byte[] data, string suffix)
        {
            try
            {
                string copy = resource + "." + suffix + "-" +
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
            HttpWebRequest rq = (HttpWebRequest)WebRequest.Create(url);
            rq.Method = "POST";
            rq.ContentType = "application/x-www-form-urlencoded";
            rq.Timeout = _timeoutMs;
            byte[] body = Encoding.UTF8.GetBytes(sb.ToString());
            rq.ContentLength = body.Length;
            using (Stream s = rq.GetRequestStream()) s.Write(body, 0, body.Length);
            using (HttpWebResponse rs = (HttpWebResponse)rq.GetResponse())
            using (StreamReader sr = new StreamReader(rs.GetResponseStream(), Encoding.UTF8))
                return sr.ReadToEnd();
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
}

#pragma warning restore SYSLIB0014
