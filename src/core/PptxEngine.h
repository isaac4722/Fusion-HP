// ============================================================================
//  LuminaPresentation Suite - Copyright (c) 2026 Isaac. Licencia View-Only.
// ============================================================================
//  PptxEngine.h : Importacion y exportacion de archivos Microsoft PowerPoint
//  (.pptx) mediante el estandar ZIP+XML (OpenXml) SIN Office ni OLE.
//  - Descompresion con miniz (vendorizado).
//  - Parseo XML con QXmlStreamReader.
//  - Extrae: orden de slides, cajas de texto (posicion/tamano/color/tamano
//    de fuente/negrita), formas (rect/redonda/elipse) e imagenes incrustadas.
//  - Exportacion minima: slides de texto con fondo del tema.
// ============================================================================
#ifndef LUMINA_PPTXENGINE_H
#define LUMINA_PPTXENGINE_H

#include "Models.h"

#include <QString>
#include <QVector>
#include <QMap>
#include <QXmlStreamReader>
#include <QFile>
#include <QDir>
#include <QImage>
#include <QBuffer>
#include <QDateTime>
#include <QDebug>

#include "miniz.h"

class PptxEngine
{
public:
    struct Box {
        enum Kind { Text, Shape, Image } kind = Text;
        double x = 0, y = 0, w = 0, h = 0;      // en px sobre lienzo de salida
        QString text;
        int fontSize = 28;                       // pt
        bool bold = false, italic = false;
        QString color = QStringLiteral("#FFFFFF");
        QString prst = QStringLiteral("rect");   // geometria de forma
        QString imagePath;                       // ruta de imagen extraida
        int align = 0;                           // 0 center,1 left,2 right
    };

    struct PptxSlide {
        QVector<Box> boxes;
        QString bgImagePath;      // imagen de fondo (si la slide solo tiene 1 imagen grande)
    };

    // EMU (English Metric Unit) -> px @96dpi : 914400 EMU = 1 inch = 96 px
    static double emuToPx(qint64 emu) { return double(emu) / 9525.0; }

