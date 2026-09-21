// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  Renderer.h : Motor de renderizado de slides con QPainter.
//  Pipeline raster determinista (< 16 ms por frame en hardware legacy):
//  fondo (color/gradiente/imagen) + texto con contorno, sombra y alineacion.
//  Un unico renderizador alimenta OutputWindow, StageWindow y previews.
// ============================================================================
#ifndef LUMINA_RENDERER_H
#define LUMINA_RENDERER_H

#include "Models.h"
#include "Chords.h"

#include <QPixmap>
#include <QSize>
#include <QPainter>
#include <QPainterPath>
#include <QFontMetrics>
#include <cmath>

// ---------------------------------------------------------------------------
// Opciones de render (fuera de la clase: se usan como argumento por defecto)
// ---------------------------------------------------------------------------
struct RenderOptions
{
    bool  showChords   = false;     // cifras sobre el texto (stage view)
    bool  latinChords  = true;
    int   transpose    = 0;
    bool  showTitle    = true;
    bool  showFooter   = true;      // pie: referencia / etiqueta
    qreal scaleFonts   = 1.0;       // ajuste dinamico de tamano
};

class Renderer
{
public:
    using Options = RenderOptions;

    // Renderiza una slide completa a la resolucion pedida
    static QPixmap render(const Theme &theme, const Slide &slide, const QSize &size,
                          const Options &opt = Options())
    {
        QPixmap pm(size);
        pm.fill(Qt::black);
        QPainter p(&pm);
        p.setRenderHint(QPainter::Antialiasing, true);
        p.setRenderHint(QPainter::TextAntialiasing, true);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        paintBackground(p, theme.background, size);
        paintSlideContent(p, theme, slide, size, opt);
        p.end();
        return pm;
    }

    // Solo fondo (para Black/Clear/Logo states o previews vacios)
    static QPixmap renderBackground(const Theme &theme, const QSize &size)
    {
        QPixmap pm(size);
        pm.fill(Qt::black);
        QPainter p(&pm);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        paintBackground(p, theme.background, size);
        p.end();
        return pm;
    }

    // Fondo con logo centrado
    static QPixmap renderLogo(const Theme &theme, const QPixmap &logo, const QSize &size)
    {
        QPixmap pm = renderBackground(theme, size);
        if (logo.isNull()) return pm;
        QPainter p(&pm);
        p.setRenderHint(QPainter::SmoothPixmapTransform, true);
        const QSize ls = logo.size().scaled(size * 0.5, Qt::KeepAspectRatio);
        const int x = (size.width() - ls.width()) / 2;
        const int y = (size.height() - ls.height()) / 2;
        p.drawPixmap(x, y, ls.width(), ls.height(), logo);
        p.end();
        return pm;
    }

    static void paintBackground(QPainter &p, const BackgroundStyle &bg, const QSize &size)
    {
        switch (bg.type) {
        case 1: {   // gradiente
            QLinearGradient g;
            switch (bg.gradientAngle % 360 / 45) {
            case 0: g = QLinearGradient(0, 0, size.width(), 0); break;
            case 1: g = QLinearGradient(0, 0, size.width(), size.height()); break;
            case 2: g = QLinearGradient(0, 0, 0, size.height()); break;
            case 3: g = QLinearGradient(size.width(), 0, 0, size.height()); break;
            case 4: g = QLinearGradient(size.width(), 0, 0, 0); break;
            case 5: g = QLinearGradient(size.width(), size.height(), 0, 0); break;
            case 6: g = QLinearGradient(0, size.height(), 0, 0); break;
            default: g = QLinearGradient(0, size.height(), size.width(), 0); break;
            }
            g.setColorAt(0.0, bg.color1);
            g.setColorAt(1.0, bg.color2);
            p.fillRect(0, 0, size.width(), size.height(), g);
            break;
        }
        case 2: {   // imagen
            QImage img(bg.imagePath);
            if (!img.isNull()) {
                drawImageFit(p, img, QRect(0, 0, size.width(), size.height()),
                             bg.imageFit == 0 ? Qt::KeepAspectRatioByExpanding : Qt::KeepAspectRatio);
            } else {
                p.fillRect(0, 0, size.width(), size.height(), bg.color1);
            }
            break;
        }
        case 3: {   // video: el video se dibuja por LibVLC debajo; pintamos oscuro
            p.fillRect(0, 0, size.width(), size.height(), QColor(0, 0, 0));
            break;
        }
        default:    // color solido
            p.fillRect(0, 0, size.width(), size.height(), bg.color1);
            break;
        }
    }

