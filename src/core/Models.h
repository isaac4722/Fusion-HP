// ============================================================================
//  LuminaPresentation Suite
//  Copyright (c) 2026 Isaac. Todos los derechos reservados.
//  Licencia de Solo Lectura (View-Only) - ver LICENSE.md
// ============================================================================
//  Models.h : Estructuras de datos compartidas (Canciones, Slides, Temas)
//  y serializacion JSON de temas (Plantillas Maestras desacopladas).
// ============================================================================
#ifndef LUMINA_MODELS_H
#define LUMINA_MODELS_H

#include <QString>
#include <QStringList>
#include <QColor>
#include <QJsonObject>
#include <QJsonArray>
#include <QRectF>
#include <QVector>
#include <QMetaType>

// ---------------------------------------------------------------------------
// Estilo de texto renderizable por el Renderer
// ---------------------------------------------------------------------------
struct TextStyle
{
    QString family      = QStringLiteral("Arial");
    int     pointSize   = 48;
    bool    bold        = true;
    bool    italic      = false;
    bool    underline   = false;
    QColor  color       = QColor(255, 255, 255);
    QColor  outlineColor= QColor(0, 0, 0);
    int     outlineWidth= 0;            // 0 = sin contorno
    bool    shadow      = true;
    QColor  shadowColor = QColor(0, 0, 0, 160);
    int     shadowOffset= 4;
    int     align       = 0;            // 0=center 1=left 2=right
    bool    upperCase   = false;
    int     letterSpacing = 0;

    QJsonObject toJson() const;
    static TextStyle fromJson(const QJsonObject &o);
};

// ---------------------------------------------------------------------------
// Estilo de fondo del tema
// ---------------------------------------------------------------------------
struct BackgroundStyle
{
    int     type        = 0;    // 0=Color solido, 1=Gradiente, 2=Imagen, 3=Video
    QColor  color1      = QColor(8, 16, 40);
    QColor  color2      = QColor(24, 60, 130);
    int     gradientAngle = 90;
    QString imagePath;
    QString videoPath;
    int     imageFit    = 0;    // 0=Cover (llenar), 1=Contain (ajustar)

    QJsonObject toJson() const;
    static BackgroundStyle fromJson(const QJsonObject &o);
};

// ---------------------------------------------------------------------------
// Tema / Plantilla Maestra (desacoplado del contenido)
// ---------------------------------------------------------------------------
struct Theme
{
    int             id = 0;
    QString         name = QStringLiteral("Predeterminado");
    BackgroundStyle background;
    TextStyle       title;
    TextStyle       body;
    // Cajas normalizadas 0..1 respecto al lienzo 1920x1080
    QRectF          titleBox = QRectF(0.08, 0.05, 0.84, 0.18);
    QRectF          bodyBox  = QRectF(0.08, 0.28, 0.84, 0.60);

    // Estilo del Stage View (monitor de musicos, alto contraste)
    QColor stageBg    = QColor(0, 0, 0);
    QColor stageText  = QColor(255, 255, 255);
    QColor stageChord = QColor(120, 220, 120);
    QColor stageNext  = QColor(160, 160, 160);

    QJsonObject toJson() const;
    static Theme fromJson(const QJsonObject &o);
    static Theme defaultTheme();
};

// ---------------------------------------------------------------------------
// Linea de slide: texto + acordes (opcionales) alineados encima
// ---------------------------------------------------------------------------
struct SlideLine
{
    QString text;
    QString chords;     // linea de cifras/acordes encima del texto
    SlideLine() = default;
    SlideLine(const QString &t, const QString &c = QString()) : text(t), chords(c) {}
};

// ---------------------------------------------------------------------------
// Slide generica (unidad de proyeccion)
// ---------------------------------------------------------------------------
struct Slide
{
    enum Kind { Title, Text, Bible, Image, Video, Blank, Custom, Pptx, Aviso };

