// ============================================================================
//  Fusion-HP · Motor.h — EL MOTOR DEL PROGRAMA (v2.2, linaje beta 1)
//  El núcleo nativo es el protagonista: es dueño del estado vivo (programa
//  cargado, índices de escenario/elemento/línea, modo de pantalla, resaltado)
//  y lo persiste. La GUI (chrome de la versión web) es una vista/controlador
//  desechable: si se cierra, el Motor SIGUE PROYECTANDO el último programa y
//  admite control por teclado sobre la salida — el programa nunca se cae por
//  cerrar la ventana del operador.
//  La GUI resuelve la herencia de 4 niveles [SPEC §5.4] y entrega diapositivas
//  RESUELTAS; el Motor las secuencia, aplica resaltado y conmuta el render.
// ============================================================================
#pragma once
#include "Common.h"
#include <functional>
#include <mutex>

namespace fusion {

// Un elemento del programa: diapositiva resuelta + metadatos para las listas.
struct MotorElement {
    std::string id;
    std::string title;
    std::string kindHint;      // text|verse|image|video|lower3 (icono de la GUI)
    int lineCount = 0;
    Json slide;                // contrato ipc.v1 resuelto (con transición horneada)
};

struct MotorScenario {
    std::string id;
    std::string title;
    std::vector<MotorElement> elements;
};

class Motor {
public:
    Motor() = default;

    // ---- enlaces que instala App (aplicar al mundo real) ----
    // Aplica una diapositiva resuelta (parse + video + estado + render).
    std::function<void(const Json&)> ApplySlideJson;
    // Aplica un modo de pantalla: black|logo|theme|clear|none.
    std::function<void(const std::string&)> ApplyBlank;
    // Precarga el fondo/imagen del siguiente elemento [SPEC §6.4].
    std::function<void(const Json&)> PreloadSlide;
    // Notifica a la GUI (broadcast motor.state).
    std::function<void()> Broadcast;

    // ---- carga / persistencia ----
    // motor.load {program:[...], select?{...}, advance?, highlight?}
    bool LoadProgram(const Json& payload, std::string* err = nullptr);
    // motor.append {scenario:{...}} — añade al final y lo selecciona (Enviar a vivo).
    bool AppendScenario(const Json& payload, std::string* err = nullptr);
    void Clear();                                   // motor.clear
    bool LoadPersisted(const std::wstring& file);   // arranque: sesion.json
    void SavePersisted(const std::wstring& file);

    // ---- navegación ----
    void Next();                    // línea (o elemento si advance="slide")
    void Prev();
    void NextElement();
    void PrevElement();
    bool Goto(int scnIdx, int elIdx, int lineIdx);  // motor.goto
    bool SetLine(int line);                          // motor.line

    // ---- pantalla / modos ----
    void SetBlank(const std::string& mode);          // motor.blank
    void SetAdvance(const std::string& mode);        // motor.advance
    void Highlight(const std::vector<std::string>& words);  // motor.highlight

    // ---- consultas ----
    Json StateJson() const;          // estado completo (evento/respuesta motor.state)
    bool HasProgram() const;
    bool ValidSelection() const;
    int LineIndexOfCurrent() const { return lineIdx_; }

    // ---- control autónomo (sin GUI) ----
    // Teclas sobre la salida: Espacio/→ avanza, ← retrocede, B/C/L pantallas,
    // Esc negro. Las activa App cuando no hay clientes GUI conectados.
    void StandaloneKey(UINT vk);
    bool StandaloneKeysEnabled() const { return standaloneKeys_; }
    void SetStandaloneKeys(bool on) { standaloneKeys_ = on; }

    // ---- persistencia interna ----
    void SetPersistFile(const std::wstring& file) { persistFile_ = file; }

private:
    void ShowCurrent(bool forceCut);   // aplica el elemento activo (con resaltado)
    Json SlideWithHighlight(const Json& src) const;
    void SaveSoon();
    bool ValidSelectionLocked() const; // índices válidos (lock ya tomado)

    mutable std::mutex m_;
    std::vector<MotorScenario> scenarios_;
    int scnIdx_ = -1, elIdx_ = -1, lineIdx_ = 0;
    std::string blank_ = "black";      // arranque en reposo [SPEC §6.1.3]
    std::string advance_ = "line";
    std::vector<std::string> highlight_;
    bool standaloneKeys_ = false;
    std::wstring persistFile_;
    std::string lastShownId_;          // para hot-reload sin transición espuria
};

} // namespace fusion