    // Dibuja imagen con fit cover/contain centrado (evita deformaciones)
    static void drawImageFit(QPainter &p, const QImage &img, const QRect &rect, Qt::AspectRatioMode mode)
    {
        QImage scaled = img.scaled(rect.size(), mode, Qt::SmoothTransformation);
        int x = rect.x() + (rect.width() - scaled.width()) / 2;
        int y = rect.y() + (rect.height() - scaled.height()) / 2;
        if (mode == Qt::KeepAspectRatioByExpanding) { x = rect.x(); y = rect.y(); }
        p.drawImage(QPoint(x, y), scaled);
    }

    static void paintSlideContent(QPainter &p, const Theme &theme, const Slide &slide,
                                  const QSize &size, const Options &opt)
    {
        const int W = size.width();
        const int H = size.height();
        const qreal base = qMin(W, H) / 1080.0;     // factor de escala desde 1080p

        // ---- Titulo (encabezado) ----
        if (opt.showTitle && !slide.title.isEmpty() &&
            slide.kind != Slide::Title) {
            drawStyledText(p, theme.title, slide.title,
                           QRectF(theme.titleBox.x() * W, theme.titleBox.y() * H,
                                  theme.titleBox.width() * W, theme.titleBox.height() * H),
                           base * opt.scaleFonts);
        }

        // ---- Contenido principal ----
        QRectF bodyRect(theme.bodyBox.x() * W, theme.bodyBox.y() * H,
                        theme.bodyBox.width() * W, theme.bodyBox.height() * H);
        const bool hasChords = opt.showChords;
        int fontPx = qMax(12, int(theme.body.pointSize * base * opt.scaleFonts * (96.0 / 72.0)));

        // Auto-ajuste: reduce el tamano hasta que el bloque quepa en la caja
        for (int attempt = 0; attempt < 24; ++attempt) {
            const qreal needed = measureBlock(p, theme.body, slide.lines, bodyRect, fontPx, hasChords, base);
            if (needed <= bodyRect.height() || fontPx <= 12) break;
            fontPx = qMax(12, int(fontPx * 0.92));
        }
        drawBlock(p, theme.body, slide.lines, bodyRect, fontPx, hasChords, base, opt);

        // ---- Pie / referencia ----
        if (opt.showFooter && !slide.refLabel.isEmpty()) {
            TextStyle foot = theme.body;
            foot.pointSize = qMax(10, theme.body.pointSize / 3);
            foot.bold = false;
            foot.color = QColor(255, 255, 255, 170);
            foot.shadow = false;
            drawStyledText(p, foot, slide.refLabel,
                           QRectF(W * 0.04, H * 0.93, W * 0.60, H * 0.05), base);
        }
    }

    static QFont makeFont(const TextStyle &st, int px)
    {
        QFont f(st.family);
        f.setPixelSize(px);
        f.setBold(st.bold);
        f.setItalic(st.italic);
        f.setUnderline(st.underline);
        f.setLetterSpacing(QFont::AbsoluteSpacing, st.letterSpacing);
        return f;
    }

