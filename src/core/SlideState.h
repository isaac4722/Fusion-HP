// ============================================================================
//  Fusion-HP · SlideState — contrato de render resuelto (lo que el núcleo dibuja).
//  La capa C# resuelve la herencia de 4 niveles [SPEC §5.4] y envía el estado
//  RESUELTO; el núcleo solo renderiza. Perfil C: el propio núcleo resuelve un
//  subconjunto al abrir un Escenario empaquetado [SPEC §4.2].
//  Cambios de línea NUNCA re-renderizan el fondo [SPEC §6.2].
// ============================================================================
#pragma once
#include "Common.h"
#include <mutex>

namespace fusion {

enum class SlideKind { None = 0, Text, Image, Video, Verse, Lower3 };
enum class BlankMode { None = 0, Black, Logo, Theme, Clear };
// Clear = ocultar el texto y conservar el fondo (tecla C, referencia web)

struct TextStyle {
    std::wstring font = L"Segoe UI";
    double size = 44.0;             // pt
    bool bold = false, italic = false;
    uint32_t color = 0xFFFFFFFF;    // 0xAARRGGBB
    uint32_t activeColor = 0xFFFFD700; // color de línea activa [SPEC §6.2.3]
    int align = 0;                  // 0 izq 1 centro 2 der
    int vAlign = 1;                 // 0 arriba 1 medio
    bool shadow = true, outline = false;
    double lineSpacing = 1.15;
    double x = 0.05, y = 0.08, w = 0.90, h = 0.84;  // caja en fracciones de pantalla
};

struct BackgroundStyle {
    uint32_t color = 0xFF000000;
    std::wstring image;             // ruta absoluta (vacío = solo color)
    int fit = 0;                    // 0 llenar 1 ajustar
    double opacity = 1.0;
};

struct MediaSpec {
    std::wstring src;
    bool loop = false;
    int volume = 100;               // 0-100 [SPEC §5.2]
    double startAt = 0.0;           // segundos
};

struct OverlaySpec {                 // Lower Third [SPEC §5.2 #5]
    bool present = false;
    std::vector<std::wstring> lines;
    TextStyle style;
    int position = 0;                // 0 abajo 1 arriba
    double duration = 0.0;          // 0 = manual
};

struct Slide {
    std::string id;                 // ID estable del elemento [SPEC §5.3c]
    SlideKind kind = SlideKind::None;
    std::vector<std::wstring> lines;
    int activeLine = 0;
    std::wstring reference;         // cita (Versículo)
    std::vector<std::wstring> highlight;   // palabras a resaltar [SPEC §5.2 #2]
    std::string transition;         // "" | cut | fade | slide
    TextStyle style;
    BackgroundStyle bg;
    MediaSpec media;
    OverlaySpec overlay;
    uint64_t stamp = 0;             // versión incremental del estado
};

// Estado completo del proyector, protegido por mutex y conmutado atómicamente
// (la conmutación es intercambio de puntero: transición ≤16 ms [SPEC §6.3]).
class SlideState {
public:
    void Set(const Slide& s) {
        std::lock_guard<std::mutex> lk(m_);
        slide_ = s;
        stamp_++;
    }
    void SetActiveLine(int n) {
        std::lock_guard<std::mutex> lk(m_);
        if (n >= 0 && n < (int)slide_.lines.size()) { slide_.activeLine = n; stamp_++; }
    }
    void SetBlank(BlankMode b) { std::lock_guard<std::mutex> lk(m_); blank_ = b; stamp_++; }
    Slide Get() const { std::lock_guard<std::mutex> lk(m_); return slide_; }
    BlankMode Blank() const { std::lock_guard<std::mutex> lk(m_); return blank_; }
    uint64_t Stamp() const { return stamp_.load(); }

    // Parseo desde JSON ipc.v1 (comando "show"/"line"/"blank")
    static bool ParseSlide(const Json& j, Slide& out);
    static SlideKind KindFromString(const std::string& k);
    static void ParseStyle(const Json& j, TextStyle& st);
    static void ParseBg(const Json& j, BackgroundStyle& bg);

    Json StateJson() const {
        std::lock_guard<std::mutex> lk(m_);
        Json j = Json::object();
        j["id"] = slide_.id;
        j["kind"] = (int)slide_.kind;
        j["activeLine"] = slide_.activeLine;
        j["lineCount"] = slide_.lines.size();
        j["reference"] = ToUtf8(slide_.reference);
        j["blank"] = (int)blank_;
        j["stamp"] = (uint64_t)stamp_;
        return j;
    }

private:
    mutable std::mutex m_;
    Slide slide_;
    BlankMode blank_ = BlankMode::Black;   // arranque: pantalla de reposo [SPEC §6.1.3]
    std::atomic<uint64_t> stamp_{1};
};

} // namespace fusion