    // ------------------------------ IMPORTACION ------------------------------
    static bool importPptx(const QString &path, QVector<PptxSlide> *slides,
                           QSize *slideSizePx, QString *error)
    {
        if (!slides || !slideSizePx) return false;
        slides->clear();

        mz_zip_archive zip;
        memset(&zip, 0, sizeof(zip));
        const QByteArray p8 = QDir::toNativeSeparators(path).toUtf8();
        if (!mz_zip_reader_init_file(&zip, p8.constData(), 0)) {
            if (error) *error = QStringLiteral("No se pudo abrir el archivo PPTX (ZIP invalido).");
            return false;
        }
        // Extra todo a memoria para acesso aleatorio
        QMap<QString, QByteArray> files;
        const int n = int(mz_zip_reader_get_num_files(&zip));
        for (int i = 0; i < n; ++i) {
            mz_zip_archive_file_stat st;
            if (!mz_zip_reader_file_stat(&zip, mz_uint(i), &st)) continue;
            const QString name = QString::fromUtf8(st.m_filename);
            size_t sz = 0;
            char *data = (char *)mz_zip_reader_extract_to_heap(&zip, mz_uint(i), &sz, 0);
            if (data) { files.insert(name, QByteArray(data, int(sz))); mz_free(data); }
        }
        mz_zip_reader_end(&zip);

        if (!files.contains(QStringLiteral("ppt/presentation.xml"))) {
            if (error) *error = QStringLiteral("El archivo no parece un PPTX valido.");
            return false;
        }

        // Tamano de slide (EMU)
        QSizeF slideEmu(9144000, 6858000);      // default 10x7.5in
        {
            QXmlStreamReader xr(files.value(QStringLiteral("ppt/presentation.xml")));
            while (!xr.atEnd()) {
                xr.readNext();
                if (xr.isStartElement() && xr.name() == QStringLiteral("sldSz")) {
                    const qint64 cx = xr.attributes().value(QStringLiteral("cx")).toLongLong();
                    const qint64 cy = xr.attributes().value(QStringLiteral("cy")).toLongLong();
                    slideEmu = QSizeF(double(cx), double(cy));
                    break;
                }
            }
        }
        const double outW = 1920.0, outH = 1080.0;
        // CORRECCION: emuToPx() ya convierte EMU->px@96dpi. El factor de
        // escala debe ser px-de-salida / px-de-la-slide (NO px/EMU: eso
        // provocaba una doble conversion y geometria ~0 en todas las cajas).
        const double slidePxW = slideEmu.width() / 9525.0;
        const double slidePxH = slideEmu.height() / 9525.0;
        // Presentaciones 4:3 proyectadas en 16:9: escala uniforme + centrado
        const double scale = qMin(outW / slidePxW, outH / slidePxH);
        const double offX = (outW - slidePxW * scale) / 2.0;
        const double offY = (outH - slidePxH * scale) / 2.0;
        *slideSizePx = QSize(int(outW), int(outH));

        // Relaciones presentation -> slides en orden
        QMap<QString, QString> presRels = parseRels(files.value(QStringLiteral("ppt/_rels/presentation.xml.rels")));
        QStringList slidePaths;
        {
            QXmlStreamReader xr(files.value(QStringLiteral("ppt/presentation.xml")));
            while (!xr.atEnd()) {
                xr.readNext();
                if (xr.isStartElement() && xr.name() == QStringLiteral("sldId")) {
                    const QString rid = xr.attributes().value(QStringLiteral("r:id")).toString();
                    const QString target = presRels.value(rid);
                    if (!target.isEmpty()) {
                        QString t = target;
                        if (t.startsWith(QStringLiteral("/"))) t = t.mid(1);
                        else if (!t.startsWith(QStringLiteral("ppt/"))) t = QStringLiteral("ppt/") + t;
                        slidePaths << t;
                    }
                }
            }
        }

        // Directorio temporal para imagenes extraidas
        const QString tmpRoot = QDir::tempPath() + QStringLiteral("/LuminaImports/");
        QDir().mkpath(tmpRoot);
        const QString stamp = QString::number(QDateTime::currentMSecsSinceEpoch());

        for (const QString &sp : slidePaths) {
            PptxSlide outSlide;
            const QByteArray xml = files.value(sp);
            // rels de esta slide -> imagenes
            const QString relsPath = sp.mid(0, sp.lastIndexOf(QChar('/')) + 1) +
                                     QStringLiteral("_rels/") +
                                     sp.mid(sp.lastIndexOf(QChar('/')) + 1) + QStringLiteral(".rels");
            QMap<QString, QString> rels = parseRels(files.value(relsPath));

            // Extrae imagenes referenciadas
            QMap<QString, QString> imageMap;    // rid -> archivo temporal
            for (auto it = rels.constBegin(); it != rels.constEnd(); ++it) {
                const QString target = it.value();
                if (target.contains(QStringLiteral("media/"))) {
                    QString mediaPath = target;
                    if (mediaPath.startsWith(QStringLiteral("../"))) {
                        mediaPath = QStringLiteral("ppt/") + mediaPath.mid(3);
                    } else if (!mediaPath.startsWith(QStringLiteral("ppt/"))) {
                        mediaPath = QStringLiteral("ppt/slides/") + mediaPath;
                    }
                    mediaPath = QDir::cleanPath(mediaPath);
                    const QByteArray data = files.value(mediaPath);
                    if (!data.isEmpty()) {
                        QString ext = mediaPath.mid(mediaPath.lastIndexOf(QChar('.')));
                        if (ext.size() > 6) ext = QStringLiteral(".png");
                        const QString outF = tmpRoot + stamp + QStringLiteral("_s%1_%2%3")
                                .arg(slidePaths.indexOf(sp)).arg(it.key()).arg(ext);
                        QFile f(outF);
                        if (f.open(QIODevice::WriteOnly)) { f.write(data); f.close(); }
                        imageMap.insert(it.key(), outF);
                    }
                }
            }

            parseSlideXml(xml, imageMap, scale, offX, offY, &outSlide);
            slides->append(outSlide);
        }
        return true;
    }

private:
    static QMap<QString, QString> parseRels(const QByteArray &xml)
    {
        QMap<QString, QString> out;
        if (xml.isEmpty()) return out;
        QXmlStreamReader xr(xml);
        while (!xr.atEnd()) {
            xr.readNext();
            if (xr.isStartElement() && xr.name() == QStringLiteral("Relationship")) {
                const QString id = xr.attributes().value(QStringLiteral("Id")).toString();
                const QString target = xr.attributes().value(QStringLiteral("Target")).toString();
                const QString type = xr.attributes().value(QStringLiteral("Type")).toString();
                if (!id.isEmpty() && !target.isEmpty() &&
                    (type.contains(QStringLiteral("image")) || target.contains(QStringLiteral("slide")) ||
                     target.contains(QStringLiteral("media"))))
                    out.insert(id, target);
            }
        }
        return out;
    }

