// ============================================================================
//  Fusion-HP · UiTheme — sistema de diseño de la GUI web replicada en C++
//  Paleta Lumina/PowerStudio (referencia web), fuentes en caché, iconos Tabler
//  PNG (ink20/accent20/white20) y primitivas de dibujo de controles modelados:
//  chips (botones con radio 5, hover/pressed/foco), icon buttons 34 px con
//  estado activo acento, pestañas con subrayado, filas de lista, tarjetas.
//  NADA se crea por repintado: fuentes e iconos viven en caché global
//  (lección de studio-errors.log: fugas de handles GDI).
// ============================================================================
#pragma once
#include "Common.h"
#include <gdiplus.h>
#pragma comment(lib, "gdiplus.lib")

namespace fusion {
namespace ui {

// ---- Paleta de la referencia web ----
constexpr uint32_t Accent    = 0xFFC43E1C;   // #C43E1C botones primarios
constexpr uint32_t AccentDk  = 0xFF8A1F11;   // texto sobre fondo activo
constexpr uint32_t AccentBg  = 0xFFF3E9E4;   // fondo activo (hover acento)
constexpr uint32_t Ink       = 0xFF201F1E;   // texto principal
constexpr uint32_t Gray      = 0xFF605E5C;   // texto secundario
constexpr uint32_t GrayLt    = 0xFF6F6E6D;   // texto terciario
constexpr uint32_t Border    = 0xFFEDEBE9;   // separadores
constexpr uint32_t Border2   = 0xFFE1DFDD;   // bordes de controles
constexpr uint32_t CanvasBg  = 0xFFF3F2F1;   // fondo de página
constexpr uint32_t Paper     = 0xFFFFFFFF;   // tarjetas / paneles
constexpr uint32_t HoverBg   = 0xFFF3F2F1;   // hover neutro
constexpr uint32_t DangerBg  = 0xFFFDE7E9;
constexpr uint32_t DangerTx  = 0xFFA4262C;
constexpr uint32_t Gold      = 0xFFE8C872;   // línea activa (web #e8c872)
constexpr uint32_t LiveRed   = 0xFFD13438;   // badge EN VIVO

// ---- inicialización (una vez por proceso) ----
bool InitGdiplus();                 // token propio de la UI (Win7+)
void ShutdownGdiplus();

// ---- fuentes en caché (NUNCA crear en un paint) ----
HFONT Font(int px, int weight = FW_NORMAL, bool italic = false);

// ---- iconos Tabler (resources/img/icons/<set>/<name>.png) ----
enum IconTint { TintInk = 0, TintAccent = 1, TintWhite = 2 };
// Devuelve el bitmap en caché o nullptr si falta (la UI degrada, no falla).
Gdiplus::Bitmap* Icon(const std::string& name, IconTint tint = TintInk);
std::wstring IconRoot();            // <exe>/resources/img/icons

// ---- utilidades de color / geometría ----
Gdiplus::Color ToColor(uint32_t c);
inline COLORREF Cref(uint32_t c) {     // 0xAARRGGBB → COLORREF 0x00BBGGRR
    return RGB((c >> 16) & 0xFF, (c >> 8) & 0xFF, c & 0xFF);
}
void RoundRect(Gdiplus::Graphics& g, const RECT& r, int rad, Gdiplus::Color fill,
               Gdiplus::Color line = ToColor(0x00000000), float lineW = 0.0f);
SIZE Measure(HDC dc, HFONT f, const std::wstring& text, int maxW = 0);
std::wstring Elide(HDC dc, HFONT f, const std::wstring& text, int maxW);

// ---- controles modelados (dibujan sobre Graphics con doble buffer) ----
// Estados combinables por flags.
enum BtnState {
    kHot   = 1,      // hover
    kPress = 2,      // presionado
    kOn    = 4,      // activo (acento)
    kAcc   = 8,      // primario (relleno acento)
    kDis   = 16,     // deshabilitado
    kDanger= 32      // peligro (hover rojo)
};

// Chip estilo web (.ppt-chip / BigBtn): icono 16 + etiqueta (+sub).
void Chip(Gdiplus::Graphics& g, const RECT& r, const std::wstring& label,
          const std::wstring& sub, const std::string& icon, int state,
          int kbd = 0);
// Botón cuadrado 34x34 con icono 20 y estado activo acento (.ppt-iconbtn).
void IconButton(Gdiplus::Graphics& g, const RECT& r, const std::string& icon,
                int state, const std::wstring& tooltipId = std::wstring());
// Pestaña de cinta con subrayado acento cuando está activa.
void Tab(Gdiplus::Graphics& g, const RECT& r, const std::wstring& label, bool active,
         bool hot, bool isArchivo = false);
// Fila de lista con número, título y subtítulo (biblioteca/programa).
void Row(Gdiplus::Graphics& g, const RECT& r, int num, const std::wstring& title,
         const std::wstring& sub, const std::string& icon, int state);
// Tarjeta de presentación (inicio / escenarios) con cabecera oscura.
void Card(Gdiplus::Graphics& g, const RECT& r, const std::wstring& title,
          const std::wstring& sub, bool hot, bool accentBar = false);
// Píldora/badge (EN VIVO, conteos).
void Badge(Gdiplus::Graphics& g, const RECT& r, const std::wstring& text,
           uint32_t bg, uint32_t fg);
// Kbd chip (<kbd> de la web).
void Kbd(Gdiplus::Graphics& g, RECT r, const std::wstring& text);
// Grupo de cinta con etiqueta inferior.
void GroupLabel(Gdiplus::Graphics& g, const RECT& r, const std::wstring& label);
// Separador vertical de la cinta.
void Sep(Gdiplus::Graphics& g, const RECT& r);
// Caja de texto modelada (borde #C8C6C4 → acento con foco).
void Field(Gdiplus::Graphics& g, const RECT& r, bool focused, bool hot);

// ---- miniatura de diapositiva (SlideStage compact de la web) ----
// Compone fondo (color/imagen cacheada) + título + líneas (activa dorada) +
// referencia. w/h en píxeles; devuelve bitmap NUEVO (el llamador lo borra) o
// nullptr. Pensada para caché por id+línea del estudio.
Gdiplus::Bitmap* MakeThumb(const Json& slide, int w, int h);

} // namespace ui
} // namespace fusion
