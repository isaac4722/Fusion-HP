// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Types.h : Estructuras de datos compartidas (Canciones, Slides, Temas,
//  Cultos, Medios) y serializacion JSON (nlohmann) portada de la edicion Qt.
// ============================================================================
#ifndef LUMINA_TYPES_H
#define LUMINA_TYPES_H

#include <wx/string.h>
#include <wx/colour.h>
#include <wx/font.h>
#include <wx/gdicmn.h>

#include <vector>
#include <nlohmann/json.hpp>

using json = nlohmann::json;

// Conversiones UTF-8 explicitas (INDEPENDIENTES del locale del sistema):
// wxString::ToStdString()/wxString(std::string) usan wxConvLibc y corrompen
// el UTF-8 en locales no-UTF8 (p.ej. "C"), por lo que NUNCA se usan para JSON.
inline std::string ToUtf8(const wxString &s) { return std::string(s.utf8_str()); }
inline wxString FromUtf8(const std::string &s)
{
    return wxString::FromUTF8(s.data(), s.size());
}

// ---------------------------------------------------------------------------
// Utilidades de serializacion
// ---------------------------------------------------------------------------
inline json ColorToJson(const wxColour &c)
{
    if (!c.IsOk())
        return json("#FF000000");
    return json(wxString::Format("#%02X%02X%02X%02X", c.Red(), c.Green(), c.Blue(), c.Alpha()).ToStdString());
}

inline wxColour JsonToColor(const json &o, const wxColour &fallback)
{
    if (!o.is_string())
        return fallback;
    const wxString s(o.get<std::string>());
    long r = fallback.Red(), g = fallback.Green(), b = fallback.Blue(), a = fallback.Alpha();
    if (s.Length() >= 9 && s[0] == '#') {
        struct Pair { size_t off; long *dst; } pairs[4] = {
            { 1, &r }, { 3, &g }, { 5, &b }, { 7, &a }
        };
        for (auto &p : pairs) {
            long v = 0;
            if (s.Mid(p.off, 2).ToLong(&v, 16))
                *p.dst = v;
        }
    }
    return wxColour((int)r, (int)g, (int)b, (int)a);
}

inline wxColour FallbackBg1()   { return wxColour(10, 18, 44);  }
inline wxColour FallbackBg2()   { return wxColour(28, 64, 140); }

// ---------------------------------------------------------------------------
// Estilo de texto renderizable por el Renderer
// ---------------------------------------------------------------------------
struct TextStyle
{
    wxString family        = "Arial";
    int      pointSize     = 48;
    bool     bold          = true;
    bool     italic        = false;
    bool     underline     = false;
    wxColour color         = wxColour(255, 255, 255);
    wxColour outlineColor  = wxColour(0, 0, 0);
    int      outlineWidth  = 0;            // 0 = sin contorno
    bool     shadow        = true;
    wxColour shadowColor   = wxColour(0, 0, 0, 160);
    int      shadowOffset  = 4;
    int      align         = 0;            // 0=centro 1=izq 2=der
    bool     upperCase     = false;

    json toJson() const
    {
        return json{
            { "family",        family.ToStdString() },
            { "pointSize",     pointSize },
            { "bold",          bold },
            { "italic",        italic },
            { "underline",     underline },
            { "color",         ColorToJson(color) },
            { "outlineColor",  ColorToJson(outlineColor) },
            { "outlineWidth",  outlineWidth },
            { "shadow",        shadow },
            { "shadowColor",   ColorToJson(shadowColor) },
            { "shadowOffset",  shadowOffset },
            { "align",         align },
            { "upperCase",     upperCase },
        };
    }