    Kind        kind = Text;
    QString     title;          // encabezado opcional (titulo cancion, referencia biblica)
    QVector<SlideLine> lines;   // contenido principal
    QString     notes;          // notas privadas para stage view
    QString     refLabel;       // etiqueta de referencia (ej: "Jn 3:16")
    QString     mediaPath;      // para Image/Video/Custom(JSON)/Pptx(imagen extraida)
    QString     ref;            // referencia interna DB (ej: "song:12")

    int lineCount() const { return lines.size(); }
};

// ---------------------------------------------------------------------------
// Cancion
// ---------------------------------------------------------------------------
struct Song
{
    int     id = 0;
    QString title;
    QString artist;
    QString key;            // tonalidad musical (ej: "Do", "G")
    int     bpm = 0;
    QString lyrics;         // letra cruda con etiquetas [Verso]/[Coro] y acordes
    QString updatedAt;
};

// ---------------------------------------------------------------------------
// Item de culto / playlist
// ---------------------------------------------------------------------------
struct ServiceItem
{
    enum Kind { Song = 0, Bible, Pptx, Image, Video, Custom, Aviso };
    int     id = 0;
    int     kind = Song;
    int     refId = 0;      // id en su tabla (song id, custom slide id, ...)
    QString label;          // titulo legible
    QString payload;        // JSON extra (ej. referencia biblica, ruta de medios)
};

// ---------------------------------------------------------------------------
// Resultado de busqueda FTS de cancion (fila ligera para listas)
// ---------------------------------------------------------------------------
struct SongRow
{
    int id = 0;
    QString title;
    QString artist;
    QString key;
    int bpm = 0;
};

// ---------------------------------------------------------------------------
// Serializacion JSON
// ---------------------------------------------------------------------------
inline Theme Theme::defaultTheme()
{
    Theme t;
    t.name = QStringLiteral("Clásico Azul");
    t.background.type = 1;                  // gradiente
    t.background.color1 = QColor(8, 16, 40);
    t.background.color2 = QColor(24, 60, 130);
    t.background.gradientAngle = 90;
    t.title = TextStyle();
    t.title.family = QStringLiteral("Arial");
    t.title.pointSize = 40;
    t.title.color = QColor(255, 231, 160);
    t.title.outlineWidth = 2;
    t.body = TextStyle();
    t.body.family = QStringLiteral("Arial");
    t.body.pointSize = 54;
    t.body.color = Qt::white;
    t.body.outlineWidth = 2;
    t.body.shadow = true;
    return t;
}

inline QJsonObject TextStyle::toJson() const
{
    QJsonObject o;
    o["family"] = family;  o["pointSize"] = pointSize;
    o["bold"] = bold; o["italic"] = italic; o["underline"] = underline;
    o["color"] = color.name(QColor::HexArgb);
    o["outlineColor"] = outlineColor.name(QColor::HexArgb);
    o["outlineWidth"] = outlineWidth;
    o["shadow"] = shadow; o["shadowColor"] = shadowColor.name(QColor::HexArgb);
    o["shadowOffset"] = shadowOffset;
    o["align"] = align; o["upperCase"] = upperCase; o["letterSpacing"] = letterSpacing;
    return o;
}

inline TextStyle TextStyle::fromJson(const QJsonObject &o)
{
    TextStyle s;
    s.family = o.value("family").toString(s.family);
    s.pointSize = o.value("pointSize").toInt(s.pointSize);
    s.bold = o.value("bold").toBool(s.bold);
    s.italic = o.value("italic").toBool(s.italic);
    s.underline = o.value("underline").toBool(s.underline);
    s.color = QColor(o.value("color").toString(s.color.name(QColor::HexArgb)));
    s.outlineColor = QColor(o.value("outlineColor").toString(s.outlineColor.name(QColor::HexArgb)));
    s.outlineWidth = o.value("outlineWidth").toInt(s.outlineWidth);
    s.shadow = o.value("shadow").toBool(s.shadow);
    s.shadowColor = QColor(o.value("shadowColor").toString(s.shadowColor.name(QColor::HexArgb)));
    s.shadowOffset = o.value("shadowOffset").toInt(s.shadowOffset);
    s.align = o.value("align").toInt(s.align);
    s.upperCase = o.value("upperCase").toBool(s.upperCase);
    s.letterSpacing = o.value("letterSpacing").toInt(s.letterSpacing);
    return s;
}