    static void parseSlideXml(const QByteArray &xml, const QMap<QString, QString> &imageMap,
                              double scale, double offX, double offY, PptxSlide *out)
    {
        if (xml.isEmpty()) return;
        QXmlStreamReader xr(xml);

        // NOTA CRITICA: QXmlStreamReader::name() devuelve el nombre LOCAL
        // (sin prefijo de namespace). Los elementos DrawingML "a:xxx" se
        // comparan por nombre local ("t", "off", "ext", ...).
        struct Ctx {
            Box cur;
            QString runText;
            bool inText = false;    // dentro de <a:t>
            bool inRun = false;     // dentro de <a:r> (run de texto)
            int runSize = -1;
            bool runBold = false, runItalic = false;
            QString runColor;
            QString align;
        } c;

        auto flushParagraph = [&]() {
            const QString trimmed = c.runText.trimmed();
            if (!trimmed.isEmpty() && c.cur.kind != Box::Image) {
                if (!c.cur.text.isEmpty()) c.cur.text += QChar('\n');
                c.cur.text += trimmed;
                // Aplica el estilo del ultimo run activo a la caja
                if (c.runSize > 0) c.cur.fontSize = c.runSize;
                c.cur.bold = c.runBold;
                c.cur.italic = c.runItalic;
                if (!c.runColor.isEmpty()) c.cur.color = c.runColor;
            }
            c.runText.clear();
        };

        while (!xr.atEnd()) {
            xr.readNext();
            if (xr.hasError()) break;
            // ---- Caracteres: contenido textual de <a:t> ----
            if (xr.isCharacters() || xr.isCDATA()) {
                if (c.inText) c.runText += xr.text();
                continue;
            }
            const QString name = xr.name().toString();
            if (xr.isStartElement()) {
                if (name == QStringLiteral("sp")) {
                    // Nueva forma / caja de texto
                    flushParagraph();
                    if (c.cur.kind == Box::Text && !c.cur.text.isEmpty()) out->boxes.append(c.cur);
                    c = Ctx();
                    c.cur.kind = Box::Text;
                } else if (name == QStringLiteral("pic")) {
                    flushParagraph();
                    if (c.cur.kind == Box::Text && !c.cur.text.isEmpty()) out->boxes.append(c.cur);
                    c = Ctx();
                    c.cur.kind = Box::Image;
                } else if (name == QStringLiteral("off")) {
                    // Solo la primera transform dentro de la forma actual
                    if (c.cur.w == 0 && c.cur.h == 0) {
                        c.cur.x = emuToPx(xr.attributes().value(QStringLiteral("x")).toLongLong()) * scale + offX;
                        c.cur.y = emuToPx(xr.attributes().value(QStringLiteral("y")).toLongLong()) * scale + offY;
                    }
                } else if (name == QStringLiteral("ext")) {
                    if (c.cur.w == 0 && c.cur.h == 0) {
                        c.cur.w = emuToPx(xr.attributes().value(QStringLiteral("cx")).toLongLong()) * scale;
                        c.cur.h = emuToPx(xr.attributes().value(QStringLiteral("cy")).toLongLong()) * scale;
                    }
                } else if (name == QStringLiteral("prstGeom")) {
                    c.cur.prst = xr.attributes().value(QStringLiteral("prst")).toString();
                } else if (name == QStringLiteral("r")) {
                    c.inRun = true;
                } else if (name == QStringLiteral("rPr")) {
                    const int sz = xr.attributes().value(QStringLiteral("sz")).toInt();
                    if (sz > 0) { c.runSize = sz / 100; c.cur.fontSize = c.runSize; }
                    c.runBold = xr.attributes().value(QStringLiteral("b")).toInt() == 1;
                    c.runItalic = xr.attributes().value(QStringLiteral("i")).toInt() == 1;
                    c.cur.bold = c.runBold; c.cur.italic = c.runItalic;
                } else if (name == QStringLiteral("srgbClr")) {
                    if (c.inRun) {
                        // Color del run de texto (esta dentro de <a:r><a:rPr>)
                        c.runColor = QStringLiteral("#") + xr.attributes().value(QStringLiteral("val")).toString();
                    }
                } else if (name == QStringLiteral("t")) {
                    c.inText = true;
                } else if (name == QStringLiteral("blip")) {
                    QString rid = xr.attributes().value(QStringLiteral("embed")).toString();
                    if (rid.isEmpty())
                        rid = xr.attributes().value(QStringLiteral("r:embed")).toString();
                    c.cur.imagePath = imageMap.value(rid);
                } else if (name == QStringLiteral("pPr")) {
                    const QString al = xr.attributes().value(QStringLiteral("algn")).toString();
                    c.align = al;
                    c.cur.align = (al == QStringLiteral("l")) ? 1 : (al == QStringLiteral("r")) ? 2 : 0;
                }
            } else if (xr.isEndElement()) {
                if (name == QStringLiteral("t")) {
                    c.inText = false;
                    if (!c.runText.isEmpty() && !c.runText.endsWith(QChar(' ')))
                        c.runText += QChar(' ');
                }
                else if (name == QStringLiteral("r")) { c.inRun = false; }
                else if (name == QStringLiteral("p")) { flushParagraph(); }
                else if (name == QStringLiteral("sp") || name == QStringLiteral("pic")) {
                    flushParagraph();
                    if ((c.cur.kind == Box::Image && !c.cur.imagePath.isEmpty()) ||
                        (c.cur.kind == Box::Text && !c.cur.text.simplified().isEmpty())) {
                        // Cajas sin xfrm explicito (heredan de layout): geometria
                        // por defecto centrada para que el texto sea visible.
                        if (c.cur.w <= 0 || c.cur.h <= 0) {
                            c.cur.x = 1920.0 * 0.07;
                            c.cur.y = 1080.0 * 0.25;
                            c.cur.w = 1920.0 * 0.86;
                            c.cur.h = 1080.0 * 0.5;
                        }
                        out->boxes.append(c.cur);
                    }
                    c = Ctx();
                }
            }
        }
    }

public:
    // ------------------------------ EXPORTACION ------------------------------
    // PPTX minimo valido (texto + fondo de color por slide). Sin Office.
    static bool exportPptx(const QVector<Slide> &slides, const Theme &theme,
                           const QString &outPath, QString *error)
    {
        mz_zip_archive zip;
        memset(&zip, 0, sizeof(zip));
        const QByteArray out8 = QDir::toNativeSeparators(outPath).toUtf8();
        if (!mz_zip_writer_init_file(&zip, out8.constData(), 0)) {
            if (error) *error = QStringLiteral("No se pudo crear el archivo de salida.");
            return false;
        }
        auto addFile = [&](const char *name, const QByteArray &data) {
            return mz_zip_writer_add_mem(&zip, name, data.constData(), size_t(data.size()), MZ_DEFAULT_COMPRESSION);
        };

        // [Content_Types].xml
        QByteArray ct =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>";
        for (int i = 0; i < slides.size(); ++i)
            ct += QString("<Override PartName=\"/ppt/slides/slide%1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>").arg(i + 1).toUtf8();
        ct += "</Types>";
        addFile("[Content_Types].xml", ct);

        addFile("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>"
            "</Relationships>");

        addFile("ppt/presentation.xml",
            QString("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
            "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">"
            "<p:sldIdLst>%1</p:sldIdLst>"
            "<p:sldSz cx=\"12192000\" cy=\"6858000\"/>"
            "</p:presentation>").arg([&]() {
                QString ids;
                for (int i = 0; i < slides.size(); ++i)
                    ids += QString("<p:sldId id=\"%1\" r:id=\"rId%2\"/>").arg(256 + i).arg(i + 1);
                return ids;
            }()).toUtf8());

        QByteArray prels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";
        for (int i = 0; i < slides.size(); ++i)
            prels += QString("<Relationship Id=\"rId%1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide%2.xml\"/>")
                        .arg(i + 1).arg(i + 1).toUtf8();
        prels += "</Relationships>";
        addFile("ppt/_rels/presentation.xml.rels", prels);

        for (int i = 0; i < slides.size(); ++i) {
            const Slide &s = slides.at(i);
            QString body;
            for (const SlideLine &l : s.lines) {
                body += QString("<a:p><a:r><a:rPr lang=\"es-ES\" sz=\"%1\" b=\"%2\" dirty=\"0\">"
                                "<a:solidFill><a:srgbClr val=\"%3\"/></a:solidFill></a:rPr>"
                                "<a:t>%4</a:t></a:r></a:p>")
                            .arg(theme.body.pointSize * 100)
                            .arg(theme.body.bold ? 1 : 0)
                            .arg(theme.body.color.name().mid(1))
                            .arg(xmlEscape(l.text));
            }
            const QString bg = theme.background.color1.name().mid(1);
            const QString xml =
                QString("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<p:sld xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">"
                "<p:cSld><p:bg><p:bgPr><a:solidFill><a:srgbClr val=\"%1\"/></a:solidFill>"
                "<a:effectLst/></p:bgPr></p:bg>"
                "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
                "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/>"
                "<a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>"
                "<p:sp><p:nvSpPr><p:cNvPr id=\"2\" name=\"Text\"/><p:cNvSpPr txBox=\"1\"/><p:nvPr/></p:nvSpPr>"
                "<p:spPr><a:xfrm><a:off x=\"971550\" y=\"3048000\"/><a:ext cx=\"10248900\" cy=\"2514600\"/></a:xfrm>"
                "<a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></p:spPr>"
                "<p:txBody><a:bodyPr anchor=\"ctr\"/><a:lstStyle/>%2</p:txBody></p:sp>"
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sld>")
                    .arg(bg).arg(body);
            addFile(QString("ppt/slides/slide%1.xml").arg(i + 1).toUtf8().constData(), xml.toUtf8());
        }

        if (!mz_zip_writer_finalize_archive(&zip)) {
            if (error) *error = QStringLiteral("Fallo al finalizar el ZIP.");
            mz_zip_writer_end(&zip);
            return false;
        }
        mz_zip_writer_end(&zip);
        return true;
    }

private:
    static QString xmlEscape(const QString &s)
    {
        QString out = s;
        out.replace(QChar('&'), QStringLiteral("&amp;"));
        out.replace(QChar('<'), QStringLiteral("&lt;"));
        out.replace(QChar('>'), QStringLiteral("&gt;"));
        out.replace(QChar('"'), QStringLiteral("&quot;"));
        out.replace(QChar('\''), QStringLiteral("&apos;"));
        return out;
    }
};

#endif // LUMINA_PPTXENGINE_H