    static TextStyle fromJson(const json &o)
    {
        TextStyle s;
        if (!o.is_object())
            return s;
        if (o.contains("family") && o["family"].is_string())
            s.family = FromUtf8(o["family"].get<std::string>());
        if (o.contains("pointSize")) s.pointSize     = o.value("pointSize", s.pointSize);
        if (o.contains("bold"))      s.bold          = o.value("bold", s.bold);
        if (o.contains("italic"))    s.italic        = o.value("italic", s.italic);
        if (o.contains("underline")) s.underline     = o.value("underline", s.underline);
        if (o.contains("color"))        s.color        = JsonToColor(o["color"], s.color);
        if (o.contains("outlineColor")) s.outlineColor = JsonToColor(o["outlineColor"], s.outlineColor);
        if (o.contains("outlineWidth")) s.outlineWidth = o.value("outlineWidth", s.outlineWidth);
        if (o.contains("shadow"))       s.shadow       = o.value("shadow", s.shadow);
        if (o.contains("shadowColor"))  s.shadowColor  = JsonToColor(o["shadowColor"], s.shadowColor);
        if (o.contains("shadowOffset")) s.shadowOffset = o.value("shadowOffset", s.shadowOffset);
        if (o.contains("align"))        s.align        = o.value("align", s.align);
        if (o.contains("upperCase"))    s.upperCase    = o.value("upperCase", s.upperCase);
        return s;
    }

    wxFont MakeFont(int pxSize) const
    {
        wxFontInfo fi(pxSize);
        fi.FaceName(family);
        if (bold)      fi.Bold();
        if (italic)    fi.Italic();
        if (underline) fi.Underlined();
        wxFont f(fi);
        if (!f.IsOk())
            f = wxFont(wxFontInfo(pxSize).Family(wxFONTFAMILY_SWISS));
        return f;
    }
};

// ---------------------------------------------------------------------------
// Estilo de fondo del tema
// ---------------------------------------------------------------------------
struct BackgroundStyle
{
    int      type           = 1;    // 0=Color solido, 1=Gradiente, 2=Imagen
    wxColour color1         = FallbackBg1();
    wxColour color2         = FallbackBg2();
    int      gradientAngle  = 90;
    wxString imagePath;
    int      imageFit       = 0;    // 0=Llenar (cover), 1=Ajustar (contain)

    json toJson() const
    {
        return json{
            { "type",          type },
            { "color1",        ColorToJson(color1) },
            { "color2",        ColorToJson(color2) },
            { "gradientAngle", gradientAngle },
            { "imagePath",     imagePath.ToStdString() },
            { "imageFit",      imageFit },
        };
    }

    static BackgroundStyle fromJson(const json &o)
    {
        BackgroundStyle b;
        if (!o.is_object())
            return b;
        if (o.contains("type"))          b.type          = o.value("type", b.type);
        if (o.contains("color1"))        b.color1        = JsonToColor(o["color1"], b.color1);
        if (o.contains("color2"))        b.color2        = JsonToColor(o["color2"], b.color2);
        if (o.contains("gradientAngle")) b.gradientAngle = o.value("gradientAngle", b.gradientAngle);
        if (o.contains("imagePath") && o["imagePath"].is_string())
            b.imagePath = FromUtf8(o["imagePath"].get<std::string>());
        if (o.contains("imageFit"))      b.imageFit      = o.value("imageFit", b.imageFit);
        return b;
    }
};

// ---------------------------------------------------------------------------
// Tema / Plantilla maestra (desacoplado del contenido)
// ---------------------------------------------------------------------------
struct Theme
{
    int             id = 0;
    wxString        name = "Predeterminado";
    BackgroundStyle background;
    TextStyle       title;
    TextStyle       body;

    json toJson() const
    {
        return json{
            { "id",         id },
            { "name",       name.ToStdString() },
            { "background", background.toJson() },
            { "title",      title.toJson() },
            { "body",       body.toJson() },
        };
    }

