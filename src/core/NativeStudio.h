// ============================================================================
//  Fusion-HP · NativeStudio — la GUI/UX/UI de la versión web, replicada
//  100 % en C++ nativo (Win32 + GDI+, sin navegador ni capas externas).
//  [v2.3, petición del usuario: "la GUI de la web es a replicar con C++ y C#"]
//
//  Modos de la referencia web (App.tsx / PowerStudio / PresentMode / RemoteView):
//   · Start   — pantalla de inicio estilo PowerPoint (barra lateral roja +
//              tarjetas de presentaciones + buscador).
//   · Editor  — PowerStudio: barra de título con acceso rápido, cinta de 8
//              pestañas (Archivo=Backstage), miniaturas con secciones, lienzo
//              con edición directa + notas, panel Formato/Biblioteca y barra
//              de estado con vistas Normal/Clasificador.
//   · Present — consola del presentador: cabecera EN VIVO + reloj, programa
//              con sub-líneas, vista previa + A continuación, Pausas B/C/L,
//              transporte con avance conmutable y Biblia rápida (G).
//   · Mando   — réplica del control remoto móvil (RemoteView de la web).
//
//  El estudio ES una vista/controlador desechable del Motor (v2.2): navega
//  con Motor::Goto/Next/... y refleja el estado. Sin este estudio (p. ej. con
//  la GUI C#), el Motor sigue proyectando igual.
// ============================================================================
#pragma once
#include "Common.h"
#include "UiTheme.h"
#include "NativeLibrary.h"
#include <map>

namespace fusion {

class LiveWindow;
class VideoPlayer;
class Motor;
class SlideState;

class NativeStudio {
public:
    NativeStudio(SlideState* state, LiveWindow* live, VideoPlayer* video, Motor* motor);
    ~NativeStudio();

    bool Create();
    void Tick();                       // reloj + reconciliación con el Motor (500 ms)

private:
    // ------------------------------------------------------------ modos
    enum class Mode { Start, Editor, Present, Mando };
    enum RibbonTab { TAB_ARCHIVO = 0, TAB_INICIO, TAB_INSERTAR, TAB_DISENO,
                     TAB_TRANSICIONES, TAB_ANIMACIONES, TAB_PRESENTACION, TAB_VISTA,
                     TAB_COUNT };
    enum LibTab { LIB_CANTOS = 0, LIB_BIBLIA, LIB_MEDIOS, LIB_TEMAS, LIB_LISTA };
    enum BackPage { BP_INFO = 0, BP_NUEVO, BP_ABRIR, BP_EXPORTAR, BP_OPCIONES };

    // ------------------------------------------------------------ ventana
    static LRESULT CALLBACK WndProc(HWND, UINT, WPARAM, LPARAM);
    LRESULT Handle(UINT, WPARAM, LPARAM);
    void Paint();
    void Repaint();
    void HitAdd(int id, int a, int b, const RECT& r);
    HBITMAP CreateCompatible32(int w, int h);
    void LoadRecientes();
    void PushReciente(const std::wstring& path, const std::wstring& name);
    void SyncEditsFromDraft();          // EDITs del lienzo ← borrador
    void OnWheel(POINT p, int dz);      // rueda → región bajo el cursor

    // ------------------------------------------------------------ pintado por modo
    void PaintStart(Gdiplus::Graphics& g, const RECT& cli);
    void PaintEditor(Gdiplus::Graphics& g, const RECT& cli);
    void PaintPresent(Gdiplus::Graphics& g, const RECT& cli);
    void PaintMando(Gdiplus::Graphics& g, const RECT& cli);
    void PaintModal(Gdiplus::Graphics& g, const RECT& cli);   // ? / Opciones

    // ediciones auxiliares de pintado
    void PaintHeader(Gdiplus::Graphics& g, const RECT& r);
    void PaintRibbon(Gdiplus::Graphics& g, const RECT& r);
    void PaintBackstage(Gdiplus::Graphics& g, const RECT& r);
    void PaintThumbs(Gdiplus::Graphics& g, const RECT& r);
    void PaintCanvas(Gdiplus::Graphics& g, const RECT& r);
    void PaintSorter(Gdiplus::Graphics& g, const RECT& r);
    void PaintTaskPane(Gdiplus::Graphics& g, const RECT& r);
    void PaintStatus(Gdiplus::Graphics& g, const RECT& r);
    void PaintLibrary(Gdiplus::Graphics& g, const RECT& r);   // dentro del task pane

    // ------------------------------------------------------------ entrada
    void OnCommand(int id);
    bool OnKey(UINT vk, bool down, bool ctrl, bool shift);
    void OnEditChanged(int editId);
    void PushUndo();
    void DoUndo();
    void DoRedo();

