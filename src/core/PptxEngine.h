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
//  - v1.6.0: import limita la extraccion a RAM (XML/imagenes utiles, 20 MB
//    max. por entrada), slides rasterizadas como Slide::Image (C3), parseo
//    de grupos y tablas (B4) y export con escritura verificada (B3).
// ============================================================================
#ifndef LUMINA_PPTXENGINE_H
#define LUMINA_PPTXENGINE_H

#include "Models.h"
#include "Renderer.h"

#include <vector>

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
        // CORRECCION v1.2.0: miniz abre los archivos con fopen() ANSI; en
        // Windows, una ruta UTF-8 con "ñ"/acentos ("Canción.pptx",
        // "C:\Users\José\...") NO coincide con la cadena esperada por el
        // codepage y la apertura falla con "ZIP invalido" para archivos
        // perfectamente sanos. En Windows se convierte a Local8Bit (ANSI CP).
#ifdef Q_OS_WIN
        const QByteArray p8 = QDir::toNativeSeparators(path).toLocal8Bit();
#else
        const QByteArray p8 = QDir::toNativeSeparators(path).toUtf8();
#endif
        if (!mz_zip_reader_init_file(&zip, p8.constData(), 0)) {
            if (error) *error = QStringLiteral("No se pudo abrir el archivo PPTX (ZIP invalido).");
            return false;
        }
        // Extrae a memoria (acceso aleatorio) solo las entradas utiles.
        // M23: un PPTX puede traer videos (mp4 de 200-500 MB), audio y
        // fuentes embebidas que el parser NO usa; en equipos de 4 GB de RAM
        // (spec §2.5) volcar el ZIP completo a memoria puede agotarla. Se
        // saltan las entradas con extension ajena a XML/relaciones/imagen
        // de dibujo y toda entrada que supere 20 MB sin comprimir.
        QMap<QString, QByteArray> files;
        const int n = int(mz_zip_reader_get_num_files(&zip));
        const QStringList kImportExts = {
            QStringLiteral(".xml"), QStringLiteral(".rels"),
            QStringLiteral(".png"), QStringLiteral(".jpg"), QStringLiteral(".jpeg"),
            QStringLiteral(".gif"), QStringLiteral(".bmp"),
            QStringLiteral(".tif"), QStringLiteral(".tiff"),
            QStringLiteral(".emf"), QStringLiteral(".wmf")
        };
        const qint64 kMaxEntryBytes = 20ll * 1024 * 1024;
        for (int i = 0; i < n; ++i) {
            mz_zip_archive_file_stat st;
            if (!mz_zip_reader_file_stat(&zip, mz_uint(i), &st)) continue;
            const QString name = QString::fromUtf8(st.m_filename);
            // El filtro por extension ya cubre los XML necesarios:
            // [Content_Types].xml, _rels/*, ppt/* y docProps/*.
            const QString lower = name.toLower();
            bool allowed = false;
            for (const QString &ext : kImportExts) {
                if (lower.endsWith(ext)) { allowed = true; break; }
            }
            if (!allowed) continue;                                    // video/audio/fuente: fuera
            if (qint64(st.m_uncomp_size) > kMaxEntryBytes) continue;   // entrada gigante: fuera
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
        // v1.2.0: purga de temporales antiguos (> 3 días) — cada importación
        // escribía PNGs/imagenes nuevos sin limpiar nunca: crecimiento
        // indefinido de %TEMP% en máquinas de uso diario.
        {
            const QDir d(tmpRoot);
            const auto entries = d.entryInfoList(QDir::Files, QDir::Name);
            const qint64 cutoff = QDateTime::currentMSecsSinceEpoch() - 3ll * 24 * 60 * 60 * 1000;
            int purged = 0;
            for (const QFileInfo &fi : entries) {
                if (fi.lastModified().toMSecsSinceEpoch() < cutoff) {
                    if (QFile::remove(fi.absoluteFilePath())) ++purged;
                }
            }
            if (purged > 0) qInfo() << "[Pptx] Temporales purgados:" << purged;
        }
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

        // B4b: anexa la caja en curso (con geometria por defecto si hereda
        // del layout y no tiene xfrm propio) y reinicia el contexto. Lo
        // usan las formas (sp), las imagenes (pic), la caja "virtual" de
        // las tablas (a:tbl) y el flush final del documento.
        auto appendCurrentBox = [&]() {
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
        };

        // B4a: pila de transformaciones de grupos (<p:grpSp>). Las formas
        // hijas de un grupo expresan sus coordenadas en el espacio
        // chOff/chExt del grupo: hay que remapearlas con el factor
        // ext/chExt al espacio de la slide (protegiendo la division por
        // cero: chExt==0 => factor 1) ANTES de convertir EMU->px. Se aplica
        // de adentro (grupo mas interno) hacia afuera.
        struct GrpXf {
            double offX = 0, offY = 0;       // <a:off>   del grupo (EMU)
            double extX = 0, extY = 0;       // <a:ext>   del grupo (EMU)
            double chOffX = 0, chOffY = 0;   // <a:chOff> del grupo (EMU)
            double chExtX = 0, chExtY = 0;   // <a:chExt> del grupo (EMU)
            bool collecting = true;          // xfrm del grupo aun incompleto
        };
        // std::vector en vez de QVector: evita el falso positivo de GCC
        // -Wstringop-overflow con QVector<T>::append de structs pequeños.
        std::vector<GrpXf> grpStack;
        // Punto (EMU) del espacio hijo -> espacio de la slide (EMU):
        // nuevoX = offX + (hijoX - chOffX) * extX/chExtX (idem Y).
        auto mapEmuPoint = [&grpStack](double ex, double ey, double &mx, double &my) {
            for (int gi = grpStack.size() - 1; gi >= 0; --gi) {
                const GrpXf &g = grpStack.at(gi);
                const double fx = (g.chExtX != 0.0) ? g.extX / g.chExtX : 1.0;
                const double fy = (g.chExtY != 0.0) ? g.extY / g.chExtY : 1.0;
                ex = g.offX + (ex - g.chOffX) * fx;
                ey = g.offY + (ey - g.chOffY) * fy;
            }
            mx = ex;
            my = ey;
        };
        // Tamano (EMU) hijo -> slide: solo escala (los off/chOff no aplican
        // a dimensiones), factor ext/chExt por eje.
        auto mapEmuSize = [&grpStack](double ex, double ey, double &mx, double &my) {
            for (int gi = grpStack.size() - 1; gi >= 0; --gi) {
                const GrpXf &g = grpStack.at(gi);
                ex *= (g.chExtX != 0.0) ? g.extX / g.chExtX : 1.0;
                ey *= (g.chExtY != 0.0) ? g.extY / g.chExtY : 1.0;
            }
            mx = ex;
            my = ey;
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
                    if (!grpStack.empty()) grpStack.back().collecting = false;
                    appendCurrentBox();
                    c.cur.kind = Box::Text;
                } else if (name == QStringLiteral("pic")) {
                    if (!grpStack.empty()) grpStack.back().collecting = false;
                    appendCurrentBox();
                    c.cur.kind = Box::Image;
                } else if (name == QStringLiteral("grpSp")) {
                    // B4a: forma agrupada — push de transformacion. El xfrm
                    // del grupo (off/ext/chOff/chExt) llega justo despues y
                    // NO debe tomarse como geometria de una caja.
                    if (!grpStack.empty()) grpStack.back().collecting = false;
                    grpStack.emplace_back();
                } else if (name == QStringLiteral("off")) {
                    if (!grpStack.empty() && grpStack.back().collecting) {
                        // B4a: es el <a:off> del propio grupo
                        grpStack.back().offX = double(xr.attributes().value(QStringLiteral("x")).toLongLong());
                        grpStack.back().offY = double(xr.attributes().value(QStringLiteral("y")).toLongLong());
                    } else if (c.cur.w == 0 && c.cur.h == 0) {
                        // Solo la primera transform dentro de la forma actual
                        double mx = 0.0, my = 0.0;
                        mapEmuPoint(double(xr.attributes().value(QStringLiteral("x")).toLongLong()),
                                    double(xr.attributes().value(QStringLiteral("y")).toLongLong()), mx, my);
                        c.cur.x = emuToPx(qRound64(mx)) * scale + offX;
                        c.cur.y = emuToPx(qRound64(my)) * scale + offY;
                    }
                } else if (name == QStringLiteral("ext")) {
                    if (!grpStack.empty() && grpStack.back().collecting) {
                        // B4a: es el <a:ext> del propio grupo
                        grpStack.back().extX = double(xr.attributes().value(QStringLiteral("cx")).toLongLong());
                        grpStack.back().extY = double(xr.attributes().value(QStringLiteral("cy")).toLongLong());
                    } else if (c.cur.w == 0 && c.cur.h == 0) {
                        double mx = 0.0, my = 0.0;
                        mapEmuSize(double(xr.attributes().value(QStringLiteral("cx")).toLongLong()),
                                   double(xr.attributes().value(QStringLiteral("cy")).toLongLong()), mx, my);
                        c.cur.w = emuToPx(qRound64(mx)) * scale;
                        c.cur.h = emuToPx(qRound64(my)) * scale;
                    }
                } else if (name == QStringLiteral("chOff")) {
                    // B4a: origen del espacio de coordenadas de los hijos
                    if (!grpStack.empty() && grpStack.back().collecting) {
                        grpStack.back().chOffX = double(xr.attributes().value(QStringLiteral("x")).toLongLong());
                        grpStack.back().chOffY = double(xr.attributes().value(QStringLiteral("y")).toLongLong());
                    }
                } else if (name == QStringLiteral("chExt")) {
                    if (!grpStack.empty() && grpStack.back().collecting) {
                        grpStack.back().chExtX = double(xr.attributes().value(QStringLiteral("cx")).toLongLong());
                        grpStack.back().chExtY = double(xr.attributes().value(QStringLiteral("cy")).toLongLong());
                        // xfrm del grupo completo: los off/ext que siguen ya
                        // pertenecen a las formas hijas.
                        grpStack.back().collecting = false;
                    }
                } else if (name == QStringLiteral("tbl")) {
                    // B4b: tabla (a:tbl) — caja de texto "virtual": cada
                    // parrafo/celda se acumula como linea de la misma caja
                    // (antes el texto de las tablas se perdia).
                    if (!grpStack.empty()) grpStack.back().collecting = false;
                    appendCurrentBox();
                    c.cur.kind = Box::Text;
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
                    appendCurrentBox();
                }
                else if (name == QStringLiteral("grpSp")) {
                    // B4a: fin de grupo — pop de la transformacion
                    if (!grpStack.empty()) grpStack.pop_back();
                }
                else if (name == QStringLiteral("tbl")) {
                    // B4b: fin de tabla — anexa la caja virtual acumulada
                    appendCurrentBox();
                }
            }
        }
        // B4b: flush final — si quedo texto pendiente al terminar el
        // documento (XML truncado o caja sin cerrar), se anade como caja
        // del slide en vez de perderse.
        appendCurrentBox();
    }