inline QJsonObject BackgroundStyle::toJson() const
{
    QJsonObject o;
    o["type"] = type;
    o["color1"] = color1.name(QColor::HexArgb);
    o["color2"] = color2.name(QColor::HexArgb);
    o["gradientAngle"] = gradientAngle;
    o["imagePath"] = imagePath;
    o["videoPath"] = videoPath;
    o["imageFit"] = imageFit;
    return o;
}

inline BackgroundStyle BackgroundStyle::fromJson(const QJsonObject &o)
{
    BackgroundStyle b;
    b.type = o.value("type").toInt(b.type);
    b.color1 = QColor(o.value("color1").toString(b.color1.name(QColor::HexArgb)));
    b.color2 = QColor(o.value("color2").toString(b.color2.name(QColor::HexArgb)));
    b.gradientAngle = o.value("gradientAngle").toInt(b.gradientAngle);
    b.imagePath = o.value("imagePath").toString();
    b.videoPath = o.value("videoPath").toString();
    b.imageFit = o.value("imageFit").toInt(b.imageFit);
    return b;
}

inline QJsonObject Theme::toJson() const
{
    QJsonObject o;
    o["id"] = id;
    o["name"] = name;
    o["background"] = background.toJson();
    o["title"] = title.toJson();
    o["body"] = body.toJson();
    o["titleBox"] = QJsonArray{ titleBox.x(), titleBox.y(), titleBox.width(), titleBox.height() };
    o["bodyBox"] = QJsonArray{ bodyBox.x(), bodyBox.y(), bodyBox.width(), bodyBox.height() };
    o["stageBg"] = stageBg.name(QColor::HexArgb);
    o["stageText"] = stageText.name(QColor::HexArgb);
    o["stageChord"] = stageChord.name(QColor::HexArgb);
    o["stageNext"] = stageNext.name(QColor::HexArgb);
    return o;
}

inline Theme Theme::fromJson(const QJsonObject &o)
{
    Theme t = defaultTheme();
    t.id = o.value("id").toInt(0);
    t.name = o.value("name").toString(t.name);
    t.background = BackgroundStyle::fromJson(o.value("background").toObject());
    t.title = TextStyle::fromJson(o.value("title").toObject());
    t.body = TextStyle::fromJson(o.value("body").toObject());
    QJsonArray tb = o.value("titleBox").toArray();
    if (tb.size() == 4) t.titleBox = QRectF(tb.at(0).toDouble(), tb.at(1).toDouble(),
                                            tb.at(2).toDouble(), tb.at(3).toDouble());
    QJsonArray bb = o.value("bodyBox").toArray();
    if (bb.size() == 4) t.bodyBox = QRectF(bb.at(0).toDouble(), bb.at(1).toDouble(),
                                           bb.at(2).toDouble(), bb.at(3).toDouble());
    t.stageBg = QColor(o.value("stageBg").toString(t.stageBg.name(QColor::HexArgb)));
    t.stageText = QColor(o.value("stageText").toString(t.stageText.name(QColor::HexArgb)));
    t.stageChord = QColor(o.value("stageChord").toString(t.stageChord.name(QColor::HexArgb)));
    t.stageNext = QColor(o.value("stageNext").toString(t.stageNext.name(QColor::HexArgb)));
    return t;
}

// ---------------------------------------------------------------------------
// Metatypes para uso con QVariant (cola de culto en QListWidget)
// ---------------------------------------------------------------------------
Q_DECLARE_METATYPE(ServiceItem)

#endif // LUMINA_MODELS_H
