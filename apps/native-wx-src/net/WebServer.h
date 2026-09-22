// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  WebServer.h : Servidor HTTP/control remoto embebido (wxSocketServer).
//
//  Lecciones del auditorio v1.6.0 aplicadas al port:
//   - M1: buffer por cliente con tope de 64 KB (respuesta 400 + corte);
//   - M2: token exigido en upgrade y endpoints de estado cuando esta
//         configurado (?token=...);
//   - B13: el '?' se calcula sobre el target CRUDO y rutas desconocidas
//         responden 404 (no la pagina principal con 200);
//   - C1: stop() desmantela clientes (destruye sockets con claridad de
//         eventos ANTES de destruir el servidor) — sin doble free.
//
//  Todo el trafico se procesa por eventos wxSocket en el hilo principal:
//  los comandos se despachan directamente al MainFrame (sin datos de
//  carrera) y el estado se consulta bajo demanda.
// ============================================================================
#ifndef LUMINA_WEBSERVER_H
#define LUMINA_WEBSERVER_H

#include <wx/event.h>
#include <wx/socket.h>
#include <wx/string.h>

#include <map>
#include <memory>

// Evento: comando remoto recibido ("next","prev","black","clear","logo",
// "goto:5","live:3","alert:hola")
wxDECLARE_EVENT(EVT_REMOTE_COMMAND, wxCommandEvent);

class WebServer : public wxEvtHandler
{
public:
    WebServer() = default;
    ~WebServer() override;

    // Proveedor de estado JSON (lo llama el hilo principal al responder)
    using StateFn = std::function<wxString()>;
    using LiveTextFn = std::function<wxString()>;

    bool Start(int port, const wxString &token, StateFn stateFn, LiveTextFn liveFn,
               wxString *error = nullptr);

    // Sumidero de comandos (MainFrame): recibe EVT_REMOTE_COMMAND
    void SetSink(wxEvtHandler *sink) { m_sink = sink; }
    void Stop();
    bool IsRunning() const { return m_server != nullptr; }
    int  Port() const { return m_port; }

private:
    struct Client
    {
        wxSocketBase *sock = nullptr;
        wxString buffer;
    };

    void OnServerEvent(wxSocketEvent &e);
    void OnClientEvent(wxSocketEvent &e);
    void HandleRequest(Client &cl);
    void SendRaw(Client &cl, const wxString &headers, const wxString &body);
    void SendHtml(Client &cl, const wxString &html, int code = 200);
    void SendJson(Client &cl, const wxString &json, int code = 200);
    void DropClient(const wxSocketBase *sock);

    wxSocketServer *m_server = nullptr;
    wxEvtHandler *m_sink = nullptr;
    int m_port = 0;
    wxString m_token;
    StateFn m_stateFn;
    LiveTextFn m_liveFn;
    std::map<wxSocketBase *, std::unique_ptr<Client>> m_clients;

    wxDECLARE_NO_COPY_CLASS(WebServer);
};

// Pagina de control remoto (movil) embebida
extern const char *REMOTE_HTML;

#endif // LUMINA_WEBSERVER_H