public:
    // --------------------------------------------------------------------
    // v1.1.0: rasteriza slides PPTX importadas a PNG temporal para poder
    // proyectarlas. Unica implementacion compartida por el panel y por la
    // ejecucion de items de culto en cola (antes el item guardado no podia
    // ejecutarse).
    // C3: las slides resultantes se marcan Slide::Image — Renderer y
    // DisplayEngine solo dibujan el mediaPath cuando kind == Slide::Image;
    // con el kind antiguo (Slide::Pptx) el PPTX proyectado salia en blanco
    // (solo fondo+titulo). Patron identico al de CustomPanel v1.2.0.
    // --------------------------------------------------------------------
    static QVector<Slide> renderToSlides(const QVector<PptxSlide> &slides,
                                         const QSize &slideSizePx, const Theme &theme,
                                         const QString &nameForTitle)
    {
        QVector<Slide> out;
        if (slides.isEmpty()) return out;
        const QString dir = QDir::tempPath() + QStringLiteral("/LuminaImports/");
        QDir().mkpath(dir);
        const qint64 stamp = QDateTime::currentMSecsSinceEpoch();
        for (int si = 0; si < slides.size(); ++si) {
            const PptxSlide &ps = slides.at(si);
            QPixmap pm(slideSizePx);
            pm.fill(QColor(255, 255, 255));
            QPainter p(&pm);
            p.setRenderHint(QPainter::Antialiasing, true);
            p.setRenderHint(QPainter::TextAntialiasing, true);
            p.setRenderHint(QPainter::SmoothPixmapTransform, true);
            for (const Box &b : ps.boxes) {
                QRectF box(b.x, b.y, b.w, b.h);
                if (b.kind == Box::Image) {
                    QImage img(b.imagePath);
                    if (!img.isNull())
                        Renderer::drawImageFit(p, img, box.toRect(), Qt::KeepAspectRatio);
                } else if (b.kind == Box::Shape) {
                    QBrush fill(QColor(30, 60, 140, 200));
                    QPen pen(QColor(60, 140, 255), 3);
                    if (b.prst == QStringLiteral("ellipse"))
                        p.setBrush(fill), p.setPen(pen), p.drawEllipse(box);
                    else if (b.prst == QStringLiteral("roundRect"))
                        p.setBrush(fill), p.setPen(pen), p.drawRoundedRect(box, 18, 18);
                    else
                        p.setBrush(fill), p.setPen(pen), p.drawRect(box);
                } else {
                    TextStyle st = theme.body;
                    st.pointSize = b.fontSize;
                    st.bold = b.bold;
                    st.italic = b.italic;
                    st.color = QColor(b.color);
                    st.align = b.align;
                    st.shadow = true;
                    Renderer::drawStyledText(p, st, b.text, box, 1.0);
                }
            }
            p.end();
            const QString png = dir + QStringLiteral("render_%1_%2.png")
                                    .arg(stamp + si).arg(QDateTime::currentMSecsSinceEpoch());
            pm.save(png, "PNG");
            Slide s;
            // C3: kind Image para que Renderer/DisplayEngine dibujen el PNG
            // de mediaPath; titulo y refLabel se conservan igual.
            s.kind = Slide::Image;
            s.title = nameForTitle;
            s.refLabel = QStringLiteral("Slide %1/%2").arg(si + 1).arg(slides.size());
            s.mediaPath = png;
            out.append(s);
        }
        return out;
    }