    static Theme fromJson(const json &o)
    {
        Theme t = DefaultTheme();
        if (!o.is_object())
            return t;
        if (o.contains("id"))   t.id   = o.value("id", 0);
        if (o.contains("name") && o["name"].is_string())
            t.name = FromUtf8(o["name"].get<std::string>());
        if (o.contains("background")) t.background = BackgroundStyle::fromJson(o["background"]);
        if (o.contains("title"))      t.title      = TextStyle::fromJson(o["title"]);
        if (o.contains("body"))       t.body       = TextStyle::fromJson(o["body"]);
        return t;
    }

    static Theme DefaultTheme()
    {
        Theme t;
        t.name = L"Clásico";
        t.background.type = 1;
        t.background.color1 = wxColour(10, 18, 44);
        t.background.color2 = wxColour(28, 64, 140);
        t.background.gradientAngle = 90;
        t.title.family = "Arial";
        t.title.pointSize = 40;
        t.title.color = wxColour(255, 231, 160);
        t.title.outlineWidth = 2;
        t.body.family = "Arial";
        t.body.pointSize = 54;
        t.body.color = wxColour(255, 255, 255);
        t.body.outlineWidth = 2;
        t.body.shadow = true;
        return t;
    }
};

// ---------------------------------------------------------------------------
// Linea de slide: texto + acordes (opcionales) alineados encima
// ---------------------------------------------------------------------------
struct SlideLine
{
    wxString text;
    wxString chords;     // cifrado encima del texto
    SlideLine() = default;
    SlideLine(const wxString &t, const wxString &c = wxString()) : text(t), chords(c) {}
};

// ---------------------------------------------------------------------------
// Slide generica (unidad de proyeccion)
// ---------------------------------------------------------------------------
struct Slide
{
    enum Kind { Title = 0, Text, Bible, Image, Video, Blank, Aviso };

    Kind               kind = Text;
    wxString           title;      // encabezado opcional
    wxString           refLabel;   // etiqueta (ej: "Jn 3:16", "[Coro]")
    std::vector<SlideLine> lines;
    wxString           mediaPath;  // para Image/Video
    wxString           notes;
};

// ---------------------------------------------------------------------------
// Cancion
// ---------------------------------------------------------------------------
struct Song
{
    int      id = 0;
    wxString title;
    wxString artist;
    wxString key;          // tonalidad musical (ej: "Do", "G")
    int      bpm = 0;
    wxString lyrics;       // letra cruda con [Verso]/[Coro] y acordes
    wxString tags;
    wxString updatedAt;
};

// ---------------------------------------------------------------------------
// Item de culto / playlist
// ---------------------------------------------------------------------------
struct ServiceItem
{
    enum Kind { Song = 0, Bible, Image, Video, Aviso };
    int      id = 0;
    int      kind = Song;
    int      refId = 0;     // id en su tabla
    wxString label;         // titulo legible
    wxString payload;       // JSON extra (referencia biblica, ruta de medios...)
};

// ---------------------------------------------------------------------------
// Fila ligera de medio (biblioteca de fondos)
// ---------------------------------------------------------------------------
struct MediaRow
{
    wxString path;
    int      kind = 0;      // 0 imagen, 1 video
    wxString tags;
};

// ---------------------------------------------------------------------------
// Tema de eventos internos de la aplicacion
// ---------------------------------------------------------------------------
enum AppAction
{
    ActionGoLiveSong = 1,     // id = song id
    ActionAddSongToService,   // id = song id
    ActionGoLiveRef,          // string = "book|chapter|verseFrom|verseTo"
    ActionAddRefToService,    // string = ref cruda
    ActionGoLiveMedia,        // string = ruta
    ActionAddMediaToService,  // string = ruta
    ActionApplyTheme,         // id = theme id
    ActionSongsChanged,       // refrescar combos/etiquetas
    ActionThemesChanged,
    ActionMediaChanged,
    ActionServiceChanged,     // id = indice a proyectar (o -1 solo refrescar)
    ActionQuickVerse,         // F9
};

#endif // LUMINA_TYPES_H