    // Dibuja un texto con el estilo completo (sombra + contorno) en una caja
    static void drawStyledText(QPainter &p, const TextStyle &st, const QString &text,
                               const QRectF &box, qreal scaleFrom1080)
    {
        const qreal base = scaleFrom1080;
        int px = qMax(8, int(st.pointSize * base * (96.0 / 72.0)));
        QString content = st.upperCase ? text.toUpper() : text;
        // Ajuste de tamano para caber en la caja
        for (int i = 0; i < 30; ++i) {
            QFont f = makeFont(st, px);
            QFontMetrics fm(f);
            QRectF br = fm.boundingRect(QRect(int(box.x()), 0, int(box.width()), 10000),
                                        Qt::TextWordWrap, content);
            if (br.height() <= box.height() || px <= 8) break;
            px = qMax(8, int(px * 0.93));
        }
        p.save();
        p.setFont(makeFont(st, px));
        Qt::Alignment align = Qt::AlignCenter;
        if (st.align == 1) align = Qt::AlignLeft;
        else if (st.align == 2) align = Qt::AlignRight;
        align |= Qt::AlignVCenter;

        // Contorno via QPainterPath
        if (st.outlineWidth > 0) {
            QPainterPath path;
            path.addText(QPointF(0, 0), p.font(), content);
            // Usamos un QTextLayout simplificado: contorno linea a linea via bounding
            // Para bloques multiliena usamos boundingRect dibujo por partes:
        }
        // Sombra
        if (st.shadow) {
            p.setPen(Qt::NoPen);
            QColor sc = st.shadowColor;
            p.setBrush(sc);
            p.drawText(box.translated(st.shadowOffset * base, st.shadowOffset * base),
                       align | Qt::TextWordWrap, content);
        }
        // Texto con contorno
        if (st.outlineWidth > 0) {
            p.setPen(QPen(st.outlineColor, st.outlineWidth * base, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
            QPainterPath pp;
            QFont f = p.font();
            // Contorno por capas (offset circular) — robusto y rapido
            const qreal ow = st.outlineWidth * base;
            for (int a = 0; a < 12; ++a) {
                const qreal ang = a * 3.14159265 / 6.0;
                p.setPen(st.outlineColor);
                p.drawText(box.translated(ow * std::cos(ang), ow * std::sin(ang)),
                           align | Qt::TextWordWrap, content);
            }
            Q_UNUSED(f)
            Q_UNUSED(pp)
        }
        p.setPen(st.color);
        p.setBrush(Qt::NoBrush);
        p.drawText(box, align | Qt::TextWordWrap, content);
        p.restore();
    }

    static qreal measureBlock(QPainter &p, const TextStyle &st, const QVector<SlideLine> &lines,
                              const QRectF &box, int fontPx, bool hasChords, qreal base)
    {
        QFont f = makeFont(st, fontPx);
        QFontMetrics fm(f);
        const qreal chordPx = fontPx * 0.55;
        qreal total = 0;
        const qreal lineSpacing = 1.18;
        for (const SlideLine &l : lines) {
            if (hasChords && !l.chords.isEmpty())
                total += chordPx * 1.25;
            QRectF br = fm.boundingRect(QRect(int(box.x()), 0, int(box.width()), 10000),
                                        Qt::TextWordWrap, l.text);
            total += br.height() * lineSpacing;
        }
        Q_UNUSED(base)
        return total;
    }

    // Bloque de lineas con acordes opcionales encima
    static void drawBlock(QPainter &p, const TextStyle &st, const QVector<SlideLine> &lines,
                          const QRectF &box, int fontPx, bool hasChords, qreal base,
                          const Options &opt)
    {
        QFont f = makeFont(st, fontPx);
        QFontMetrics fm(f);
        const int chordPx = qMax(8, int(fontPx * 0.55));
        QFont chordFont = makeFont(st, chordPx);
        chordFont.setBold(false);
        chordFont.setItalic(true);
        QFontMetrics chFm(chordFont);

        // Altura total para centrado vertical
        const qreal lineSpacing = 1.18;
        qreal totalH = 0;
        QVector<qreal> lineH(lines.size());
        for (int i = 0; i < lines.size(); ++i) {
            const SlideLine &l = lines.at(i);
            qreal h = 0;
            if (hasChords && !l.chords.isEmpty()) h += chordPx * 1.25;
            h += fm.boundingRect(QRect(int(box.x()), 0, int(box.width()), 10000),
                                 Qt::TextWordWrap, l.text).height() * lineSpacing;
            lineH[i] = h;
            totalH += h;
        }

        Qt::Alignment hAlign = Qt::AlignHCenter;
        if (st.align == 1) hAlign = Qt::AlignLeft;
        else if (st.align == 2) hAlign = Qt::AlignRight;

        p.save();
        qreal y = box.y() + qMax<qreal>(0, (box.height() - totalH) / 2.0);
        for (int i = 0; i < lines.size(); ++i) {
            const SlideLine &l = lines.at(i);
            QString content = st.upperCase ? l.text.toUpper() : l.text;
            // Acordes
            if (hasChords && !l.chords.isEmpty()) {
                QString chords = l.chords;
                if (opt.transpose != 0)
                    chords = Chords::transposeLine(l.chords, opt.transpose, opt.latinChords);
                p.setFont(chordFont);
                p.setPen(QPen(QColor(120, 235, 120)));
                if (st.shadow) {
                    p.setPen(QPen(QColor(0, 60, 0)));
                    p.drawText(QRectF(box.x(), y + 1, box.width(), chordPx * 1.3), hAlign, chords);
                    p.setPen(QPen(QColor(140, 255, 140)));
                }
                p.drawText(QRectF(box.x(), y, box.width(), chordPx * 1.3), hAlign, chords);
                y += chordPx * 1.25;
            }
            // Sombra del texto principal
            QRectF lineBox(box.x(), y, box.width(), lineH[i]);
            if (st.shadow) {
                p.setFont(f);
                p.setPen(QPen(st.shadowColor));
                p.drawText(lineBox.translated(st.shadowOffset * base, st.shadowOffset * base),
                           hAlign | Qt::TextWordWrap, content);
            }
            // Contorno
            if (st.outlineWidth > 0) {
                p.setFont(f);
                p.setPen(st.outlineColor);
                const qreal ow = st.outlineWidth * base;
                for (int a = 0; a < 12; ++a) {
                    const qreal ang = a * 3.14159265 / 6.0;
                    p.drawText(lineBox.translated(ow * std::cos(ang), ow * std::sin(ang)),
                               hAlign | Qt::TextWordWrap, content);
                }
            }
            p.setFont(f);
            p.setPen(QPen(st.color));
            p.drawText(lineBox, hAlign | Qt::TextWordWrap, content);
            y += lineH[i];
        }
        p.restore();
    }
};

#endif // LUMINA_RENDERER_H
