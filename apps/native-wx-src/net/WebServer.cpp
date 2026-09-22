// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  WebServer.cpp : implementacion del servidor HTTP remoto (ver WebServer.h)
// ============================================================================
#include "WebServer.h"

#include <wx/buffer.h>
#include <wx/tokenzr.h>
#include <wx/log.h>
#include <wx/uri.h>

#include <functional>

namespace {
constexpr int ID_SERVER = wxID_HIGHEST + 1;
constexpr int ID_CLIENT = wxID_HIGHEST + 2;

// Respuesta minimalista con longitud exacta (evita desincronia HTTP)
inline wxString FixedResponse(const wxString &status, const wxString &body)
{
    return wxString::Format(
        "HTTP/1.1 %s\r\nContent-Type: text/plain; charset=utf-8\r\n"
        "Content-Length: %zu\r\nConnection: close\r\n\r\n%s",
        status, (size_t)body.utf8_str().length(), body);
}
} // namespace

wxDEFINE_EVENT(EVT_REMOTE_COMMAND, wxCommandEvent);

// ---------------------------------------------------------------------------
// Pagina remota embebida (movil): next/prev/black/clear/logo + estado vivo.
// Sin dependencias externas; autorefresca cada 2 s; tema oscuro.
// ---------------------------------------------------------------------------
const char *REMOTE_HTML = R"HTML(<!doctype html>
<html lang="es"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1,user-scalable=no">
<title>Fusion-HP Remoto</title>
<style>
:root{--bg:#0b0f17;--card:#151b28;--tx:#f2f5fa;--gold:#e6a82a;--ok:#3fbf7f}
*{box-sizing:border-box;margin:0;padding:0;font-family:system-ui,Segoe UI,Arial}
body{background:var(--bg);color:var(--tx);min-height:100vh;display:flex;flex-direction:column;align-items:center;padding:14px;gap:12px}
h1{font-size:15px;letter-spacing:2px;color:var(--gold);text-transform:uppercase}
#live{background:var(--card);border-radius:14px;padding:16px;width:100%;max-width:420px;min-height:120px;text-align:center;box-shadow:0 4px 18px #0008}
#ref{color:var(--gold);font-size:13px;letter-spacing:1px;margin-bottom:6px;text-transform:uppercase}
#txt{font-size:20px;line-height:1.35;white-space:pre-wrap}
#pos{margin-top:10px;color:#8fa0b8;font-size:12px}
.grid{display:grid;grid-template-columns:1fr 1fr 1fr;gap:10px;width:100%;max-width:420px}
button{border:0;border-radius:12px;padding:16px 8px;font-size:16px;font-weight:600;background:var(--card);color:var(--tx);cursor:pointer;transition:transform .05s,background .15s}
button:active{transform:scale(.96);background:#1f2738}
.wide{grid-column:span 3;background:#233047}
.gold{background:var(--gold);color:#151b28}
.dim{color:#8fa0b8}
#conn{font-size:12px;color:#8fa0b8}
#conn.on{color:var(--ok)}
</style></head><body>
<h1>● Fusion-HP — Remoto</h1>
<div id="conn">conectando…</div>
<div id="live"><div id="ref">—</div><div id="txt">Sin contenido en vivo</div><div id="pos"></div></div>
<div class="grid">
  <button onclick="cmd('prev')">◀ Anterior</button>
  <button onclick="cmd('black')">⬤ Negro</button>
  <button onclick="cmd('next')">Siguiente ▶</button>
  <button onclick="cmd('clear')">✕ Limpiar</button>
  <button class="gold" onclick="cmd('logo')">⌂ Logo</button>
  <button onclick="cmd('alert')">📣 Aviso</button>
</div>
<script>
const QS=new URLSearchParams(location.search);
const TOK=QS.get('token')||'';
const TOKQ=TOK?('&token='+encodeURIComponent(TOK)):'';
async function state(){
  try{
    const r=await fetch('/api/state?_=1'+TOKQ,{cache:'no-store'});
    const j=await r.json();
    document.getElementById('conn').textContent='en línea · '+j.app;
    document.getElementById('conn').className='on';
    document.getElementById('ref').textContent=j.ref||'';
    document.getElementById('txt').textContent=j.text||'Sin contenido en vivo';
    document.getElementById('pos').textContent=j.count>0?('slide '+(j.index+1)+' / '+j.count):'';
  }catch(e){
    document.getElementById('conn').textContent='sin conexión con el servidor';
    document.getElementById('conn').className='';
  }
}
async function cmd(c){
  if(c==='alert'){
    const t=prompt('Texto del aviso para la pantalla:');
    if(!t) return;
    await fetch('/api/cmd?c='+encodeURIComponent('alert:'+t)+TOKQ.replace('&','&'));
    return;
  }
  try{await fetch('/api/cmd?c='+encodeURIComponent(c)+TOKQ.replace('&','&'));}catch(e){}
  setTimeout(state,250);
}
state();setInterval(state,2500);
</script></body></html>
)HTML";

// ---------------------------------------------------------------------------
// Ciclo de vida
// ---------------------------------------------------------------------------
WebServer::~WebServer()
{
    Stop();
}

bool WebServer::Start(int port, const wxString &token, StateFn stateFn, LiveTextFn liveFn,
                      wxString *error)
{
    Stop();
    m_port = port;
    m_token = token;
    m_stateFn = std::move(stateFn);
    m_liveFn = std::move(liveFn);

    wxIPV4address addr;
    addr.AnyAddress();
    addr.Service(port);
    m_server = new wxSocketServer(addr, wxSOCKET_REUSEADDR);
    if (!m_server->IsOk()) {
        delete m_server;
        m_server = nullptr;
        if (error)
            *error = wxString::Format("No se pudo abrir el puerto %d (¿en uso por otro programa?)", port);
        return false;
    }
    m_server->SetEventHandler(*this, ID_SERVER);
    m_server->SetNotify(wxSOCKET_CONNECTION_FLAG);
    m_server->Notify(true);
    Bind(wxEVT_SOCKET, &WebServer::OnServerEvent, this, ID_SERVER);
    return true;
}

void WebServer::Stop()
{
    if (!m_server)
        return;
    // C1: desconectar eventos y destruir clientes ANTES del servidor.
    m_server->SetNotify(0);
    m_server->Notify(false);
    this->Unbind(wxEVT_SOCKET, &WebServer::OnServerEvent, this, ID_SERVER);
    for (auto &kv : m_clients) {
        Client &cl = *kv.second;
        cl.sock->SetEventHandler(*this, ID_CLIENT);
        cl.sock->SetNotify(0);
        cl.sock->Notify(false);
        cl.sock->Destroy();          // diferido: seguro dentro de eventos wx
    }
    m_clients.clear();
    m_server->Destroy();
    m_server = nullptr;
}

void WebServer::OnServerEvent(wxSocketEvent &e)
{
    if (e.GetSocketEvent() != wxSOCKET_CONNECTION)
        return;
    wxSocketBase *sock = m_server ? m_server->Accept(false) : nullptr;
    if (!sock)
        return;
    sock->SetEventHandler(*this, ID_CLIENT);
    sock->SetNotify(wxSOCKET_INPUT_FLAG | wxSOCKET_LOST_FLAG);
    sock->Notify(true);
    auto cl = std::make_unique<Client>();
    cl->sock = sock;
    m_clients[sock] = std::move(cl);
}

void WebServer::OnClientEvent(wxSocketEvent &e)
{
    wxSocketBase *sock = e.GetSocket();
    if (!sock)
        return;
    if (e.GetSocketEvent() == wxSOCKET_LOST) {
        DropClient(sock);
        return;
    }
    auto it = m_clients.find(sock);
    if (it == m_clients.end())
        return;
    Client &cl = *it->second;

    char tmp[4096];
    sock->Read(tmp, sizeof(tmp));
    const size_t n = sock->LastCount();
    if (n == 0) {
        if (sock->Error() && sock->IsDisconnected())
            DropClient(sock);
        return;
    }
    cl.buffer += wxString::FromUTF8(tmp, n);
    if (cl.buffer.Length() > 64 * 1024) {              // M1: tope de RAM
        SendRaw(cl, FixedResponse("400 Bad Request", "peticion demasiado grande\n"), "");
        sock->Close();
        DropClient(sock);
        return;
    }
    if (cl.buffer.Contains("\r\n\r\n"))
        HandleRequest(cl);
}

void WebServer::DropClient(const wxSocketBase *sock)
{
    auto it = m_clients.find(const_cast<wxSocketBase *>(sock));
    if (it == m_clients.end())
        return;
    Client &cl = *it->second;
    cl.sock->SetEventHandler(*this, ID_CLIENT);
    cl.sock->SetNotify(0);
    cl.sock->Notify(false);
    cl.sock->Destroy();
    m_clients.erase(it);
}

// ---------------------------------------------------------------------------
// Enrutado
// ---------------------------------------------------------------------------
void WebServer::HandleRequest(Client &cl)
{
    const wxString req = cl.buffer.BeforeFirst('\n').Trim(true).Trim(false);
    cl.buffer.clear();

    wxString method = req.BeforeFirst(' ');
    wxString target = req.AfterFirst(' ').BeforeLast(' ').Trim(true).Trim(false);
    if (method != "GET" || target.empty()) {
        SendRaw(cl, FixedResponse("405 Method Not Allowed", ""), "");
        return;
    }

    // B13: '?' sobre el target CRUDO
    const int qm = target.Find('?');
    const wxString path = qm == wxNOT_FOUND ? target : target.Left(qm);
    const wxString query = qm == wxNOT_FOUND ? wxString() : target.Mid(qm + 1);

    // Parametros (token, comando) con decodificacion de %XX
    wxString tokenParam, cmdParam, alertText;
    wxStringTokenizer toks(query, "&");
    while (toks.HasMoreTokens()) {
        const wxString kv = toks.GetNextToken();
        const wxString k = kv.BeforeFirst('=');
        const wxString v = wxURI::Unescape(kv.AfterFirst('='));
        if (k == "token")  tokenParam = v;
        if (k == "c")      cmdParam = v;
    }

    // M2: token exigido si esta configurado (en /api/*)
    const bool needToken = !m_token.empty() && path.StartsWith("/api/");
    const bool tokenOk = m_token.empty() || tokenParam == m_token;
    if (needToken && !tokenOk) {
        SendRaw(cl, FixedResponse("401 Unauthorized", "token invalido\n"), "");
        return;
    }

    if (path == "/" || path == "/remote.html") {
        SendHtml(cl, wxString::FromUTF8(REMOTE_HTML));
        return;
    }
    if (path == "/api/state") {
        SendJson(cl, m_stateFn ? m_stateFn() : "{}");
        return;
    }
    if (path == "/api/live.txt") {
        SendRaw(cl, "HTTP/1.1 200 OK\r\nContent-Type: text/plain; charset=utf-8\r\n"
                    "Cache-Control: no-store\r\nConnection: close\r\n\r\n",
                m_liveFn ? m_liveFn() : wxString());
        return;
    }
    if (path == "/api/cmd") {
        if (!cmdParam.empty() && m_sink) {
            wxCommandEvent ev(EVT_REMOTE_COMMAND);
            ev.SetString(cmdParam);
            // Los eventos de socket YA corren en el hilo principal; QueueEvent
            // al sumidero (MainFrame) garantiza orden y evita re-entrada.
            m_sink->QueueEvent(ev.Clone());
        }
        SendRaw(cl, "HTTP/1.1 200 OK\r\nContent-Type: application/json; charset=utf-8\r\n"
                    "Content-Length: 9\r\nConnection: close\r\n\r\n", "{\"ok\":true}");
        return;
    }
    // B13: rutas desconocidas -> 404
    SendRaw(cl, FixedResponse("404 Not Found", "not found\n"), "");
}

// ---------------------------------------------------------------------------
// Respuestas
// ---------------------------------------------------------------------------
void WebServer::SendRaw(Client &cl, const wxString &headers, const wxString &body)
{
    if (!cl.sock || !cl.sock->IsOk())
        return;
    const wxScopedCharBuffer hb = headers.utf8_str();
    const wxScopedCharBuffer bb = body.utf8_str();
    cl.sock->Write(hb.data(), hb.length());
    if (bb.length() > 0)
        cl.sock->Write(bb.data(), bb.length());
}

void WebServer::SendHtml(Client &cl, const wxString &html, int code)
{
    const wxScopedCharBuffer bb = html.utf8_str();
    const wxString head = wxString::Format(
        "HTTP/1.1 %d OK\r\nContent-Type: text/html; charset=utf-8\r\n"
        "Content-Length: %zu\r\nConnection: close\r\n\r\n", code, (size_t)bb.length());
    SendRaw(cl, head, html);
}

void WebServer::SendJson(Client &cl, const wxString &json, int code)
{
    const wxScopedCharBuffer bb = json.utf8_str();
    const wxString head = wxString::Format(
        "HTTP/1.1 %d OK\r\nContent-Type: application/json; charset=utf-8\r\n"
        "Cache-Control: no-store\r\nContent-Length: %zu\r\nConnection: close\r\n\r\n",
        code, (size_t)bb.length());
    SendRaw(cl, head, json);
}