    // ------------------------------------------------------------ acciones
    void GoMode(Mode m);
    void Present();                    // F5: borrador → motor.load
    void OpenAhp();
    void SaveAhp();
    void AddTitle();
    void AddBlankSlide();
    void AddLowerThird();
    void InsertSong(int songIdx);
    void InsertVerseRange();
    void InsertImage(const BgDef& b);
    void ApplyThemeToSel(const std::string& themeId);
    void ApplyBgToSel(const std::string& bgId);
    void RemoveItem(int i);
    void MoveItem(int i, int dir);
    void DeleteSelSlides();
    void DuplicateSelSlides();
    void CopySelSlides();
    void PasteSlides();
    void ExportPngCurrent();
    void ExportPngAll();
    void ExportCsv();

    // Motor (espejo optimista — igual que la GUI C#)
    void MotorGoto(int scn, int el, int line);
    void MotorNext();
    void MotorPrev();
    void MotorBlank(const std::string& mode);
    void SyncFromMotor();
    Json CurrentSlideJson() const;

    // miniaturas con caché
    Gdiplus::Bitmap* ThumbOf(const Json& slide, int w, int h, const std::string& key);
    void ClearThumbs();

    // utilidades
    DraftItem* SelItem();
    DraftSlide* SelSlide();
    int SlideTotalCount() const;
    int SlideGlobalIndex() const;
    std::wstring ClockText() const;

    // ------------------------------------------------------------ estado
    SlideState* state_;
    LiveWindow* live_;
    VideoPlayer* video_;
    Motor* motor_;

    HWND hwnd_ = nullptr;
    HWND edtName_ = nullptr, edtSearch_ = nullptr, edtLib_ = nullptr;
    HWND edtTitle_ = nullptr, edtSub_ = nullptr, edtLines_ = nullptr, edtNotes_ = nullptr;
    HWND edtBible_ = nullptr, edtDesc_ = nullptr;
    HFONT editFont_ = nullptr;

    // hits (regiones interactivas del modo actual; se recolectan al pintar)
    struct Hit { int id; int a = 0, b = 0; RECT r; };
    std::vector<Hit> hits_;
    int hoverId_ = 0, pressId_ = 0, hoverA_ = 0, hoverB_ = 0;   // payload del hover
    int pressA_ = 0, pressB_ = 0;                               // payload del presionado
    const Hit* HitAt(POINT p) const;
    void PlaceEdit(HWND ed, const RECT& r, bool show);

    // regiones desplazables (se recolectan al pintar; la rueda las consulta)
    struct Scrollable { RECT r; int* off; int max; };
    std::vector<Scrollable> scrolls_;
    void ScrollAdd(const RECT& r, int& off, int max);
    int scThumbs_ = 0, scLib_ = 0, scProgram_ = 0, scMando_ = 0,
        scStart_ = 0, scSorter_ = 0, scVerses_ = 0, scRecent_ = 0;

    // modo/paneles
    Mode mode_ = Mode::Start;
    int ribbonTab_ = TAB_INICIO;
    bool taskOpen_ = true, paneOpen_ = true, notesOpen_ = true;
    bool viewSorter_ = false;
    int libTab_ = LIB_CANTOS;
    int taskTab_ = 0;                  // 0 Formato 1 Biblioteca
    int backPage_ = BP_INFO;
    bool backstage_ = false;           // pestaña Archivo activa
    bool shortcutsOpen_ = false, optionsOpen_ = false;

    // datos
    Draft draft_;
    std::vector<Draft> undo_, redo_;
    SongLibrary songs_;
    BibleLibrary bibles_;
    History history_;
    struct Reciente { long long t; std::wstring path, name; };
    std::vector<Reciente> recientes_;            // recientes.jsonl
    std::wstring startQuery_, libQuery_, bibleQuery_;
    int bibleBook_ = -1, bibleChapter_ = 23;      // Salmos 23 por defecto (web)
    bool bibleAsLt_ = false;
    std::vector<int> songHits_;                   // resultado de búsqueda actual
    std::vector<int> verseNums_;                  // versículos listados (biblia rápida)
    std::wstring ahpPath_;                        // archivo abierto/guardado

    // espejo del Motor
    int curScn_ = -1, curEl_ = -1, curLine_ = 0, curLineCount_ = 0;
    std::string blank_ = "black", advance_ = "line";
    std::vector<std::pair<std::string, std::string>> progTitles_;   // (title, kind)
    Json motorSnapshot_;

    // present extra
    bool showClock_ = true, animate_ = true;

    // offscreen
    HBITMAP memBmp_ = nullptr;
    HDC memDc_ = nullptr;
    int memW_ = 0, memH_ = 0;

    // caché de miniaturas (id → bitmap), con límite
    std::map<std::string, Gdiplus::Bitmap*> thumbs_;
    size_t thumbSeq_ = 0;

    UINT_PTR timer_ = 0;
    long long lastHistoryEl_ = 0;
};

} // namespace fusion
