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
#include <QTextDocument>
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

    // -----------------------------------------------------------------
    // v1.4.0 — Comprobador de accesibilidad (spec PowerPoint: WCAG).
    // Relativo de luminancia sRGB y ratio de contraste (WCAG 2.x):
    //   L = 0.2126R + 0.7152G + 0.0722B   (linealizado por canal)
    //   ratio = (Lmax + 0.05) / (Lmin + 0.05)   [1..21]
    // Umbral AA: >= 4.5 texto normal, >= 3.0 texto grande (>= 24pt o
    // >= 18pt negrita) — se clasifica como el Accessibility Checker de
    // PowerPoint: Error / Advertencia / Correcto.
    // -----------------------------------------------------------------
    static qreal srgbChannel(qreal c) noexcept
    {
        if (c <= 0.0) return 0.0;
        if (c >= 1.0) return 1.0;
        return (c <= 0.04045) ? (c / 12.92)
                              : std::pow((c + 0.055) / 1.055, 2.4);
    }

    static qreal relativeLuminance(const QColor &col) noexcept
    {
        const qreal r = srgbChannel(qreal(col.red())   / 255.0);
        const qreal g = srgbChannel(qreal(col.green()) / 255.0);
        const qreal b = srgbChannel(qreal(col.blue())  / 255.0);
        return 0.2126 * r + 0.7152 * g + 0.0722 * b;
    }

    static qreal contrastRatio(const QColor &a, const QColor &b) noexcept
    {
        const qreal la = relativeLuminance(a);
        const qreal lb = relativeLuminance(b);
        const qreal hi = qMax(la, lb), lo = qMin(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    enum class ContrastLevel { PassAA,        // >= 4.5 (texto normal AA)
                               PassLargeOnly, // >= 3.0: solo texto grande
                               Fail };        // < 3.0: error
    static ContrastLevel contrastLevel(qreal ratio, bool largeText) noexcept
    {
        if (ratio >= 4.5) return ContrastLevel::PassAA;
        if (ratio >= 3.0) return largeText ? ContrastLevel::PassLargeOnly
                                           : ContrastLevel::Fail;
        return ContrastLevel::Fail;
    }

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
        // CORRECCION v1.2.0: "QSize * qreal" NO existe en Qt 5.15 (llego en
        // Qt 6); con double 0.5 GCC resolvía la ambigüedad convirtiendo a
        // int(0.5)=0 -> el logo se escalaba a 0x0 y jamás se proyectaba.
        const QSize ls = logo.size().scaled(QSize(size.width() / 2, size.height() / 2),
                                             Qt::KeepAspectRatio);
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
        // CORRECCION v1.2.0: en modo "cover" (ByExpanding) la imagen escalada
        // EXCEDE el rect en una dimension -> el centrado produce offsets
        // negativos, lo cual es seguro (QPainter recorta al destino). Antes se
        // forzaba x/y al origen del rect y los fondos "cover" quedaban
        // anclados arriba-izquierda (recorte descentrado de videos/fotos).
        const int x = rect.x() + (rect.width() - scaled.width()) / 2;
        const int y = rect.y() + (rect.height() - scaled.height()) / 2;
        p.drawImage(QPoint(x, y), scaled);
    }

    static void paintSlideContent(QPainter &p, const Theme &theme, const Slide &slide,
                                  const QSize &size, const Options &opt)
    {
        // v1.2.0: slides de imagen (lienzo libre rasterizado, fotos, PNGs de
        // PPTX): imagen a pantalla completa + pie de referencia. Antes este
        // tipo de slide solo lo dibujaba OutputWindow (el preview del dock y
        // la exportación PDF/PPTX mostraban solo el fondo).
        if (slide.kind == Slide::Image && !slide.mediaPath.isEmpty()) {
            QImage img(slide.mediaPath);
            if (!img.isNull())
                drawImageFit(p, img, QRect(0, 0, size.width(), size.height()),
                             Qt::KeepAspectRatioByExpanding);
        }

        // v1.1.0: superposición de Lower Third (spec Componente 2: elemento
        // de texto semitransparente sobre fondo/imagen para citulos/títulos,
        // típico de transmisiones en vivo).
        if (slide.kind == Slide::LowerThird) {
            paintLowerThird(p, theme, slide, size);
            return;
        }

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
            const qreal needed = measureBlock(p, theme.body, slide.lines, bodyRect, fontPx, hasChords, base,
                                              slide.highlight);
            if (needed <= bodyRect.height() || fontPx <= 12) break;
            fontPx = qMax(12, int(fontPx * 0.92));
        }
        drawBlock(p, theme.body, slide.lines, bodyRect, fontPx, hasChords, base, opt, slide.highlight);

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

    // ------------------------------------------------------------------
    // v1.1.0: utilidades de resaltado de palabras (spec Holyrics).
    // Comparacion caso-insensible y sin acentos; coincide palabras
    // completas (ignora puntuacion en los bordes).
    // ------------------------------------------------------------------
    static QString stripAccentsLocal(const QString &s)
    {
        const QString out = s.normalized(QString::NormalizationForm_D);
        QString result;
        result.reserve(out.size());
        for (const QChar &c : out)
            if (c.category() != QChar::Mark_NonSpacing) result += c;
        return result;
    }

    static QString coreWord(const QString &w)
    {
        QString c = w;
        while (!c.isEmpty() && !c.front().isLetter()) c.remove(0, 1);
        while (!c.isEmpty() && !c.back().isLetter()) c.chop(1);
        return c;
    }

    static bool isHighlightWord(const QString &raw, const QStringList &normTargets)
    {
        const QString core = stripAccentsLocal(coreWord(raw));
        if (core.isEmpty()) return false;
        for (const QString &t : normTargets)
            if (core.compare(t, Qt::CaseInsensitive) == 0) return true;
        return false;
    }

    // Envuelve las palabras destacadas en HTML dorado/negrita.
    // 'escaped' ya viene con htmlEscape aplicado (sin tags propios).
    static QString highlightToHtml(const QString &escaped, const QString &highlight)
    {
        const QStringList targets = highlight.simplified().split(QChar(' '), Qt::SkipEmptyParts);
        if (targets.isEmpty()) return escaped;
        QStringList norm;
        norm.reserve(targets.size());
        for (const QString &t : targets) {
            const QString n = stripAccentsLocal(coreWord(t));
            if (!n.isEmpty()) norm << n;
        }
        if (norm.isEmpty()) return escaped;
        const QStringList parts = escaped.split(QChar(' '));
        QString out;
        out.reserve(escaped.size() + 32);
        for (int i = 0; i < parts.size(); ++i) {
            const QString raw = parts.at(i);
            if (isHighlightWord(raw, norm))
                out += QStringLiteral("<span style=\"color:#FFD54A; font-weight:800;\">%1</span>").arg(raw);
            else
                out += raw;
            if (i + 1 < parts.size()) out += QChar(' ');
        }
        return out;
    }

    static QString htmlEscape(const QString &s)
    {
        QString out = s;
        out.replace(QChar('&'), QStringLiteral("&amp;"));
        out.replace(QChar('<'), QStringLiteral("&lt;"));
        out.replace(QChar('>'), QStringLiteral("&gt;"));
        return out;
    }

    // Dibuja UNA linea con QTextDocument (permite HTML: palabras destacadas
    // en dorado). Mantiene sombra y alineacion del tema.
    static void drawRichLine(QPainter &p, const TextStyle &st, const QString &text,
                             const QRectF &lineBox, const QFont &f, Qt::Alignment hAlign,
                             const QString &highlight, qreal base)
    {
        const QString alignTag = (hAlign == Qt::AlignLeft) ? QStringLiteral("left")
                                 : (hAlign == Qt::AlignRight) ? QStringLiteral("right")
                                 : QStringLiteral("center");
        const QString bodyHtml = highlightToHtml(htmlEscape(st.upperCase ? text.toUpper() : text), highlight);

        auto makeDoc = [&f, &lineBox, &alignTag, &st](const QString &innerHtml, const QColor &color) {
            QTextDocument *doc = new QTextDocument();
            doc->setDefaultFont(f);
            doc->setDocumentMargin(0);
            doc->setHtml(QStringLiteral("<div align=\"%1\" style=\"color:%2;\">%3</div>")
                             .arg(alignTag, color.name(), innerHtml));
            doc->setTextWidth(lineBox.width());
            return doc;
        };

        QTextDocument *doc = makeDoc(bodyHtml, st.color);
        const qreal docH = doc->size().height();
        QPointF org = lineBox.topLeft();
        org.ry() += qMax<qreal>(0, (lineBox.height() - docH) / 2.0);
        if (st.shadow) {
            QTextDocument *sd = makeDoc(bodyHtml, QColor(0, 0, 0, 170));
            p.save();
            p.translate(org.x() + st.shadowOffset * base, org.y() + st.shadowOffset * base);
            sd->drawContents(&p);
            p.restore();
            delete sd;
        }
        p.save();
        p.translate(org);
        doc->drawContents(&p);
        p.restore();
        delete doc;
    }

    // Medida de una linea (usa QTextDocument si hay resaltado)
    static qreal measureLine(QPainter &p, const TextStyle &st, const SlideLine &l,
                             int fontPx, bool hasChords, const QRectF &box,
                             const QString &highlight, qreal lineSpacing)
    {
        QFont f = makeFont(st, fontPx);
        if (hasChords && !l.chords.isEmpty()) {
            // acordes + texto
            QFontMetrics fm(f);
            return fontPx * 0.55 * 1.25 + measureTextHeight(f, st, l.text, box, highlight, lineSpacing);
        }
        return measureTextHeight(f, st, l.text, box, highlight, lineSpacing);
    }

    static qreal measureTextHeight(const QFont &f, const TextStyle &st, const QString &text,
                                   const QRectF &box, const QString &highlight, qreal lineSpacing)
    {
        if (highlight.simplified().isEmpty()) {
            QFontMetrics fm(f);
            return fm.boundingRect(QRect(int(box.x()), 0, int(box.width()), 10000),
                                   Qt::TextWordWrap, text).height() * lineSpacing;
        }
        QTextDocument doc;
        doc.setDefaultFont(f);
        doc.setDocumentMargin(0);
        doc.setHtml(QStringLiteral("<div style=\"color:white;\">%1</div>")
                        .arg(highlightToHtml(htmlEscape(text), highlight)));
        doc.setTextWidth(box.width());
        return doc.size().height();
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

        // Sombra
        // CORRECCION v1.2.0: drawText() pinta con la PLUMA del painter; con
        // Qt::NoPen el texto de sombra no se dibujaba (sombras invisibles en
        // títulos/pies/Lower Third). El brush es irrelevante para drawText.
        if (st.shadow) {
            p.setPen(QPen(st.shadowColor));
            p.drawText(box.translated(st.shadowOffset * base, st.shadowOffset * base),
                       align | Qt::TextWordWrap, content);
        }
        // Contorno por capas (offset circular) — robusto y rapido
        if (st.outlineWidth > 0) {
            p.setPen(QPen(st.outlineColor, st.outlineWidth * base, Qt::SolidLine, Qt::RoundCap, Qt::RoundJoin));
            const qreal ow = st.outlineWidth * base;
            for (int a = 0; a < 12; ++a) {
                const qreal ang = a * 3.14159265 / 6.0;
                p.drawText(box.translated(ow * std::cos(ang), ow * std::sin(ang)),
                           align | Qt::TextWordWrap, content);
            }
        }
        p.setPen(st.color);
        p.setBrush(Qt::NoBrush);
        p.drawText(box, align | Qt::TextWordWrap, content);
        p.restore();
    }

    static qreal measureBlock(QPainter &p, const TextStyle &st, const QVector<SlideLine> &lines,
                              const QRectF &box, int fontPx, bool hasChords, qreal base,
                              const QString &highlight = QString())
    {
        QFont f = makeFont(st, fontPx);
        QFontMetrics fm(f);
        const qreal chordPx = fontPx * 0.55;
        qreal total = 0;
        const qreal lineSpacing = 1.18;
        Q_UNUSED(base)
        for (const SlideLine &l : lines)
            total += measureLine(p, st, l, fontPx, hasChords && !l.chords.isEmpty(), box, highlight, lineSpacing);
        return total;
    }

    // Bloque de lineas con acordes opcionales encima y resaltado HTML
    static void drawBlock(QPainter &p, const TextStyle &st, const QVector<SlideLine> &lines,
                          const QRectF &box, int fontPx, bool hasChords, qreal base,
                          const Options &opt, const QString &highlight = QString())
    {
        QFont f = makeFont(st, fontPx);
        QFontMetrics fm(f);
        const int chordPx = qMax(8, int(fontPx * 0.55));
        QFont chordFont = makeFont(st, chordPx);
        chordFont.setBold(false);
        chordFont.setItalic(true);
        const bool rich = !highlight.simplified().isEmpty();

        // Altura total para centrado vertical
        const qreal lineSpacing = 1.18;
        qreal totalH = 0;
        QVector<qreal> lineH(lines.size());
        for (int i = 0; i < lines.size(); ++i) {
            const SlideLine &l = lines.at(i);
            qreal h = 0;
            if (hasChords && !l.chords.isEmpty()) h += chordPx * 1.25;
            if (rich) {
                h += measureTextHeight(f, st, l.text, box, highlight, lineSpacing);
            } else {
                h += fm.boundingRect(QRect(int(box.x()), 0, int(box.width()), 10000),
                                     Qt::TextWordWrap, l.text).height() * lineSpacing;
            }
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
            if (rich) {
                // v1.1.0: ruta QTextDocument (soporta palabras destacadas)
                drawRichLine(p, st, l.text, lineBox, f, hAlign, highlight, base);
            } else {
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
            }
            y += lineH[i];
        }
        p.restore();
    }

    // ------------------------------------------------------------------
    // v1.1.0: superposición de Lower Third — banda semitransparente en la
    // zona inferior con acento dorado, titulo y texto alineados a la
    // izquierda (inspirado en las transmisiones en vivo de Holyrics).
    // ------------------------------------------------------------------
    static void paintLowerThird(QPainter &p, const Theme &theme, const Slide &slide,
                                const QSize &size)
    {
        const int W = size.width();
        const int H = size.height();
        const qreal base = qMin(W, H) / 1080.0;

        const QRectF band(W * 0.05, H * 0.68, W * 0.90, H * 0.26);
        p.save();
        p.setRenderHint(QPainter::Antialiasing, true);

        // Banda principal
        QPainterPath bandPath;
        bandPath.addRoundedRect(band, 18 * base, 18 * base);
        p.fillPath(bandPath, QColor(0, 0, 0, 175));
        p.setPen(QPen(QColor(255, 255, 255, 40), 2));
        p.drawPath(bandPath);

        // Barra de acento (dorado litúrgico)
        const QRectF accent(band.x() + 14 * base, band.y() + 14 * base,
                            9 * base, band.height() - 28 * base);
        QPainterPath accentPath;
        accentPath.addRoundedRect(accent, 4 * base, 4 * base);
        p.fillPath(accentPath, QColor(255, 209, 102, 235));

        // Titulo (opcional)
        const qreal padL = 44 * base;
        const qreal innerW = band.width() - padL - 30 * base;
        qreal yCur = band.y() + 12 * base;
        if (!slide.title.isEmpty()) {
            TextStyle ts = theme.title;
            ts.pointSize = qMax(16, int(ts.pointSize * 0.52));
            ts.align = 1;                    // izquierda
            ts.upperCase = true;
            const QFontMetrics fm(makeFont(ts, qMax(10, int(ts.pointSize * base * (96.0 / 72.0)))));
            const QRectF tBox(band.x() + padL, yCur, innerW, fm.height() * 1.25);
            drawStyledText(p, ts, slide.title, tBox, base);
            yCur += tBox.height() + 4 * base;
        }

        // Lineas de texto (estilo cuerpo, alineado a la izquierda)
        if (!slide.lines.isEmpty()) {
            TextStyle bs = theme.body;
            bs.pointSize = qMax(14, int(bs.pointSize * 0.62));
            bs.align = 1;
            const QRectF bodyBox(band.x() + padL, yCur, innerW,
                                 band.bottom() - yCur - 12 * base);
            int fontPx = qMax(12, int(bs.pointSize * base * (96.0 / 72.0)));
            for (int attempt = 0; attempt < 20; ++attempt) {
                const qreal needed = measureBlock(p, bs, slide.lines, bodyBox, fontPx, false, base,
                                                  slide.highlight);
                if (needed <= bodyBox.height() || fontPx <= 12) break;
                fontPx = qMax(12, int(fontPx * 0.92));
            }
            RenderOptions ro;
            drawBlock(p, bs, slide.lines, bodyBox, fontPx, false, base, ro, slide.highlight);
        }

        // Pie de referencia dentro de la banda (derecha)
        if (!slide.refLabel.isEmpty()) {
            TextStyle rs;
            rs.family = theme.body.family;
            rs.pointSize = 14;
            rs.bold = false;
            rs.color = QColor(255, 255, 255, 165);
            rs.shadow = false;
            rs.align = 2;
            drawStyledText(p, rs, slide.refLabel,
                           QRectF(band.x() + band.width() * 0.55, band.y() + band.height() - 34 * base,
                                  band.width() * 0.42, 26 * base), base);
        }
        p.restore();
    }
};

#endif // LUMINA_RENDERER_H