public:
    // ------------------------------ EXPORTACION ------------------------------
    // PPTX minimo valido (texto + fondo de color por slide). Sin Office.
    static bool exportPptx(const QVector<Slide> &slides, const Theme &theme,
                           const QString &outPath, QString *error)
    {
        mz_zip_archive zip;
        memset(&zip, 0, sizeof(zip));
        // CORRECCION v1.2.0: idem importación — fopen ANSI: en Windows la ruta
        // de salida debe ir en Local8Bit o los PPTX exportados a carpetas con
        // acentos/"ñ" no se crean.
#ifdef Q_OS_WIN
        const QByteArray out8 = QDir::toNativeSeparators(outPath).toLocal8Bit();
#else
        const QByteArray out8 = QDir::toNativeSeparators(outPath).toUtf8();
#endif
        if (!mz_zip_writer_init_file(&zip, out8.constData(), 0)) {
            if (error) *error = QStringLiteral("No se pudo crear el archivo de salida.");
            return false;
        }
        // B3a: addFile comprueba el retorno de mz_zip_writer_add_mem; si
        // falla (disco lleno, error de E/S) se aborta la exportacion.
        auto addFile = [&](const char *name, const QByteArray &data) {
            if (!mz_zip_writer_add_mem(&zip, name, data.constData(), size_t(data.size()), MZ_DEFAULT_COMPRESSION)) {
                if (error) *error = QStringLiteral("Fallo al escribir «%1» en el ZIP.").arg(QString::fromUtf8(name));
                return false;
            }
            return true;
        };
        // Cierra el escritor y aborta (sin finalize: el ZIP quedaria trunco).
        auto abortExport = [&zip]() { mz_zip_writer_end(&zip); return false; };

        // [Content_Types].xml — v1.5.0: incluye theme + slideMaster +
        // slideLayout (herencia de 4 niveles, spec §4.3).
        QByteArray ct =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">"
            "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>"
            "<Default Extension=\"xml\" ContentType=\"application/xml\"/>"
            "<Override PartName=\"/ppt/presentation.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.presentation.main+xml\"/>"
            "<Override PartName=\"/ppt/theme/theme1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.theme+xml\"/>"
            "<Override PartName=\"/ppt/slideMasters/slideMaster1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideMaster+xml\"/>"
            "<Override PartName=\"/ppt/slideLayouts/slideLayout1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slideLayout+xml\"/>";
        for (int i = 0; i < slides.size(); ++i)
            ct += QString("<Override PartName=\"/ppt/slides/slide%1.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.presentationml.slide+xml\"/>").arg(i + 1).toUtf8();
        ct += "</Types>";
        if (!addFile("[Content_Types].xml", ct)) return abortExport();

        if (!addFile("_rels/.rels",
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"ppt/presentation.xml\"/>"
            "</Relationships>")) return abortExport();

        // v1.5.0: el maestro es rId1 y las slides empiezan en rId2 (el orden
        // del esquema exige sldMasterIdLst ANTES de sldIdLst; el id del
        // maestro debe ser >= 2147483648).
        if (!addFile("ppt/presentation.xml",
            QString("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<p:presentation xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
            "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">"
            "<p:sldMasterIdLst><p:sldMasterId id=\"2147483648\" r:id=\"rId1\"/></p:sldMasterIdLst>"
            "<p:sldIdLst>%1</p:sldIdLst>"
            "<p:sldSz cx=\"12192000\" cy=\"6858000\"/>"
            "</p:presentation>").arg([&]() {
                QString ids;
                for (int i = 0; i < slides.size(); ++i)
                    ids += QString("<p:sldId id=\"%1\" r:id=\"rId%2\"/>").arg(256 + i).arg(i + 2);
                return ids;
            }()).toUtf8())) return abortExport();

        QByteArray prels = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
            "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"slideMasters/slideMaster1.xml\"/>";
        for (int i = 0; i < slides.size(); ++i)
            prels += QString("<Relationship Id=\"rId%1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slide\" Target=\"slides/slide%2.xml\"/>")
                        .arg(i + 2).arg(i + 1).toUtf8();
        prels += "</Relationships>";
        if (!addFile("ppt/_rels/presentation.xml.rels", prels)) return abortExport();

        // ----------------- v1.5.0: NIVEL 1 — TEMA (theme1.xml) -----------------
        // Paleta derivada del tema activo (spec §4.3: Tema -> Maestro ->
        // Diseño -> Diapositiva). clrScheme/fontScheme/fmtScheme completos y
        // validos para PowerPoint (3 fills, 3 lines, 3 effects, 3 bg fills).
        {
            const QString dk = theme.body.color.name().mid(1);
            const QString lt = theme.background.color1.name().mid(1);
            const QString ac = theme.title.color.name().mid(1);
            if (!addFile("ppt/theme/theme1.xml",
                QStringLiteral(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"Lumina\">"
                "<a:themeElements>"
                "<a:clrScheme name=\"Lumina\">"
                "<a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1>"
                "<a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>"
                "<a:dk2><a:srgbClr val=\"%1\"/></a:dk2>"
                "<a:lt2><a:srgbClr val=\"%2\"/></a:lt2>"
                "<a:accent1><a:srgbClr val=\"%3\"/></a:accent1>"
                "<a:accent2><a:srgbClr val=\"%3\"/></a:accent2>"
                "<a:accent3><a:srgbClr val=\"%3\"/></a:accent3>"
                "<a:accent4><a:srgbClr val=\"%3\"/></a:accent4>"
                "<a:accent5><a:srgbClr val=\"%3\"/></a:accent5>"
                "<a:accent6><a:srgbClr val=\"%3\"/></a:accent6>"
                "<a:hlink><a:srgbClr val=\"2D7DFF\"/></a:hlink>"
                "<a:folHlink><a:srgbClr val=\"8C8C8C\"/></a:folHlink>"
                "</a:clrScheme>"
                "<a:fontScheme name=\"Lumina\">"
                "<a:majorFont><a:latin typeface=\"Calibri Light\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:majorFont>"
                "<a:minorFont><a:latin typeface=\"Calibri\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/></a:minorFont>"
                "</a:fontScheme>"
                "<a:fmtScheme name=\"Lumina\">"
                "<a:fillStyleLst>"
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
                "<a:gradFill rotWithShape=\"1\"><a:gsLst>"
                "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"100000\"/></a:schemeClr></a:gs>"
                "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"100000\"/></a:schemeClr></a:gs>"
                "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>"
                "<a:gradFill rotWithShape=\"1\"><a:gsLst>"
                "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"100000\"/></a:schemeClr></a:gs>"
                "<a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:tint val=\"74000\"/></a:schemeClr></a:gs>"
                "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"100000\"/></a:schemeClr></a:gs>"
                "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>"
                "</a:fillStyleLst>"
                "<a:lnStyleLst>"
                "<a:ln w=\"6350\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>"
                "<a:ln w=\"12700\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>"
                "<a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill></a:ln>"
                "</a:lnStyleLst>"
                "<a:effectStyleLst>"
                "<a:effectStyle><a:effectLst/></a:effectStyle>"
                "<a:effectStyle><a:effectLst/></a:effectStyle>"
                "<a:effectStyle><a:effectLst/></a:effectStyle>"
                "</a:effectStyleLst>"
                "<a:bgFillStyleLst>"
                "<a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>"
                "<a:solidFill><a:schemeClr val=\"phClr\"><a:tint val=\"95000\"/></a:schemeClr></a:solidFill>"
                "<a:gradFill rotWithShape=\"1\"><a:gsLst>"
                "<a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"95000\"/></a:schemeClr></a:gs>"
                "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"100000\"/></a:schemeClr></a:gs>"
                "</a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>"
                "</a:bgFillStyleLst>"
                "</a:fmtScheme>"
                "</a:themeElements>"
                "<a:objectDefaults/><a:extraClrSchemeLst/></a:theme>")
                    .arg(dk, lt, ac).toUtf8())) return abortExport();

            // ------------- v1.5.0: NIVEL 2 — MAESTRO (slideMaster1.xml) --------
            // Fondo heredado del tema (lt1); clrMap estándar; layout rId1.
            if (!addFile("ppt/slideMasters/slideMaster1.xml",
                QStringLiteral(
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<p:sldMaster xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\">"
                "<p:cSld><p:bg><p:bgPr><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill>"
                "<a:effectLst/></p:bgPr></p:bg>"
                "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
                "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/>"
                "<a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>"
                "</p:spTree></p:cSld>"
                "<p:clrMap bg1=\"lt1\" tx1=\"dk1\" bg2=\"lt2\" tx2=\"dk2\" accent1=\"accent1\" "
                "accent2=\"accent2\" accent3=\"accent3\" accent4=\"accent4\" accent5=\"accent5\" "
                "accent6=\"accent6\" hlink=\"hlink\" folHlink=\"folHlink\"/>"
                "<p:sldLayoutIdLst><p:sldLayoutId id=\"2147483649\" r:id=\"rId1\"/></p:sldLayoutIdLst>"
                "<p:txStyles>"
                "<p:titleStyle><a:lvl1pPr><a:defRPr sz=\"4000\"/></a:lvl1pPr></p:titleStyle>"
                "<p:bodyStyle><a:lvl1pPr><a:defRPr sz=\"2400\"/></a:lvl1pPr></p:bodyStyle>"
                "<p:otherStyle><a:lvl1pPr><a:defRPr sz=\"2400\"/></a:lvl1pPr></p:otherStyle>"
                "</p:txStyles></p:sldMaster>").toUtf8())) return abortExport();
            if (!addFile("ppt/slideMasters/_rels/slideMaster1.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" Target=\"../slideLayouts/slideLayout1.xml\"/>"
                "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/theme\" Target=\"../theme/theme1.xml\"/>"
                "</Relationships>")) return abortExport();

            // ------------- v1.5.0: NIVEL 3 — DISEÑO (slideLayout1.xml) ----------
            // Diseño "blank" heredando TODO del maestro (la cascada completa:
            // slide -> layout -> master -> theme; la slide conserva su
            // override de fondo sólido como dicta el modelo de herencia).
            if (!addFile("ppt/slideLayouts/slideLayout1.xml",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<p:sldLayout xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" "
                "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" "
                "xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" "
                "type=\"blank\" preserve=\"1\">"
                "<p:cSld name=\"Lumina\">"
                "<p:spTree><p:nvGrpSpPr><p:cNvPr id=\"1\" name=\"\"/><p:cNvGrpSpPr/><p:nvPr/></p:nvGrpSpPr>"
                "<p:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"0\" cy=\"0\"/>"
                "<a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"0\" cy=\"0\"/></a:xfrm></p:grpSpPr>"
                "</p:spTree></p:cSld><p:clrMapOvr><a:masterClrMapping/></p:clrMapOvr></p:sldLayout>")) return abortExport();
            if (!addFile("ppt/slideLayouts/_rels/slideLayout1.xml.rels",
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideMaster\" Target=\"../slideMasters/slideMaster1.xml\"/>"
                "</Relationships>")) return abortExport();
        }

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
            // B3b: un txBody sin ningun <a:p> (slide sin lineas, p.ej. una
            // slide de imagen) produce un PPTX que PowerPoint pide reparar:
            // emitir siempre al menos un parrafo vacio valido.
            if (body.isEmpty())
                body = QStringLiteral("<a:p><a:endParaRPr lang=\"es-VE\"/></a:p>");
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
            if (!addFile(QString("ppt/slides/slide%1.xml").arg(i + 1).toUtf8().constData(), xml.toUtf8()) ||
                // v1.5.0: rel de la slide -> slideLayout1 (nivel 4 de la cascada)
                !addFile(QString("ppt/slides/_rels/slide%1.xml.rels").arg(i + 1).toUtf8().constData(),
                "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>"
                "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">"
                "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/slideLayout\" "
                "Target=\"../slideLayouts/slideLayout1.xml\"/>"
                "</Relationships>"))
                return abortExport();
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
        // B3c: XML 1.0 rechaza los caracteres de control 0x00-0x08, 0x0B,
        // 0x0C y 0x0E-0x1F; si llegan aqui (texto pegado de Word, descargas,
        // PDFs) el paquete entero deja de abrirse. Se sustituyen por espacio
        // y solo se conserva '\n' (salto de linea real) entre los controles.
        QString out;
        out.reserve(s.size());
        for (const QChar &ch : s) {
            const ushort u = ch.unicode();
            out += (u < 0x20 && u != 0x0A) ? QChar(0x20) : ch;
        }
        out.replace(QChar('&'), QStringLiteral("&amp;"));
        out.replace(QChar('<'), QStringLiteral("&lt;"));
        out.replace(QChar('>'), QStringLiteral("&gt;"));
        out.replace(QChar('"'), QStringLiteral("&quot;"));
        out.replace(QChar('\''), QStringLiteral("&apos;"));
        return out;
    }
};

#endif // LUMINA_PPTXENGINE_H
